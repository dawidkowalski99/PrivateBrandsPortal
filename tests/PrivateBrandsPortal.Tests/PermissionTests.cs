using System.ComponentModel.DataAnnotations;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PrivateBrandsPortal.Web.Interfaces;
using PrivateBrandsPortal.Web.Models.Entities;
using PrivateBrandsPortal.Web.Models.Enums;
using PrivateBrandsPortal.Web.Services;
using PrivateBrandsPortal.Web.ViewModels;

namespace PrivateBrandsPortal.Tests;

[Collection("SQL integration")]
public sealed class PermissionTests
{
    internal sealed class FixedUser(AppUser user) : IAppUserService
    {
        public Task<AppUser> GetCurrentAsync(CancellationToken ct = default) => Task.FromResult(user);
    }
    private static AppUser Profile(AppRole role = AppRole.ProjectManager, params string[] codes) => new() {
        Id=1, DomainLogin="TEST\\reader", DisplayName="Reader", Role=role,
        Permissions=codes.Select((code,i)=>new AppUserPermission { PermissionId=i+1,
            Permission=new Permission {Id=i+1,Code=code,Name=code} }).ToList()
    };
    [Theory]
    [InlineData(PermissionCodes.ManageUsers)]
    [InlineData(PermissionCodes.ManageDictionaries)]
    [InlineData(PermissionCodes.ViewReports)]
    [InlineData(PermissionCodes.ExportReports)]
    [InlineData(PermissionCodes.ReassignProjects)]
    public void SuperAdmin_has_all_permissions_but_inactive_profile_has_none(string code)
    {
        var user=Profile(AppRole.SuperAdmin);Assert.True(PermissionService.HasPermission(user,code));
        user.IsActive=false;Assert.False(PermissionService.HasPermission(user,code));
    }
    [Fact]
    public void Permissions_are_independent_and_inactive_grants_do_not_authorize()
    {
        var user=Profile(AppRole.ProjectManager,PermissionCodes.ViewReports);
        Assert.True(PermissionService.HasPermission(user,PermissionCodes.ViewReports));
        Assert.False(PermissionService.HasPermission(user,PermissionCodes.ExportReports));
        user.Permissions.Single().Permission.IsActive=false;
        Assert.False(PermissionService.HasPermission(user,PermissionCodes.ViewReports));
        user.Permissions.Single().Permission.IsActive=true;user.IsActive=false;
        Assert.False(PermissionService.HasPermission(user,PermissionCodes.ViewReports));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Manager_dictionary_access_requires_permission(bool grant)
    {
        await using var s=new ProjectSqlTests.Scope();
        var user=Profile(AppRole.Manager,grant?[PermissionCodes.ManageDictionaries]:[]);
        var service=new DictionaryService(s.Db,new PermissionService(new FixedUser(user)),TimeProvider.System);
        if(grant)Assert.NotEmpty((await service.ListAsync(DictionaryKind.Countries,default)).Items);
        else await Assert.ThrowsAsync<PortalAccessException>(()=>service.ListAsync(DictionaryKind.Countries,default));
    }
    [Theory]
    [InlineData(AppRole.ProjectManager,true)]
    [InlineData(AppRole.Manager,true)]
    [InlineData(AppRole.SuperAdmin,false)]
    public void Last_active_superadmin_cannot_be_removed(AppRole role,bool active)
    {
        var target=Profile(AppRole.SuperAdmin);var input=new UserEditInput{Role=role,IsActive=active};
        Assert.Throws<ValidationException>(()=>UserAdministrationService.ProtectLastSuperAdmin(target,input,false));
        UserAdministrationService.ProtectLastSuperAdmin(target,input,true);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Csv_endpoint_checks_export_permission_separately(bool export)
    {
        var user=Profile(AppRole.ProjectManager, export?[PermissionCodes.ViewReports,PermissionCodes.ExportReports]:[PermissionCodes.ViewReports]);
        await using var baseline=new AuthenticatedFactory();
        await using var host=baseline.WithWebHostBuilder(b=>b.ConfigureServices(s=>s.AddScoped<IAppUserService>(_=>new FixedUser(user))));
        using var client=host.CreateClient();client.DefaultRequestHeaders.Add("X-Test-User","reader");
        var report=await client.GetAsync("/Reports");Assert.Equal(HttpStatusCode.OK,report.StatusCode);
        Assert.Equal(export,(await report.Content.ReadAsStringAsync()).Contains("Export CSV"));
        Assert.Equal(export?HttpStatusCode.OK:HttpStatusCode.Forbidden,(await client.GetAsync("/Reports/Export")).StatusCode);
    }
    [Fact]
    public async Task Inactive_superadmin_and_unauthenticated_user_cannot_open_system()
    {
        var user=Profile(AppRole.SuperAdmin);user.IsActive=false;
        await using var baseline=new AuthenticatedFactory();
        await using var host=baseline.WithWebHostBuilder(b=>b.ConfigureServices(s=>s.AddScoped<IAppUserService>(_=>new FixedUser(user))));
        using var client=host.CreateClient();Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/Users")).StatusCode);
        client.DefaultRequestHeaders.Add("X-Test-User","reader");
        foreach(var path in new[]{"/","/Users","/Reports","/Reports/Export","/Dictionaries?kind=Countries","/Projects","/Approvals","/Archive"})
            Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync(path)).StatusCode);
    }
    [Fact]
    public async Task Manage_users_can_edit_regular_user_but_cannot_promote_or_edit_superadmin()
    {
        await using var actor=new ProjectSqlTests.Scope();await using var target=new ProjectSqlTests.Scope();
        var a=await actor.Users.GetCurrentAsync();var t=await target.Users.GetCurrentAsync();
        actor.Db.AppUserPermissions.Add(new(){AppUserId=a.Id,PermissionId=1});await actor.Db.SaveChangesAsync();actor.Db.ChangeTracker.Clear();
        var service=new UserAdministrationService(actor.Db,actor.Users,new PermissionService(actor.Users),TimeProvider.System);
        var input=(await service.GetAsync(t.Id,default))!.Input;input.Role=AppRole.Manager;input.PermissionIds=[2,3];
        await service.SaveAsync(input,default);actor.Db.ChangeTracker.Clear();
        var saved=await service.GetAsync(t.Id,default);Assert.Equal(AppRole.Manager,saved!.Input.Role);Assert.Equal(new[]{2,3},saved.Input.PermissionIds.Order());
        input=saved.Input;input.Role=AppRole.SuperAdmin;
        await Assert.ThrowsAsync<PortalAccessException>(()=>service.SaveAsync(input,default));
        await target.Db.AppUsers.Where(x=>x.Id==t.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Role,AppRole.SuperAdmin));actor.Db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<PortalAccessException>(()=>service.GetAsync(t.Id,default));
        input.Role=AppRole.Manager;await Assert.ThrowsAsync<PortalAccessException>(()=>service.SaveAsync(input,default));
    }
    [Fact]
    public async Task Superadmin_can_promote_user_and_stale_edit_is_rejected_with_audit()
    {
        await using var actor=new ProjectSqlTests.Scope();await using var target=new ProjectSqlTests.Scope();
        var a=await actor.Users.GetCurrentAsync();var t=await target.Users.GetCurrentAsync();
        await actor.Db.AppUsers.Where(x=>x.Id==a.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Role,AppRole.SuperAdmin));actor.Db.ChangeTracker.Clear();
        var service=new UserAdministrationService(actor.Db,actor.Users,new PermissionService(actor.Users),TimeProvider.System);
        var input=(await service.GetAsync(t.Id,default))!.Input;input.Role=AppRole.SuperAdmin;
        await service.SaveAsync(input,default);actor.Db.ChangeTracker.Clear();
        Assert.Equal(AppRole.SuperAdmin,(await service.GetAsync(t.Id,default))!.Input.Role);
        await Assert.ThrowsAsync<ValidationException>(()=>service.SaveAsync(input,default));
        Assert.True(await actor.Db.AuditLogs.AnyAsync(x=>x.EntityType==nameof(AppUser)&&x.EntityId==t.Id&&x.FieldName=="Role"&&x.NewValue=="SuperAdmin"));
    }
    [Fact]
    public async Task User_posts_require_csrf()
    {
        await using var baseline=new AuthenticatedFactory();
        await using var host=baseline.WithWebHostBuilder(b=>b.ConfigureServices(s=>s.AddScoped<IAppUserService>(_=>new FixedUser(Profile(AppRole.SuperAdmin)))));
        using var client=host.CreateClient();client.DefaultRequestHeaders.Add("X-Test-User","reader");
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("/Users")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsync("/Users/Edit",new FormUrlEncodedContent([]))).StatusCode);
    }
}
