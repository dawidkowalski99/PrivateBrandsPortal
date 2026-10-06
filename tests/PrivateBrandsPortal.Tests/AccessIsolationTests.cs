using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PrivateBrandsPortal.Web.Interfaces;
using PrivateBrandsPortal.Web.Models.Enums;
using PrivateBrandsPortal.Web.Services;
using PrivateBrandsPortal.Web.ViewModels;
namespace PrivateBrandsPortal.Tests;

[Collection("SQL integration")]
public sealed class AccessIsolationTests
{
    [Theory]
    [InlineData(AppRole.ProjectManager)]
    [InlineData(AppRole.Manager)]
    [InlineData(AppRole.SuperAdmin)]
    public async Task Reports_and_csv_apply_role_scope_before_user_filters(AppRole role)
    {
        await using var s=new ProjectSqlTests.Scope();await using var other=new ProjectSqlTests.Scope();
        var id=await s.Projects.SaveDraftAsync(WizardTests.ValidDraft());var foreign=await other.Projects.SaveDraftAsync(WizardTests.ValidDraft());
        var user=await s.Users.GetCurrentAsync();var otherUser=await other.Users.GetCurrentAsync();
        s.Db.AppUserPermissions.AddRange(new(){AppUserId=user.Id,PermissionId=3},new(){AppUserId=user.Id,PermissionId=4});await s.Db.SaveChangesAsync();
        await s.Db.AppUsers.Where(x=>x.Id==user.Id).ExecuteUpdateAsync(x=>x.SetProperty(p=>p.Role,role));
        var reports=new ReportService(s.Db,new PermissionService(s.Users),s.Users);
        var ownNumber=await s.Db.Projects.Where(x=>x.Id==id).Select(x=>x.ProjectNumber).SingleAsync();
        var foreignNumber=await s.Db.Projects.Where(x=>x.Id==foreign).Select(x=>x.ProjectNumber).SingleAsync();
        var all=await reports.GetAsync(new());
        Assert.Contains(all.Rows,x=>x.ProjectNumber==ownNumber);
        Assert.Equal(role!=AppRole.ProjectManager,all.Rows.Any(x=>x.ProjectNumber==foreignNumber));
        if(role==AppRole.ProjectManager){Assert.Equal(1,all.Totals.Projects);Assert.All(all.Managers,x=>Assert.Equal(user.Id,x.Id));Assert.All(all.Users,x=>Assert.Equal(user.Id,x.Id));}
        var filter=new ReportFilter{ProjectManagerId=otherUser.Id};var manipulated=await reports.GetAsync(filter);
        Assert.Equal(role==AppRole.ProjectManager?0:1,manipulated.Totals.Projects);
        using var stream=new MemoryStream();await ReportCsv.WriteAsync(stream,await reports.ExportAsync(new()),default);
        var csv=Encoding.UTF8.GetString(stream.ToArray());Assert.Contains(ownNumber,csv);Assert.Equal(role!=AppRole.ProjectManager,csv.Contains(foreignNumber));
        using var filtered=new MemoryStream();await ReportCsv.WriteAsync(filtered,await reports.ExportAsync(filter),default);
        Assert.Equal(role!=AppRole.ProjectManager,Encoding.UTF8.GetString(filtered.ToArray()).Contains(foreignNumber));
        if(role==AppRole.ProjectManager)
        {
            await using var baseline=new AuthenticatedFactory();
            await using var host=baseline.WithWebHostBuilder(b=>b.ConfigureServices(x=>x.AddScoped<IAppUserService>(_=>s.Users)));
            using var client=host.CreateClient();client.DefaultRequestHeaders.Add("X-Test-User","reader");
            foreach(var path in new[]{"/Reports","/Reports/Export"})
            {
                var response=await client.GetAsync(path);Assert.Equal(HttpStatusCode.OK,response.StatusCode);
                var body=await response.Content.ReadAsStringAsync();Assert.Contains(ownNumber,body);Assert.DoesNotContain(foreignNumber,body);
                var manipulatedResponse=await client.GetAsync(path+"?ProjectManagerId="+otherUser.Id);
                Assert.Equal(HttpStatusCode.OK,manipulatedResponse.StatusCode);
                Assert.DoesNotContain(foreignNumber,await manipulatedResponse.Content.ReadAsStringAsync());
            }
        }
    }
    [Fact]
    public async Task Pm_search_details_edit_commercial_archive_dashboard_and_demo_review_are_owner_scoped()
    {
        await using var s=new ProjectSqlTests.Scope();await using var other=new ProjectSqlTests.Scope();
        var own=await s.Projects.SaveDraftAsync(WizardTests.ValidDraft());var foreign=await other.Projects.SaveDraftAsync(WizardTests.ValidDraft());
        var foreignNumber=(await other.Projects.DetailsAsync(foreign))!.ProjectNumber;
        Assert.Empty(await s.Projects.ListAsync(default,foreignNumber));Assert.Null(await s.Projects.DetailsAsync(foreign));Assert.Null(await s.Projects.LoadDraftAsync(foreign));
        var stolenDraft=(await other.Projects.LoadDraftAsync(foreign))!; await Assert.ThrowsAsync<ValidationException>(()=>s.Projects.SaveDraftAsync(stolenDraft));
        Assert.Single((await DashboardTests.Service(s).GetAsync()).RecentProjects);
        var review=CommercialWorkflowTests.Review(other);await review.SubmitAsync(foreign,(await other.Projects.DetailsAsync(foreign))!.UpdatedAtUtc);
        Assert.Null(await CommercialWorkflowTests.Review(s).ReviewAsync(foreign));Assert.DoesNotContain(await CommercialWorkflowTests.Review(s).QueueAsync(),x=>x.Id==foreign);
        await CommercialWorkflowTests.Decide(other,foreign,0,ReviewDecision.Approved);await CommercialWorkflowTests.Decide(other,foreign,1,ReviewDecision.Approved);
        var product=(await other.Projects.DetailsAsync(foreign))!.Products[0];
        Assert.Null(await CommercialWorkflowTests.Commercial(s).FormAsync(foreign,product.Id,default));
        Assert.Equal(0,(await DashboardTests.Service(s).GetAsync()).AwaitingPmUpdate);
        await CommercialWorkflowTests.Status(other,foreign,0,CommercialStatus.SalesAndDelivery);await CommercialWorkflowTests.Status(other,foreign,1,CommercialStatus.SalesAndDelivery);
        Assert.Empty(await CommercialWorkflowTests.Commercial(s).ArchiveAsync(default,foreignNumber));
        var user=await s.Users.GetCurrentAsync();await s.Db.AppUsers.Where(x=>x.Id==user.Id).ExecuteUpdateAsync(x=>x.SetProperty(p=>p.Role,AppRole.SuperAdmin));
        Assert.Contains(await CommercialWorkflowTests.Commercial(s).ArchiveAsync(default,foreignNumber),x=>x.Id==foreign);
        var otherDraft=await other.Projects.SaveDraftAsync(WizardTests.ValidDraft());var number=(await other.Projects.DetailsAsync(otherDraft))!.ProjectNumber;
        Assert.Equal(otherDraft,Assert.Single(await s.Projects.ListAsync(default,number)).Id);
    }
    [Fact]
    public async Task Historical_reassign_grant_does_not_allow_pm_transfer_or_expose_form()
    {
        await using var s=new ProjectSqlTests.Scope();await using var other=new ProjectSqlTests.Scope();var user=await s.Users.GetCurrentAsync();
        var target=await other.Users.GetCurrentAsync();var id=await s.Projects.SaveDraftAsync(WizardTests.ValidDraft());
        s.Db.AppUserPermissions.Add(new(){AppUserId=user.Id,PermissionId=5});await s.Db.SaveChangesAsync();
        var service=new ProjectTransferService(s.Db,s.Users,new PermissionService(s.Users),TimeProvider.System);
        await Assert.ThrowsAsync<PortalAccessException>(()=>service.FormAsync(id,default));
        var input=new ProjectTransferInput{ProjectId=id,NewProjectManagerId=target.Id,Version=(await s.Projects.DetailsAsync(id))!.UpdatedAtUtc,Reason="Forbidden"};
        await Assert.ThrowsAsync<PortalAccessException>(()=>service.TransferAsync(input));
        await using var baseline=new AuthenticatedFactory();await using var host=baseline.WithWebHostBuilder(b=>b.ConfigureServices(x=>x.AddScoped<IAppUserService>(_=>s.Users)));
        using var client=host.CreateClient();client.DefaultRequestHeaders.Add("X-Test-User","reader");
        Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync($"/Transfers/Project/{id}")).StatusCode);
    }
    [Fact]
    public async Task Retired_reassign_permission_is_not_assignable_in_users()
    {
        await using var admin=new ProjectSqlTests.Scope();await using var target=new ProjectSqlTests.Scope();var a=await admin.Users.GetCurrentAsync();var t=await target.Users.GetCurrentAsync();
        await admin.Db.AppUsers.Where(x=>x.Id==a.Id).ExecuteUpdateAsync(x=>x.SetProperty(p=>p.Role,AppRole.SuperAdmin));
        var service=new UserAdministrationService(admin.Db,admin.Users,new PermissionService(admin.Users),TimeProvider.System);
        var page=(await service.GetAsync(t.Id,default))!;Assert.DoesNotContain(page.Permissions,x=>x.Id==5);page.Input.PermissionIds=[5];
        await Assert.ThrowsAsync<ValidationException>(()=>service.SaveAsync(page.Input,default));
    }
}
