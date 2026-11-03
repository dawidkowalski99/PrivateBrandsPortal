using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PrivateBrandsPortal.Web.Configuration;
using PrivateBrandsPortal.Web.Data;
using PrivateBrandsPortal.Web.Interfaces;
using PrivateBrandsPortal.Web.Models.Entities;
namespace PrivateBrandsPortal.Web.Services;

public sealed class NotificationDispatcher(ApplicationDbContext db, IEmailSender sender,
    IOptions<NotificationOptions> options, TimeProvider clock, ILogger<NotificationDispatcher> logger)
{
    private IQueryable<NotificationOutbox> Due(DateTimeOffset now) => db.NotificationOutbox.Where(x =>
        (x.Status == NotificationStatus.Pending || x.Status == NotificationStatus.Retry ||
         x.Status == NotificationStatus.Blocked || x.Status == NotificationStatus.Processing) && x.NextAttemptAtUtc <= now);

    public async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        var ids = await Due(clock.GetUtcNow()).AsNoTracking().OrderBy(x => x.NextAttemptAtUtc).ThenBy(x => x.Id)
            .Select(x => x.Id).Take(options.Value.BatchSize).ToListAsync(ct);
        var count = 0;
        foreach (var id in ids) if (await ProcessAsync(id, ct)) count++;
        return count;
    }

    public async Task<bool> ProcessAsync(long id, CancellationToken ct)
    {
        // Session-owned SQL lock serializes workers without holding a business transaction during IO.
        // Broken worker connections release the lock; Processing records become eligible after timeout.
        await db.Database.OpenConnectionAsync(ct);
        var connection = db.Database.GetDbConnection();
        var resource = $"PrivateBrandsPortal.Notification.{id}";
        var locked = false;
        try {
            await using var command = connection.CreateCommand();
            command.CommandText = "DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=@resource, @LockMode='Exclusive', @LockOwner='Session', @LockTimeout=0; SELECT @r;";
            var parameter = command.CreateParameter(); parameter.ParameterName = "@resource"; parameter.Value = resource; command.Parameters.Add(parameter);
            locked = Convert.ToInt32(await command.ExecuteScalarAsync(ct)) >= 0;
            if (!locked) return false;
            db.ChangeTracker.Clear();
            var row = await Due(clock.GetUtcNow()).SingleOrDefaultAsync(x => x.Id == id, ct);
            if (row is null) return false;
            var error = await WorkflowNotifications.ResolveRecipientAsync(db, row, ct);
            if (error is not null) {
                row.Status = NotificationStatus.Blocked; row.LastError = error;
                row.NextAttemptAtUtc = clock.GetUtcNow().AddMinutes(5);
                await db.SaveChangesAsync(ct); return false;
            }
            if (row.Attempts >= options.Value.MaxAttempts) {
                row.Status = NotificationStatus.Failed; row.LastError = "Maximum delivery attempts reached; manual review required.";
                await db.SaveChangesAsync(ct); return false;
            }
            row.Attempts++; row.LastAttemptAtUtc = clock.GetUtcNow(); row.Status = NotificationStatus.Processing;
            row.NextAttemptAtUtc = clock.GetUtcNow().AddMinutes(2);
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Notification {Id} event {EventType} attempt {Attempt} to {Recipient} started.", row.Id, row.EventType, row.Attempts, row.RecipientEmail);
            using var logScope = logger.BeginScope(new Dictionary<string, object> { ["OutboxId"] = row.Id, ["EventType"] = row.EventType });
            try {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                var message = NotificationEmailRenderer.Render(row, options.Value);
                var result = await sender.SendAsync(message, timeout.Token);
                row.Status = result == EmailSendResult.Delivered ? NotificationStatus.Sent : NotificationStatus.Simulated;
                row.ProcessedAtUtc = clock.GetUtcNow();
                row.SentAtUtc = result == EmailSendResult.Delivered ? clock.GetUtcNow() : null;
                row.LastError = null;
                await db.SaveChangesAsync(ct);
                logger.LogInformation("Notification {Id} event {EventType} to {Recipient} completed as {Status}.", row.Id, row.EventType, row.RecipientEmail, row.Status);
                return true;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) {
                row.Status = row.Attempts >= options.Value.MaxAttempts ? NotificationStatus.Failed : NotificationStatus.Retry;
                // Sender exception messages can contain credentials or recipient addresses; persist only safe diagnostics.
                row.LastError = ex is EmailDeliveryException safe ? safe.Message[..Math.Min(500, safe.Message.Length)] : $"Delivery failed ({ex.GetType().Name}); retry uses the same idempotency key.";
                row.NextAttemptAtUtc = clock.GetUtcNow().AddSeconds(Math.Min(3600, options.Value.RetrySeconds * Math.Pow(2, row.Attempts - 1)));
                if (ex is EmailDeliveryException { RetryAfter: { } retryAfter } && retryAfter > TimeSpan.Zero) {
                    var now = clock.GetUtcNow();
                    var suggested = retryAfter > DateTimeOffset.MaxValue - now ? DateTimeOffset.MaxValue : now.Add(retryAfter);
                    if (suggested > row.NextAttemptAtUtc) row.NextAttemptAtUtc = suggested;
                }
                logger.LogWarning("Notification {Id} event {EventType} to {Recipient} attempt {Attempt} failed ({ErrorType}).", row.Id, row.EventType, row.RecipientEmail, row.Attempts, ex.GetType().Name);
                await db.SaveChangesAsync(ct); return false;
            }
        }
        finally {
            try {
                if (locked && connection.State == ConnectionState.Open) {
                    await using var release = connection.CreateCommand();
                    release.CommandText = "EXEC sys.sp_releaseapplock @Resource=@resource, @LockOwner='Session';";
                    var p = release.CreateParameter(); p.ParameterName = "@resource"; p.Value = resource; release.Parameters.Add(p);
                    await release.ExecuteNonQueryAsync(CancellationToken.None);
                }
            } finally { await db.Database.CloseConnectionAsync(); }
        }
    }
}

public sealed class NotificationWorker(IServiceScopeFactory scopes, IOptions<NotificationOptions> options,
    ILogger<NotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.WorkerEnabled) return;
        while (!stoppingToken.IsCancellationRequested) {
            try {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<NotificationDispatcher>().ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError("Notification worker failed ({ErrorType}); next poll will retry.", ex.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromSeconds(options.Value.PollSeconds), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
