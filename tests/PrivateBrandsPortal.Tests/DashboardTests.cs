using System.Net;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PrivateBrandsPortal.Web.Interfaces;
using PrivateBrandsPortal.Web.Models.Enums;
using PrivateBrandsPortal.Web.Services;
namespace PrivateBrandsPortal.Tests;

[Collection("SQL integration")]
public sealed class DashboardTests
{
    private sealed class CaptureError : IExceptionHandler
    {
        public Exception? Error { get; private set; }
        public ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
        { Error = exception; return ValueTask.FromResult(false); }
    }
    private sealed class Clock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
    internal static DashboardService Service(ProjectSqlTests.Scope s, TimeProvider? clock = null) => new(s.Db, s.Users, s.Projects, clock ?? TimeProvider.System);

    [Fact]
    public async Task Own_metrics_use_lifecycle_distinct_projects_and_seven_day_boundary()
    {
        await using var s = new ProjectSqlTests.Scope();
        await using var other = new ProjectSqlTests.Scope();
        var now = new DateTimeOffset(2030, 1, 20, 12, 0, 0, TimeSpan.Zero);
        var states = new[] { ProjectStatus.Draft, ProjectStatus.AwaitingManagerReview, ProjectStatus.PartiallyReviewed,
            ProjectStatus.Approved, ProjectStatus.Rejected, ProjectStatus.Approved, ProjectStatus.Draft, ProjectStatus.PartiallyApproved };
        var ids = new List<int>();
        for (var i = 0; i < states.Length; i++)
        {
            var id = await s.Projects.SaveDraftAsync(WizardTests.ValidDraft()); ids.Add(id);
            var updated = now.AddDays(i == 6 ? -8 : -i);
            await s.Db.Projects.Where(p => p.Id == id).ExecuteUpdateAsync(x => x.SetProperty(p => p.Status, states[i])
                .SetProperty(p => p.UpdatedAtUtc, updated).SetProperty(p => p.ArchivedAtUtc, i == 5 ? now : (DateTimeOffset?)null));
        }
        var foreign = await other.Projects.SaveDraftAsync(WizardTests.ValidDraft());
        await s.Db.Projects.Where(p => p.Id == foreign).ExecuteUpdateAsync(x => x.SetProperty(p => p.UpdatedAtUtc, now.AddHours(-1)));
        var view = await Service(s, new Clock(now)).GetAsync();
        Assert.False(view.IsGlobal); Assert.Equal(6, view.ActiveProjects); Assert.Equal(2, view.AwaitingApproval);
        Assert.Equal(7, view.RecentlyChanged); Assert.Equal(1, view.Completed);
        Assert.Equal(ids.Take(6), view.RecentProjects.Select(p => p.Id));
        Assert.All(view.RecentProjects, p => { Assert.True(p.IsOwned); Assert.Equal(2, p.SKUs); });
        Assert.DoesNotContain(view.RecentProjects, p => p.Id == foreign);
        var user = await s.Users.GetCurrentAsync();
        await s.Db.AppUsers.Where(x => x.Id == user.Id).ExecuteUpdateAsync(x => x.SetProperty(p => p.Role, AppRole.SuperAdmin));
        var global = await Service(s, new Clock(now)).GetAsync();
        Assert.True(global.IsGlobal);
        Assert.Equal(await s.Db.Projects.CountAsync(p => p.ArchivedAtUtc == null && p.Status != ProjectStatus.Rejected), global.ActiveProjects);
        Assert.Equal(await s.Db.Projects.CountAsync(p => p.Status == ProjectStatus.AwaitingManagerReview || p.Status == ProjectStatus.PartiallyReviewed), global.AwaitingApproval);
        Assert.Equal(await s.Db.Projects.CountAsync(p => p.ArchivedAtUtc != null), global.Completed);
        Assert.Equal(8, global.RecentlyChanged);
        Assert.Equal(6, global.RecentProjects.Count);
        Assert.Equal(ids[0], global.RecentProjects[0].Id); Assert.Equal(foreign, global.RecentProjects[1].Id);
    }

    [Fact]
    public async Task Empty_owner_has_zero_metrics_and_cannot_use_global_overview()
    {
        await using var s = new ProjectSqlTests.Scope(); var service = Service(s);
        var view = await service.GetAsync();
        Assert.Equal(0, view.ActiveProjects + view.AwaitingApproval + view.RecentlyChanged + view.Completed + view.AwaitingPmUpdate);
        Assert.Empty(view.RecentProjects);
        await Assert.ThrowsAsync<PortalAccessException>(() => service.OverviewAsync(1));
    }

