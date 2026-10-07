using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using PrivateBrandsPortal.Web.Interfaces;
using PrivateBrandsPortal.Web.Models.Entities;
using PrivateBrandsPortal.Web.Models.Enums;
using PrivateBrandsPortal.Web.Services;
using PrivateBrandsPortal.Web.ViewModels;
namespace PrivateBrandsPortal.Tests;

[Collection("SQL integration")]
public sealed class ProjectTransferTests
{
    private static ProjectTransferService Service(ProjectSqlTests.Scope s) => new(s.Db, s.Users, new PermissionService(s.Users), TimeProvider.System);
    private static async Task<AppUser> Authorize(ProjectSqlTests.Scope s, bool admin = false)
    {
        var user = await s.Users.GetCurrentAsync();
        await s.Db.AppUsers.Where(x => x.Id == user.Id).ExecuteUpdateAsync(x => x.SetProperty(p => p.Role, AppRole.SuperAdmin));
        s.Db.ChangeTracker.Clear(); return await s.Users.GetCurrentAsync();
    }
    private static async Task<ProjectTransferInput> Input(ProjectSqlTests.Scope s, int id, int target) => new() {
        ProjectId = id, Version = await s.Db.Projects.Where(x => x.Id == id).Select(x => x.UpdatedAtUtc).SingleAsync(),
        NewProjectManagerId = target, Reason = "  PM responsibility changed  "
    };
    [Theory]
    [InlineData(false, AppRole.ProjectManager)]
    [InlineData(true, AppRole.ProjectManager)]
    [InlineData(false, AppRole.SuperAdmin)]
    public async Task Authorized_transfer_preserves_project_and_audits_actor_reason_and_both_managers(bool admin, AppRole role)
    {
        await using var s = new ProjectSqlTests.Scope(); await using var next = new ProjectSqlTests.Scope();
        var actor = await Authorize(s, admin); var target = await next.Users.GetCurrentAsync();
        await s.Db.AppUsers.Where(x => x.Id == target.Id).ExecuteUpdateAsync(x => x.SetProperty(p => p.Role, role));
        var id = await s.Projects.SaveDraftAsync(WizardTests.ValidDraft());
        var before = (await s.Projects.DetailsAsync(id))!;
        if(!admin) await s.Db.AppUsers.Where(x=>x.Id==actor.Id).ExecuteUpdateAsync(x=>x.SetProperty(p=>p.Role,AppRole.Manager));
        var form = (await Service(s).FormAsync(id, default))!;
        Assert.Contains(form.ProjectManagers, x => x.Id == target.Id); Assert.DoesNotContain(form.ProjectManagers, x => x.Id == actor.Id);
        await Service(s).TransferAsync(await Input(s, id, target.Id));
        var after = (await next.Projects.DetailsAsync(id))!;
        Assert.Equal(before.ProjectNumber, after.ProjectNumber); Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.CreatedAtUtc, after.CreatedAtUtc); Assert.Equal(before.SubmittedAtUtc, after.SubmittedAtUtc);
        Assert.True(after.UpdatedAtUtc > before.UpdatedAtUtc);
        Assert.Equal(JsonSerializer.Serialize(before.Products), JsonSerializer.Serialize(after.Products));
        var audit = await s.Db.AuditLogs.SingleAsync(x => x.EntityType == "Project" && x.EntityId == id && x.ChangeType == AuditChangeType.ProjectReassigned);
        Assert.Equal("ProjectManager", audit.FieldName); Assert.Contains(s.Login, audit.OldValue); Assert.Contains(next.Login, audit.NewValue);
        Assert.Contains($"#{actor.Id}", audit.OldValue); Assert.Contains($"#{target.Id}", audit.NewValue);
        Assert.Equal(actor.Id, audit.ChangedByUserId); Assert.Equal("PM responsibility changed", audit.Reason);
        Assert.Equal(TimeSpan.Zero, audit.ChangedAtUtc.Offset); Assert.Equal(after.UpdatedAtUtc, audit.ChangedAtUtc);
        Assert.Equal(audit.Reason, Assert.Single(after.Transfers).Reason);
    }
    [Fact]
    public async Task Transfer_revokes_old_wizard_and_moves_reports_and_dashboard_to_new_owner()
    {
        await using var s = new ProjectSqlTests.Scope(); await using var next = new ProjectSqlTests.Scope();
        var actor = await Authorize(s); var target = await next.Users.GetCurrentAsync();
        var id = await s.Projects.SaveDraftAsync(WizardTests.ValidDraft()); var stale = (await s.Projects.LoadDraftAsync(id))!;
        var reports = new ReportService(s.Db, new PermissionService(new PolicyAppUser("SuperAdmin")), new PolicyAppUser("SuperAdmin"));
        await s.Db.AppUsers.Where(x=>x.Id==actor.Id).ExecuteUpdateAsync(x=>x.SetProperty(p=>p.Role,AppRole.ProjectManager));
        Assert.Equal(1, (await DashboardTests.Service(s).GetAsync()).ActiveProjects);
        Assert.Equal(0, (await DashboardTests.Service(next).GetAsync()).ActiveProjects);
        var beforeGlobal = await s.Db.Projects.CountAsync(p => p.ArchivedAtUtc == null && p.Status != ProjectStatus.Rejected);
        await s.Db.AppUsers.Where(x=>x.Id==actor.Id).ExecuteUpdateAsync(x=>x.SetProperty(p=>p.Role,AppRole.Manager));
        await Service(s).TransferAsync(await Input(s, id, target.Id));
        await s.Db.AppUsers.Where(x=>x.Id==actor.Id).ExecuteUpdateAsync(x=>x.SetProperty(p=>p.Role,AppRole.ProjectManager));
        Assert.Null(await s.Projects.DetailsAsync(id)); Assert.Null(await s.Projects.LoadDraftAsync(id)); Assert.Empty(await s.Projects.ListAsync());
        await Assert.ThrowsAsync<ValidationException>(() => s.Projects.SaveDraftAsync(stale));
        Assert.Equal(id, Assert.Single(await next.Projects.ListAsync()).Id);
        var edit = (await next.Projects.LoadDraftAsync(id))!; edit.Brief.Customer = "New owner's update";
        await next.Projects.SaveDraftAsync(edit);
        Assert.Equal(0, (await DashboardTests.Service(s).GetAsync()).ActiveProjects);
        Assert.Equal(1, (await DashboardTests.Service(next).GetAsync()).ActiveProjects);
        Assert.Equal(beforeGlobal, await s.Db.Projects.CountAsync(p => p.ArchivedAtUtc == null && p.Status != ProjectStatus.Rejected));
        Assert.Equal(0, (await reports.GetAsync(new() { ProjectManagerId = actor.Id })).Totals.Projects);
        var report = await reports.GetAsync(new() { ProjectManagerId = target.Id });
        Assert.Equal(1, report.Totals.Projects); Assert.Equal(2, report.Totals.SKUs); Assert.Equal(target.Id, Assert.Single(report.Managers).Id);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Transfer_preserves_review_commercial_and_archive_and_new_owner_can_continue(bool archived)
    {
        await using var s = new ProjectSqlTests.Scope(); await using var next = new ProjectSqlTests.Scope();
        var target = await next.Users.GetCurrentAsync();
        var id = await CommercialWorkflowTests.Submitted(s);
        await Authorize(s);
        await CommercialWorkflowTests.Decide(s, id, 0, ReviewDecision.Approved);
        await CommercialWorkflowTests.Decide(s, id, 1, ReviewDecision.Rejected, 7);
        await CommercialWorkflowTests.Status(s, id, 0, archived ? CommercialStatus.SalesAndDelivery : CommercialStatus.PriceOfferSubmitted);
        var before = (await s.Projects.DetailsAsync(id))!;
        await Service(s).TransferAsync(await Input(s, id, target.Id));
        var after = (await next.Projects.DetailsAsync(id))!;
        Assert.Equal(before.Status, after.Status); Assert.Equal(before.ArchivedAtUtc, after.ArchivedAtUtc);
        Assert.Equal(JsonSerializer.Serialize(before.Products), JsonSerializer.Serialize(after.Products));
        Assert.Null(await s.Projects.DetailsAsync(id));
        if (archived) {
            Assert.Contains(await CommercialWorkflowTests.Commercial(s).ArchiveAsync(default), x => x.Id == id); // SuperAdmin's archive is now global.
            Assert.Contains(await CommercialWorkflowTests.Commercial(next).ArchiveAsync(default), x => x.Id == id);
        } else {
            await CommercialWorkflowTests.Status(next, id, 0, CommercialStatus.OfferUnderNegotiation);
            Assert.Equal(CommercialStatus.OfferUnderNegotiation, (await next.Projects.DetailsAsync(id))!.Products[0].CommercialStatus);
        }
    }
    [Theory]
    [InlineData("ordinary")]
    [InlineData("inactive actor")]
    [InlineData("revoked grant")]
    public async Task Unauthorized_or_revoked_actor_cannot_transfer(string kind)
    {
        await using var s = new ProjectSqlTests.Scope(); await using var next = new ProjectSqlTests.Scope();
        var actor = kind == "ordinary" ? await s.Users.GetCurrentAsync() : await Authorize(s);
        var target = await next.Users.GetCurrentAsync(); var id = await s.Projects.SaveDraftAsync(WizardTests.ValidDraft());
        // A stale request-scoped profile must not retain a grant revoked in SQL.
        var cached = new PermissionTests.FixedUser(actor);
        var service = new ProjectTransferService(s.Db, cached, new PermissionService(cached), TimeProvider.System);
        if (kind == "inactive actor") await s.Db.AppUsers.Where(x => x.Id == actor.Id).ExecuteUpdateAsync(x => x.SetProperty(p => p.IsActive, false));
        if (kind == "revoked grant") await s.Db.AppUsers.Where(x => x.Id == actor.Id).ExecuteUpdateAsync(x=>x.SetProperty(p=>p.Role,AppRole.ProjectManager));
        var input = await Input(s, id, target.Id);
        await Assert.ThrowsAsync<PortalAccessException>(() => service.TransferAsync(input));
        Assert.Equal(actor.Id, await s.Db.Projects.Where(x => x.Id == id).Select(x => x.ProjectManagerId).SingleAsync());
    }
    [Theory]
    [InlineData("inactive")]
    [InlineData("inactive manager")]
    [InlineData("missing")]
    [InlineData("same")]
    [InlineData("reason")]
    [InlineData("long reason")]
    [InlineData("missing project")]
    public async Task Invalid_destination_or_reason_is_rejected_without_changes(string kind)
    {
        await using var s = new ProjectSqlTests.Scope(); await using var next = new ProjectSqlTests.Scope();
        var actor = await Authorize(s); var target = await next.Users.GetCurrentAsync();
        var id = await s.Projects.SaveDraftAsync(WizardTests.ValidDraft()); var input = await Input(s, id, target.Id);
        if (kind == "inactive") await s.Db.AppUsers.Where(x => x.Id == target.Id).ExecuteUpdateAsync(x => x.SetProperty(p => p.IsActive, false));
        if (kind == "inactive manager") await s.Db.AppUsers.Where(x => x.Id == target.Id).ExecuteUpdateAsync(x => x.SetProperty(p => p.Role, AppRole.Manager).SetProperty(p=>p.IsActive,false));
        if (kind == "missing") input.NewProjectManagerId = int.MaxValue;
        if (kind == "same") input.NewProjectManagerId = actor.Id;
        if (kind == "reason") input.Reason = "  ";
        if (kind == "long reason") input.Reason = new string('a', 1001);
        if (kind == "missing project") input.ProjectId = int.MaxValue;
        await Assert.ThrowsAsync<ValidationException>(() => Service(s).TransferAsync(input));
        Assert.Equal(actor.Id, await s.Db.Projects.Where(x => x.Id == id).Select(x => x.ProjectManagerId).SingleAsync());
        Assert.False(await s.Db.AuditLogs.AnyAsync(x => x.EntityId == id && x.ChangeType == AuditChangeType.ProjectReassigned));
        if (kind is "inactive" or "inactive manager") Assert.DoesNotContain((await Service(s).FormAsync(id, default))!.ProjectManagers, x => x.Id == target.Id);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stale_project_version_has_readable_conflict_and_no_extra_audit(bool transferred)
    {
        await using var s = new ProjectSqlTests.Scope(); await using var next = new ProjectSqlTests.Scope();
        var actor = await Authorize(s); var target = await next.Users.GetCurrentAsync();
        var id = await s.Projects.SaveDraftAsync(WizardTests.ValidDraft()); var input = await Input(s, id, target.Id);
        if (transferred) await Service(s).TransferAsync(await Input(s, id, target.Id));
        else { var edit = (await s.Projects.LoadDraftAsync(id))!; edit.Brief.Customer = "Concurrent edit"; await s.Projects.SaveDraftAsync(edit); }
        var error = await Assert.ThrowsAsync<ValidationException>(() => Service(s).TransferAsync(input));
        Assert.Equal("The project has changed. Reload it and try again.", error.Message);
        Assert.Equal(transferred ? target.Id : actor.Id, await s.Db.Projects.Where(x => x.Id == id).Select(x => x.ProjectManagerId).SingleAsync());
        Assert.Equal(transferred ? 1 : 0, await s.Db.AuditLogs.CountAsync(x => x.EntityId == id && x.ChangeType == AuditChangeType.ProjectReassigned));
    }
    private sealed class AuditFailure : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            if (command.CommandText.Contains("INSERT INTO [AuditLogs]")) throw new InvalidOperationException("Deliberate transfer audit failure.");
            return base.ReaderExecutingAsync(command, data, result, ct);
        }
    }
    [Fact]
    public async Task Concurrent_transfers_have_one_winner_and_one_audit()
    {
        await using var s = new ProjectSqlTests.Scope(); await using var first = new ProjectSqlTests.Scope();
        await using var second = new ProjectSqlTests.Scope();
        await Authorize(s); var firstUser = await first.Users.GetCurrentAsync(); var secondUser = await second.Users.GetCurrentAsync();
        var id = await s.Projects.SaveDraftAsync(WizardTests.ValidDraft());
        var input1 = await Input(s, id, firstUser.Id); var input2 = await Input(s, id, secondUser.Id);
        async Task<Exception?> Attempt(ProjectTransferInput input)
        {
            await using var db = s.NewContext(); var users = s.MakeUsers(db);
            return await Record.ExceptionAsync(() => new ProjectTransferService(db, users, new PermissionService(users), TimeProvider.System).TransferAsync(input));
        }
        var results = await Task.WhenAll(Attempt(input1), Attempt(input2));
        Assert.Single(results, e => e is null);
        var conflict = Assert.IsType<ValidationException>(Assert.Single(results, e => e is not null));
        Assert.Equal("The project has changed. Reload it and try again.", conflict.Message);
        Assert.Equal(1, await s.Db.AuditLogs.CountAsync(x => x.EntityId == id && x.ChangeType == AuditChangeType.ProjectReassigned));
        Assert.Contains(await s.Db.Projects.Where(x => x.Id == id).Select(x => x.ProjectManagerId).SingleAsync(), new[] { firstUser.Id, secondUser.Id });
    }
    [Fact]
    public async Task Audit_failure_rolls_back_owner_and_timestamp()
    {
        await using var s = new ProjectSqlTests.Scope(); await using var next = new ProjectSqlTests.Scope();
        var actor = await Authorize(s); var target = await next.Users.GetCurrentAsync();
        var id = await s.Projects.SaveDraftAsync(WizardTests.ValidDraft()); var input = await Input(s, id, target.Id);
        await using var failing = s.NewContext(new AuditFailure()); var users = s.MakeUsers(failing);
        var service = new ProjectTransferService(failing, users, new PermissionService(users), TimeProvider.System);
        var error = await Assert.ThrowsAnyAsync<Exception>(() => service.TransferAsync(input));
        Assert.Contains("Deliberate transfer audit failure.", error.ToString());
        var project = await s.Db.Projects.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.Equal(actor.Id, project.ProjectManagerId); Assert.Equal(input.Version, project.UpdatedAtUtc);
        Assert.False(await s.Db.AuditLogs.AnyAsync(x => x.EntityId == id && x.ChangeType == AuditChangeType.ProjectReassigned));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Transfer_endpoint_requires_permission_and_antiforgery(bool admin)
    {
        await using var baseline = new AuthenticatedFactory();
        await using var host = baseline.WithWebHostBuilder(b => b.ConfigureServices(s =>
            s.AddScoped<IAppUserService>(_ => new PolicyAppUser(admin ? "SuperAdmin" : "ProjectManager"))));
        using var client = host.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/Transfers/Project/1")).StatusCode);
        client.DefaultRequestHeaders.Add("X-Test-User", "reader");
        Assert.Equal(admin ? HttpStatusCode.BadRequest : HttpStatusCode.Forbidden,
            (await client.PostAsync("/Transfers/Project", new FormUrlEncodedContent([]))).StatusCode);
    }
    [Fact]
    public async Task Transfer_form_posts_bound_input_with_antiforgery_and_redirects_after_commit()
    {
        await using var s = new ProjectSqlTests.Scope(); await using var next = new ProjectSqlTests.Scope();
        await Authorize(s); var target = await next.Users.GetCurrentAsync();
        var id = await s.Projects.SaveDraftAsync(WizardTests.ValidDraft());
        await using var baseline = new AuthenticatedFactory();
        await using var host = baseline.WithWebHostBuilder(b => b.ConfigureServices(services => services.AddScoped<IAppUserService>(_ => s.Users)));
        using var client = host.CreateClient(new() { AllowAutoRedirect = false }); client.DefaultRequestHeaders.Add("X-Test-User", "reader");
        var response = await client.GetAsync($"/Transfers/Project/{id}"); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        string Hidden(string name) => WebUtility.HtmlDecode(Regex.Match(html, $"name=\"{Regex.Escape(name)}\"[^>]*value=\"([^\"]*)\"").Groups[1].Value);
        Assert.NotEmpty(Hidden("__RequestVerificationToken")); Assert.NotEmpty(Hidden("Input.Version"));
        var result = await client.PostAsync($"/Transfers/Project/{id}", new FormUrlEncodedContent(new Dictionary<string, string> {
            ["__RequestVerificationToken"] = Hidden("__RequestVerificationToken"), ["Input.ProjectId"] = id.ToString(),
            ["Input.Version"] = Hidden("Input.Version"), ["Input.NewProjectManagerId"] = target.Id.ToString(), ["Input.Reason"] = "HTTP transfer test"
        }));
        Assert.Equal(HttpStatusCode.Redirect, result.StatusCode);
        Assert.Equal(target.Id, await s.Db.Projects.Where(x => x.Id == id).Select(x => x.ProjectManagerId).SingleAsync());
        Assert.Equal("HTTP transfer test", Assert.Single((await next.Projects.DetailsAsync(id))!.Transfers).Reason);
    }
}
