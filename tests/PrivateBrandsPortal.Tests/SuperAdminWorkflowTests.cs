using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PrivateBrandsPortal.Web.Interfaces;
using PrivateBrandsPortal.Web.Models.Enums;
using PrivateBrandsPortal.Web.Services;
using PrivateBrandsPortal.Web.ViewModels;

namespace PrivateBrandsPortal.Tests;

[Collection("SQL integration")]
public sealed class SuperAdminWorkflowTests
{
    [Theory]
    [InlineData("/Projects", "My projects")]
    [InlineData("/Approvals", "Awaiting review")]
    [InlineData("/Archive", "Your completed projects")]
    public async Task Superadmin_can_open_workflow_pages_without_demo(string path, string expected)
    {
        await using var baseline = new AuthenticatedFactory();
        await using var host = baseline.WithWebHostBuilder(b => b.ConfigureServices(s => {
            s.AddScoped<IAppUserService>(_ => new PolicyAppUser("SuperAdmin"));
            s.AddScoped(_ => DemoAccessTests.Access(environment: "Production", enabled: false));
        }));
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "reader");
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(expected, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Superadmin_can_complete_own_workflow_without_demo_and_ownership_is_preserved()
    {
        await using var s = new ProjectSqlTests.Scope();
        await using var other = new ProjectSqlTests.Scope();
        var user = await s.Users.GetCurrentAsync();
        await s.Db.AppUsers.Where(x => x.Id == user.Id).ExecuteUpdateAsync(x => x.SetProperty(u => u.Role, AppRole.SuperAdmin));
        s.Db.ChangeTracker.Clear();
        var review = new ApprovalService(s.Db, s.Users, TimeProvider.System,
            DemoAccessTests.Access(environment: "Production", enabled: false));
        var commercial = new CommercialService(s.Db, s.Users, TimeProvider.System);
        var copy = new ProductCopyService(s.Db, s.Users);
        var initialAwaiting = await s.Projects.AwaitingPmAsync();
        var draft = WizardTests.ValidDraft();
        var id = await s.Projects.SaveDraftAsync(draft);
        var edit = (await s.Projects.LoadDraftAsync(id))!;
        edit.Products[0].Quantity = 12345;
        Assert.Equal(id, await s.Projects.SaveDraftAsync(edit));
        var details = (await s.Projects.DetailsAsync(id))!;
        Assert.Equal(12345, details.Products[0].Quantity);
        Assert.NotNull(await copy.CopyAsync(details.Products[0].Id, default));
        await review.SubmitAsync(id, details.UpdatedAtUtc);
        Assert.Contains(await review.QueueAsync(), x => x.Id == id);
        Assert.True(await review.CountAsync() > 0);
        var approval = (await review.FormAsync(id, details.Products[0].Id, ReviewDecision.Approved))!.Input;
        await review.DecideAsync(approval);
        var rejection = (await review.FormAsync(id, details.Products[1].Id, ReviewDecision.Rejected))!.Input;
        rejection.RejectionReasonId = 7;
        await review.DecideAsync(rejection);
        Assert.Equal(initialAwaiting + 1, await s.Projects.AwaitingPmAsync());
        var change = (await commercial.FormAsync(id, details.Products[0].Id, default))!.Input;
        change.Status = CommercialStatus.SalesAndDelivery;
        await commercial.UpdateAsync(change);
        Assert.Contains(await commercial.ArchiveAsync(default), x => x.Id == id);
        Assert.Equal(ProjectStatus.PartiallyApproved, (await s.Projects.DetailsAsync(id))!.Status);

        var foreignId = await other.Projects.SaveDraftAsync(WizardTests.ValidDraft());
        Assert.Null(await s.Projects.DetailsAsync(foreignId));
        Assert.Null(await s.Projects.LoadDraftAsync(foreignId));
        var foreignProduct = await other.Db.ProjectProducts.FirstAsync(x => x.ProjectId == foreignId);
        Assert.Null(await copy.CopyAsync(foreignProduct.Id, default));
        Assert.Null(await commercial.FormAsync(foreignId, foreignProduct.Id, default));
    }
}