    [Fact]
    public async Task Awaiting_pm_counts_skus_but_lists_distinct_scoped_projects()
    {
        await using var s = new ProjectSqlTests.Scope(); await using var other = new ProjectSqlTests.Scope();
        var own = await CommercialWorkflowTests.Submitted(s);
        await CommercialWorkflowTests.Decide(s, own, 0, ReviewDecision.Approved);
        await CommercialWorkflowTests.Decide(s, own, 1, ReviewDecision.EditedAndApproved);
        var foreign = await CommercialWorkflowTests.Submitted(other);
        await CommercialWorkflowTests.Decide(other, foreign, 0, ReviewDecision.Approved);
        Assert.Equal(2, (await Service(s).GetAsync()).AwaitingPmUpdate);
        Assert.Equal(own, Assert.Single((await Service(s).AwaitingAsync(1)).Projects).Id);
        var user = await s.Users.GetCurrentAsync();
        await s.Db.AppUsers.Where(x => x.Id == user.Id).ExecuteUpdateAsync(x => x.SetProperty(p => p.Role, AppRole.SuperAdmin));
        Assert.Equal(await s.Db.ProjectProducts.CountAsync(p => p.CommercialStatus == null &&
            (p.ReviewStatus == ProductReviewStatus.Approved || p.ReviewStatus == ProductReviewStatus.EditedAndApproved)),
            (await Service(s).GetAsync()).AwaitingPmUpdate);
        var list = (await Service(s).AwaitingAsync(1)).Projects;
        Assert.Contains(list, p => p.Id == foreign); Assert.Contains(list, p => p.Id == own);
        Assert.NotNull(await Service(s).OverviewAsync(foreign));
        Assert.Null(await s.Projects.DetailsAsync(foreign)); // Global overview does not broaden Projects ownership.
    }

    [Fact]
    public async Task Real_dashboard_html_has_metrics_and_no_foundation_placeholders()
    {
        await using var s = new ProjectSqlTests.Scope(); var id = await s.Projects.SaveDraftAsync(WizardTests.ValidDraft());
        await using var baseline = new AuthenticatedFactory();
        await using var host = baseline.WithWebHostBuilder(b => b.ConfigureServices(services => {
            services.AddScoped<IAppUserService>(_ => s.Users);
            services.AddScoped<IDashboardService>(_ => Service(s));
        }));
        using var client = host.CreateClient(); client.DefaultRequestHeaders.Add("X-Test-User", "reader");
        var response = await client.GetAsync("/"); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains((await s.Projects.DetailsAsync(id))!.ProjectNumber, html);
        Assert.Contains("Updated in the last 7 days", html);
        foreach (var text in new[] { "Your next project starts here", "Project creation will be added", "No project data connected yet" })
            Assert.DoesNotContain(text, html);
    }

    [Fact]
    public async Task Global_overview_renders_read_only_and_does_not_unlock_owned_details()
    {
        await using var s = new ProjectSqlTests.Scope(); await using var other = new ProjectSqlTests.Scope();
        var user = await s.Users.GetCurrentAsync();
        await s.Db.AppUsers.Where(x => x.Id == user.Id).ExecuteUpdateAsync(x => x.SetProperty(p => p.Role, AppRole.SuperAdmin));
        var id = await other.Projects.SaveDraftAsync(WizardTests.ValidDraft());
        var errors = new CaptureError();
        await using var baseline = new AuthenticatedFactory();
        await using var host = baseline.WithWebHostBuilder(b => b.ConfigureServices(services => {
            services.RemoveAll<IExceptionHandler>(); services.AddSingleton<IExceptionHandler>(errors);
            services.AddScoped<IAppUserService>(_ => s.Users);
            services.AddScoped<IProjectService>(_ => s.Projects);
            services.AddScoped<IDashboardService>(_ => Service(s));
        }));
        using var client = host.CreateClient(); client.DefaultRequestHeaders.Add("X-Test-User", "reader");
        var response = await client.GetAsync($"/Dashboard/Project/{id}"); Assert.True(response.StatusCode == HttpStatusCode.OK, errors.Error?.ToString());
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("read only", html); Assert.Contains("Transfer project", html);
        Assert.DoesNotContain("Edit Draft", html); Assert.DoesNotContain("Submit for Approval", html);
        var overview = await client.GetAsync($"/Projects/Details/{id}"); Assert.Equal(HttpStatusCode.OK, overview.StatusCode); Assert.Contains("read only", await overview.Content.ReadAsStringAsync());
    }
}
