using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PrivateBrandsPortal.Web.Configuration;
using PrivateBrandsPortal.Web.Data;
using PrivateBrandsPortal.Web.Interfaces;
using PrivateBrandsPortal.Web.Models.Entities;
using PrivateBrandsPortal.Web.Models.Enums;
using PrivateBrandsPortal.Web.Services;
namespace PrivateBrandsPortal.Tests;

[Collection("SQL integration")]
public sealed class NotificationTests
{
    private sealed class Sender : IEmailSender
    {
        public bool Fail { get; set; }
        public List<EmailMessage> Messages { get; } = [];
        public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct) {
            Messages.Add(message);
            return Fail ? Task.FromException<EmailSendResult>(new IOException("Secret transport details")) : Task.FromResult(EmailSendResult.Delivered);
        }
    }
    private static NotificationDispatcher Dispatcher(ApplicationDbContext db, IEmailSender sender, int maxAttempts=5) =>
        new(db, sender, Options.Create(new NotificationOptions { MaxAttempts=maxAttempts }), TimeProvider.System, NullLogger<NotificationDispatcher>.Instance);
    private static async Task<int> Email(ProjectSqlTests.Scope s, AppRole role=AppRole.ProjectManager) {
        var user=await s.Users.GetCurrentAsync();
        await s.Db.AppUsers.Where(x=>x.Id==user.Id).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.Email,$"test-{user.Id}@example.invalid").SetProperty(u=>u.Role,role));
        return user.Id;
    }
    private static async Task<int> Route(ProjectSqlTests.Scope owner, ProjectSqlTests.Scope reviewer) {
        var id=await Email(reviewer,AppRole.Manager);var user=await Email(owner);
        await owner.Db.AppUsers.Where(x=>x.Id==user).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.NotificationApproverId,id));return id;
    }
    private static ApprovalService Approvals(ProjectSqlTests.Scope s) => new(s.Db,s.Users,TimeProvider.System,DemoAccessTests.Access(enabled: false));
    private static async Task<int> Submit(ProjectSqlTests.Scope s) {
        var id=await s.Projects.SaveDraftAsync(WizardTests.ValidDraft());
        await Approvals(s).SubmitAsync(id,(await s.Projects.DetailsAsync(id))!.UpdatedAtUtc);return id;
    }
    private static Task<NotificationOutbox> Row(ProjectSqlTests.Scope s,int project,string type) =>
        s.Db.NotificationOutbox.AsNoTracking().SingleAsync(x=>x.ProjectId==project && x.EventType==type);

    [Fact]
    public async Task Draft_is_silent_submission_targets_only_assigned_reviewer_and_replay_is_blocked() {
        await using var s=new ProjectSqlTests.Scope();await using var reviewer=new ProjectSqlTests.Scope();
        var recipient=await Route(s,reviewer);var id=await s.Projects.SaveDraftAsync(WizardTests.ValidDraft());
        Assert.Empty(await s.Db.NotificationOutbox.Where(x=>x.ProjectId==id).ToListAsync());
        var version=(await s.Projects.DetailsAsync(id))!.UpdatedAtUtc;
        await Approvals(s).SubmitAsync(id,version);
        var row=await Row(s,id,"ProjectSubmitted");
        Assert.Equal(recipient,row.RecipientUserId);Assert.Equal(NotificationStatus.Pending,row.Status);
        Assert.Equal($"Approvals/Review/{id}",row.RelativePath);
        await Assert.ThrowsAsync<ValidationException>(()=>Approvals(s).SubmitAsync(id,version));
        Assert.Single(await s.Db.NotificationOutbox.Where(x=>x.ProjectId==id).ToListAsync());
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Missing_route_or_email_never_blocks_business_change(bool missingEmail) {
        await using var s=new ProjectSqlTests.Scope();await using var reviewer=new ProjectSqlTests.Scope();
        if(missingEmail){var id=await Route(s,reviewer);await s.Db.AppUsers.Where(x=>x.Id==id).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.Email,(string?)null));}
        var project=await Submit(s);var row=await Row(s,project,"ProjectSubmitted");
        Assert.Equal(NotificationStatus.Blocked,row.Status);Assert.NotNull(row.LastError);
        Assert.Equal(ProjectStatus.AwaitingManagerReview,(await s.Projects.DetailsAsync(project))!.Status);
    }
    [Theory][InlineData(ReviewDecision.Approved)][InlineData(ReviewDecision.Rejected)][InlineData(ReviewDecision.EditedAndApproved)]
    public async Task Review_decision_notifies_project_manager(ReviewDecision decision) {
        await using var s=new ProjectSqlTests.Scope();await using var reviewer=new ProjectSqlTests.Scope();
        await Route(s,reviewer);var project=await Submit(s);var product=(await s.Projects.DetailsAsync(project))!.Products[0];
        var form=(await Approvals(reviewer).FormAsync(project,product.Id,decision))!;
        form.Input.Comment="Decision comment";
        if(decision==ReviewDecision.Rejected)form.Input.RejectionReasonId=form.RejectionReasons.First().Id;
        await Approvals(reviewer).DecideAsync(form.Input);
        var row=await Row(s,project,$"Product{decision}");
        Assert.Equal((await s.Users.GetCurrentAsync()).Id,row.RecipientUserId);
        var snapshot = NotificationEmailRenderer.ReadData(row);
        Assert.Contains("Decision comment", snapshot.Comment);Assert.False(string.IsNullOrWhiteSpace(snapshot.Customer));Assert.False(string.IsNullOrWhiteSpace(snapshot.Actor));
        Assert.Equal($"Projects/Details/{project}",row.RelativePath);
    }
    [Fact]
    public async Task Implementation_requests_and_decisions_have_correct_recipients_and_links() {
        await using var s=new ProjectSqlTests.Scope();await using var reviewer=new ProjectSqlTests.Scope();
        var reviewerId=await Route(s,reviewer);var project=await Submit(s);
        var p=(await s.Projects.DetailsAsync(project))!.Products[0];
        var form=(await Approvals(reviewer).FormAsync(project,p.Id,ReviewDecision.Approved))!;await Approvals(reviewer).DecideAsync(form.Input);
        await CommercialWorkflowTests.Status(s,project,0,CommercialStatus.ImplementationIntoProduction);
        var request=await Row(s,project,"ImplementationRequested");Assert.Equal(reviewerId,request.RecipientUserId);
        var approval=await s.Db.ImplementationApprovals.AsNoTracking().SingleAsync(x=>x.ProjectProductId==p.Id);
        Assert.Equal($"Implementation/Review/{approval.Id}",request.RelativePath);
        var implementation=new ImplementationService(reviewer.Db,reviewer.Users,TimeProvider.System);
        await implementation.DecideAsync(new(){Id=approval.Id,Version=(await s.Projects.DetailsAsync(project))!.UpdatedAtUtc,Decision=ImplementationApprovalStatus.Rejected,Comment="Fix calculation"},default);
        Assert.Equal((await s.Users.GetCurrentAsync()).Id,(await Row(s,project,"ImplementationRejected")).RecipientUserId);
        await new ImplementationService(s.Db,s.Users,TimeProvider.System).ResubmitAsync(project,p.Id,(await s.Projects.DetailsAsync(project))!.UpdatedAtUtc,default);
        Assert.Equal(reviewerId,(await Row(s,project,"ImplementationResubmitted")).RecipientUserId);
    }
    [Fact]
    public async Task Noop_commercial_change_produces_no_duplicate() {
        await using var s=new ProjectSqlTests.Scope();await Email(s);var id=await Submit(s);
        await CommercialWorkflowTests.Decide(s,id,0,ReviewDecision.Approved);
        await CommercialWorkflowTests.Status(s,id,0,CommercialStatus.OfferUnderNegotiation);
        await CommercialWorkflowTests.Status(s,id,0,CommercialStatus.OfferUnderNegotiation);
        Assert.Single(await s.Db.NotificationOutbox.Where(x=>x.ProjectId==id && x.EventType=="CommercialStatus:OfferUnderNegotiation").ToListAsync());
    }
    private sealed class FailOutbox : SaveChangesInterceptor {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData e,InterceptionResult<int> result,CancellationToken ct=default) {
            if(e.Context!.ChangeTracker.Entries<NotificationOutbox>().Any(x=>x.State==EntityState.Added))throw new IOException("Injected outbox failure");
            return base.SavingChangesAsync(e,result,ct);
        }
    }
    [Fact]
    public async Task Outbox_and_business_commit_are_atomic() {
        await using var s=new ProjectSqlTests.Scope();var id=await s.Projects.SaveDraftAsync(WizardTests.ValidDraft());
        var before=(await s.Projects.DetailsAsync(id))!;
        await using var failing=s.NewContext(new FailOutbox());
        var service=new ApprovalService(failing,s.MakeUsers(failing),TimeProvider.System,DemoAccessTests.Access(enabled: false));
        await Assert.ThrowsAsync<IOException>(()=>service.SubmitAsync(id,before.UpdatedAtUtc));
        Assert.Equal(ProjectStatus.Draft,(await s.Projects.DetailsAsync(id))!.Status);
        Assert.Empty(await s.Db.NotificationOutbox.Where(x=>x.ProjectId==id).ToListAsync());
    }
    [Fact]
    public async Task Failure_retries_same_key_and_success_is_sent_once() {
        await using var s=new ProjectSqlTests.Scope();await using var reviewer=new ProjectSqlTests.Scope();await Route(s,reviewer);
        var id=await Submit(s);var row=await Row(s,id,"ProjectSubmitted");var sender=new Sender {Fail=true};
        await using var dispatchDb=s.NewContext();var worker=Dispatcher(dispatchDb,sender);
        Assert.False(await worker.ProcessAsync(row.Id,default));var retry=await Row(s,id,"ProjectSubmitted");
        Assert.Equal(NotificationStatus.Retry,retry.Status);Assert.Equal(1,retry.Attempts);Assert.DoesNotContain("Secret",retry.LastError);
        await s.Db.NotificationOutbox.Where(x=>x.Id==row.Id).ExecuteUpdateAsync(x=>x.SetProperty(n=>n.NextAttemptAtUtc,DateTimeOffset.UtcNow.AddSeconds(-1)));
        sender.Fail=false;Assert.True(await worker.ProcessAsync(row.Id,default));
        var sent=await Row(s,id,"ProjectSubmitted");Assert.Equal(NotificationStatus.Sent,sent.Status);Assert.NotNull(sent.SentAtUtc);Assert.Equal(2,sent.Attempts);
        Assert.False(await worker.ProcessAsync(row.Id,default));Assert.Equal(2,sender.Messages.Count);
        Assert.Equal(sender.Messages[0].IdempotencyKey,sender.Messages[1].IdempotencyKey);
        Assert.Contains($"https://portalmp.forte.local/Approvals/Review/{id}",sender.Messages[1].TextBody);
    }
    [Fact]
    public async Task Logging_sender_marks_simulated_not_sent() {
        await using var s=new ProjectSqlTests.Scope();await using var reviewer=new ProjectSqlTests.Scope();await Route(s,reviewer);
        var id=await Submit(s);var row=await Row(s,id,"ProjectSubmitted");await using var db=s.NewContext();
        Assert.True(await Dispatcher(db,new LoggingEmailSender(NullLogger<LoggingEmailSender>.Instance)).ProcessAsync(row.Id,default));
        var simulated=await Row(s,id,"ProjectSubmitted");Assert.Equal(NotificationStatus.Simulated,simulated.Status);Assert.Null(simulated.SentAtUtc);
    }
    [Fact]
    public async Task Missing_routing_can_be_fixed_without_repeating_business_action() {
        await using var s=new ProjectSqlTests.Scope();await using var reviewer=new ProjectSqlTests.Scope();
        var id=await Submit(s);var row=await Row(s,id,"ProjectSubmitted");await Route(s,reviewer);await using var db=s.NewContext();
        Assert.True(await Dispatcher(db,new Sender()).ProcessAsync(row.Id,default));Assert.Equal(NotificationStatus.Sent,(await Row(s,id,"ProjectSubmitted")).Status);
    }
    [Fact]
    public async Task Max_attempts_stops_retry() {
        await using var s=new ProjectSqlTests.Scope();await using var reviewer=new ProjectSqlTests.Scope();await Route(s,reviewer);
        var id=await Submit(s);var row=await Row(s,id,"ProjectSubmitted");await using var db=s.NewContext();
        Assert.False(await Dispatcher(db,new Sender{Fail=true},1).ProcessAsync(row.Id,default));
        Assert.Equal(NotificationStatus.Failed,(await Row(s,id,"ProjectSubmitted")).Status);
    }
    private sealed class WaitingSender : IEmailSender {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls;
        public async Task<EmailSendResult> SendAsync(EmailMessage message,CancellationToken ct) {
            Interlocked.Increment(ref Calls);Entered.TrySetResult();await Release.Task.WaitAsync(ct);return EmailSendResult.Delivered;
        }
    }
    [Fact]
    public async Task Two_workers_cannot_send_the_same_record_concurrently() {
        await using var s=new ProjectSqlTests.Scope();await using var reviewer=new ProjectSqlTests.Scope();await Route(s,reviewer);
        var id=await Submit(s);var row=await Row(s,id,"ProjectSubmitted");var sender=new WaitingSender();
        await using var db1=s.NewContext();await using var db2=s.NewContext();
        var first=Dispatcher(db1,sender).ProcessAsync(row.Id,default);
        try {
            await sender.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            // Even when the recovery deadline is reached, the live sender holds the SQL session lock.
            await s.Db.NotificationOutbox.Where(x=>x.Id==row.Id).ExecuteUpdateAsync(x=>x.SetProperty(n=>n.NextAttemptAtUtc,DateTimeOffset.UtcNow.AddMinutes(-1)));
            Assert.False(await Dispatcher(db2,sender).ProcessAsync(row.Id,default));Assert.Equal(1,sender.Calls);
        } finally { sender.Release.TrySetResult();await first; }
        Assert.Equal(NotificationStatus.Sent,(await Row(s,id,"ProjectSubmitted")).Status);
    }
    [Fact]
    public async Task Abandoned_processing_recovers_with_same_event_key() {
        await using var s=new ProjectSqlTests.Scope();await using var reviewer=new ProjectSqlTests.Scope();await Route(s,reviewer);
        var id=await Submit(s);var row=await Row(s,id,"ProjectSubmitted");
        await s.Db.NotificationOutbox.Where(x=>x.Id==row.Id).ExecuteUpdateAsync(x=>x.SetProperty(n=>n.Status,NotificationStatus.Processing)
            .SetProperty(n=>n.Attempts,1).SetProperty(n=>n.NextAttemptAtUtc,DateTimeOffset.UtcNow.AddMinutes(-3)));
        await using var db=s.NewContext();var sender=new Sender();Assert.True(await Dispatcher(db,sender).ProcessAsync(row.Id,default));
        Assert.Equal(row.EventKey,Assert.Single(sender.Messages).IdempotencyKey);Assert.Equal(2,(await Row(s,id,"ProjectSubmitted")).Attempts);
    }
    [Theory][InlineData(true)][InlineData(false)]
    public async Task Inactive_or_wrong_role_approver_is_blocked(bool inactive) {
        await using var s=new ProjectSqlTests.Scope();await using var reviewer=new ProjectSqlTests.Scope();var uid=await Route(s,reviewer);
        await s.Db.AppUsers.Where(x=>x.Id==uid).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.IsActive,!inactive)
            .SetProperty(u=>u.Role,inactive?AppRole.Manager:AppRole.ProjectManager));
        var project=await Submit(s);Assert.Equal(NotificationStatus.Blocked,(await Row(s,project,"ProjectSubmitted")).Status);
    }
    [Fact]
    public async Task Event_key_is_unique_and_enqueue_twice_is_idempotent() {
        await using var s=new ProjectSqlTests.Scope();await Email(s);var id=await s.Projects.SaveDraftAsync(WizardTests.ValidDraft());
        var actor=(await s.Users.GetCurrentAsync()).Id;var now=DateTimeOffset.UtcNow;
        await using(var tx=await s.Db.Database.BeginTransactionAsync()) {
            for(var i=0;i<2;i++) await WorkflowNotifications.EnqueueAsync(s.Db,id,null,"DedupTest",actor,now,NotificationAudience.ProjectManager,null,null,default);
            await tx.CommitAsync();
        }
        Assert.Single(await s.Db.NotificationOutbox.Where(x=>x.ProjectId==id).ToListAsync());
        Assert.Contains(s.Db.Model.FindEntityType(typeof(NotificationOutbox))!.GetIndexes(),x=>x.IsUnique && x.Properties.Single().Name=="EventKey");
    }
    [Fact]
    public async Task Terminal_product_events_include_reason_and_owner() {
        await using var s=new ProjectSqlTests.Scope();var uid=await Email(s);var project=await Submit(s);
        await CommercialWorkflowTests.Decide(s,project,0,ReviewDecision.Approved);
        await CommercialWorkflowTests.Decide(s,project,1,ReviewDecision.Approved);
        await CommercialWorkflowTests.Status(s,project,0,CommercialStatus.SalesAndDelivery);
        Assert.Equal(uid,(await Row(s,project,"CommercialStatus:SalesAndDelivery")).RecipientUserId);
        Assert.Equal(uid,(await Row(s,project,"ImplementationApproved")).RecipientUserId);
        var p=(await s.Projects.DetailsAsync(project))!;var closure=new CustomerClosureService(s.Db,s.Users,TimeProvider.System);
        var form=(await closure.FormAsync(project,p.Products[1].Id,default))!;form.Input.ReasonId=form.Reasons.First().Id;
        form.Input.Confirm=true;form.Input.Comment="Customer ended this SKU";await closure.CloseAsync(form.Input,default);
        var row=await Row(s,project,"ProductRejectedByCustomer");Assert.Equal(uid,row.RecipientUserId);
        Assert.Contains(form.Reasons.First().Name,NotificationEmailRenderer.ReadData(row).Comment);Assert.Contains(form.Input.Comment,NotificationEmailRenderer.ReadData(row).Comment);
    }
    [Fact]
    public async Task User_administration_saves_email_and_routing_with_audit_but_not_new_permissions() {
        await using var admin=new ProjectSqlTests.Scope();await using var owner=new ProjectSqlTests.Scope();await using var reviewer=new ProjectSqlTests.Scope();
        await Email(admin,AppRole.SuperAdmin);var uid=await Email(owner);var rid=await Email(reviewer,AppRole.Manager);
        var service=new UserAdministrationService(admin.Db,admin.Users,new PermissionService(admin.Users),TimeProvider.System);
        var form=(await service.GetAsync(uid,default))!;form.Input.Email="owner@example.invalid";form.Input.NotificationApproverId=rid;
        await service.SaveAsync(form.Input,default);
        var saved=await owner.Db.AppUsers.AsNoTracking().SingleAsync(x=>x.Id==uid);
        Assert.Equal("owner@example.invalid",saved.Email);Assert.Equal(rid,saved.NotificationApproverId);Assert.Equal(AppRole.ProjectManager,saved.Role);
        Assert.True(await admin.Db.AuditLogs.AnyAsync(x=>x.EntityType==nameof(AppUser) && x.EntityId==uid && x.FieldName==nameof(AppUser.NotificationApproverId)));
        form=(await service.GetAsync(uid,default))!;form.Input.NotificationApproverId=uid;
        await Assert.ThrowsAsync<ValidationException>(()=>service.SaveAsync(form.Input,default));
    }
    [Theory][InlineData(202)][InlineData(429)][InlineData(500)][InlineData(401)]
    public async Task Graph_transport_result_uses_existing_outbox_status_and_retry(int code) {
        await using var s=new ProjectSqlTests.Scope();await using var reviewer=new ProjectSqlTests.Scope();await Route(s,reviewer);
        var id=await Submit(s);var row=await Row(s,id,"ProjectSubmitted");
        var handler=new GraphEmailSenderTests.Handler(async(request,ct)=>{
            var payload=await request.Content!.ReadAsStringAsync(ct);
            using var json=System.Text.Json.JsonDocument.Parse(payload);
            var mail=json.RootElement.GetProperty("message");
            Assert.Equal(row.RecipientEmail,mail.GetProperty("toRecipients")[0].GetProperty("emailAddress").GetProperty("address").GetString());
            Assert.Contains($"https://portalmp.forte.local/Approvals/Review/{id}",mail.GetProperty("body").GetProperty("content").GetString());
            var response=new HttpResponseMessage((System.Net.HttpStatusCode)code);
            if(code==429)response.Headers.RetryAfter=new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(5));
            return response;
        });
        using var http=new HttpClient(handler);await using var db=s.NewContext();
        var before=DateTimeOffset.UtcNow;
        var result=await Dispatcher(db,GraphEmailSenderTests.Sender(http)).ProcessAsync(row.Id,default);
        var after=await Row(s,id,"ProjectSubmitted");Assert.Equal(1,handler.Calls);Assert.Equal(1,after.Attempts);
        Assert.Equal(code==202,result);Assert.Equal(code==202?NotificationStatus.Sent:NotificationStatus.Retry,after.Status);
        if(code==202)Assert.NotNull(after.SentAtUtc);else Assert.Contains(code.ToString(),after.LastError);
        if(code==429)Assert.True(after.NextAttemptAtUtc>=before.AddMinutes(5));
        Assert.Equal(ProjectStatus.AwaitingManagerReview,(await s.Projects.DetailsAsync(id))!.Status);
    }
}
