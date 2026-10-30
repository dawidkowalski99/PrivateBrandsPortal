using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PrivateBrandsPortal.Web.Data;
using PrivateBrandsPortal.Web.Models.Entities;
using PrivateBrandsPortal.Web.Models.Enums;
namespace PrivateBrandsPortal.Web.Services;

public static class WorkflowNotifications
{
    public static async Task EnqueueAsync(ApplicationDbContext db, int projectId, int? productId,
        string eventType, int actorId, DateTimeOffset occurredAt, NotificationAudience audience,
        string? comment, string? relativePath, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Workflow notifications require the business transaction.");
        var project = await db.Projects.AsNoTracking().Where(x => x.Id == projectId)
            .Select(x => new { x.ProjectNumber, x.Customer, x.ProjectManagerId }).SingleAsync(ct);
        var actor = await db.AppUsers.AsNoTracking().Where(x => x.Id == actorId)
            .Select(x => x.DisplayName).SingleAsync(ct);
        var sku = productId.HasValue ? await db.ProjectProducts.AsNoTracking().Where(x => x.Id == productId && x.ProjectId == projectId)
            .Select(x => x.SKU).SingleAsync(ct) : null;
        var identity = $"{eventType}|{projectId}|{productId}|{occurredAt.UtcTicks}|{audience}|{project.ProjectManagerId}";
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        if (db.NotificationOutbox.Local.Any(x => x.EventKey == key) || await db.NotificationOutbox.AnyAsync(x => x.EventKey == key, ct)) return;
        var row = new NotificationOutbox {
            EventKey = key, EventType = eventType, ProjectId = projectId, ProductId = productId,
            ActorUserId = actorId, TargetUserId = project.ProjectManagerId, Audience = audience,
            Subject = $"{project.ProjectNumber} — {eventType}",
            Body = $"Project: {project.ProjectNumber}\nCustomer: {project.Customer}\nEvent: {eventType}\nActor: {actor}\nSKU: {sku ?? "Whole project"}\nComment / reason: {comment ?? "—"}\nOccurred (UTC): {occurredAt:O}",
            RelativePath = relativePath ?? $"Projects/Details/{projectId}",
            CreatedAtUtc = occurredAt, NextAttemptAtUtc = occurredAt, Status = NotificationStatus.Pending
        };
        var error = await ResolveRecipientAsync(db, row, ct);
        if (error is not null) { row.Status = NotificationStatus.Blocked; row.LastError = error; }
        db.NotificationOutbox.Add(row);
        await db.SaveChangesAsync(ct);
    }

    public static async Task<string?> ResolveRecipientAsync(ApplicationDbContext db, NotificationOutbox row, CancellationToken ct)
    {
        // Once a delivery has been attempted, freeze recipient identity and address for safe retries.
        var id = row.RecipientUserId;
        if (row.Attempts == 0 || !id.HasValue) {
            id = row.Audience == NotificationAudience.ProjectManager ? row.TargetUserId :
                await db.AppUsers.Where(x => x.Id == row.TargetUserId).Select(x => x.NotificationApproverId).SingleOrDefaultAsync(ct);
        }
        if (!id.HasValue) return "No notification approver assigned to the project owner.";
        var user = await db.AppUsers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (user is null || !user.IsActive) return "Recipient is missing or inactive.";
        if (row.Audience != NotificationAudience.ProjectManager && user.Role is not (AppRole.Manager or AppRole.SuperAdmin))
            return "Assigned notification approver is not a Manager or SuperAdmin.";
        if (row.Audience == NotificationAudience.ImplementationApprover && user.Id == row.TargetUserId && user.Role != AppRole.SuperAdmin)
            return "Manager cannot approve their own implementation.";
        var address = row.Attempts > 0 && row.RecipientEmail is not null ? row.RecipientEmail : user.Email?.Trim();
        if (string.IsNullOrWhiteSpace(address) || address.Length > 256 || address.Contains('\r') || address.Contains('\n') ||
            !MailAddress.TryCreate(address, out var parsed) || !string.Equals(parsed.Address, address, StringComparison.OrdinalIgnoreCase))
            return "Recipient has no valid email address.";
        row.RecipientUserId = user.Id; row.RecipientEmail = address;
        return null;
    }
}
