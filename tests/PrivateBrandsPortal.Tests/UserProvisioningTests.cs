using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PrivateBrandsPortal.Web.Interfaces;
using PrivateBrandsPortal.Web.Models.Entities;
using PrivateBrandsPortal.Web.Models.Enums;
using PrivateBrandsPortal.Web.Services;
using PrivateBrandsPortal.Web.ViewModels;
namespace PrivateBrandsPortal.Tests;

[Collection("SQL integration")]
public sealed class UserProvisioningTests
{
    private static UserAdministrationService Service(ProjectSqlTests.Scope s)=>new(s.Db,s.Users,new PermissionService(s.Users),TimeProvider.System);
    private static async Task Authorize(ProjectSqlTests.Scope s,bool super)
    {
        var user=await s.Users.GetCurrentAsync();
        if(super)await s.Db.AppUsers.Where(x=>x.Id==user.Id).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.Role,AppRole.SuperAdmin));
        else {var id=await s.Db.Permissions.Where(x=>x.Code==PermissionCodes.ManageUsers).Select(x=>x.Id).SingleAsync();s.Db.AppUserPermissions.Add(new(){AppUserId=user.Id,PermissionId=id});await s.Db.SaveChangesAsync();}
        s.Db.ChangeTracker.Clear();
    }
    [Theory][InlineData(true,AppRole.SuperAdmin)][InlineData(true,AppRole.Manager)][InlineData(true,AppRole.ProjectManager)]
    [InlineData(false,AppRole.ProjectManager)][InlineData(false,AppRole.Manager)]
    public async Task Authorized_creation_assigns_permissions_and_records_audit(bool super,AppRole role)
    {
        await using var actor=new ProjectSqlTests.Scope();await using var target=new ProjectSqlTests.Scope();await Authorize(actor,super);
        var permission=await actor.Db.Permissions.Where(x=>x.Code==PermissionCodes.ViewReports).Select(x=>x.Id).SingleAsync();
        var id=await Service(actor).CreateAsync(new(){DomainLogin="  "+target.Login+"  ",DisplayName="  Test user  ",Role=role,PermissionIds=[permission]},default);
        var user=await target.MakeUsers(target.Db,"Production").GetCurrentAsync();
        Assert.Equal(id,user.Id);Assert.Equal(role,user.Role);Assert.Equal("Test user",user.DisplayName);Assert.True(user.IsActive);
        Assert.Single(user.Permissions);Assert.True(PermissionService.HasPermission(user,PermissionCodes.ViewReports));
        Assert.True(await actor.Db.AuditLogs.AnyAsync(x=>x.EntityType==nameof(AppUser)&&x.EntityId==id&&x.FieldName=="UserCreated"&&x.ChangedByUserId==(actor.Db.AppUsers.Where(u=>u.DomainLogin==actor.Login).Select(u=>u.Id).First())));
        var project=await target.Projects.SaveDraftAsync(WizardTests.ValidDraft());
        Assert.Equal(role==AppRole.ProjectManager,(await target.Projects.DetailsAsync(project))!.RequiresManagerApproval);
        if(role==AppRole.ProjectManager)Assert.Null(await actor.Projects.DetailsAsync(project));
    }
    [Fact] public async Task Delegated_admin_cannot_create_superadmin_and_unprivileged_cannot_create_any_user()
    {
        await using var actor=new ProjectSqlTests.Scope();await using var target=new ProjectSqlTests.Scope();
        await Assert.ThrowsAsync<PortalAccessException>(()=>Service(actor).CreateAsync(new(){DomainLogin=target.Login,DisplayName="Test"},default));
        await Authorize(actor,false);
        await Assert.ThrowsAsync<PortalAccessException>(()=>Service(actor).CreateAsync(new(){DomainLogin=target.Login,DisplayName="Test",Role=AppRole.SuperAdmin},default));
        Assert.False(await actor.Db.AppUsers.AnyAsync(x=>x.DomainLogin==target.Login));
        Assert.False((await Service(actor).CreateFormAsync(default)).CanAssignSuperAdmin);
    }
    [Theory][InlineData(true)][InlineData(false)]
    public async Task Duplicate_login_is_case_insensitive_even_for_inactive_accounts(bool active)
    {
        await using var actor=new ProjectSqlTests.Scope();await using var target=new ProjectSqlTests.Scope();await Authorize(actor,true);
        await Service(actor).CreateAsync(new(){DomainLogin=target.Login,DisplayName="Test",IsActive=active},default);
        var ex=await Assert.ThrowsAsync<ValidationException>(()=>Service(actor).CreateAsync(new(){DomainLogin=target.Login.ToLowerInvariant(),DisplayName="Duplicate"},default));
        Assert.Contains("A user with this Windows account already exists.",ex.Message);
        Assert.Equal(1,await actor.Db.AppUsers.CountAsync(x=>x.DomainLogin==target.Login));
    }
    [Theory][InlineData("role")][InlineData("permission")][InlineData("retired")][InlineData("login")][InlineData("display")]
    public async Task Tampered_or_invalid_input_cannot_create_user(string kind)
    {
        await using var actor=new ProjectSqlTests.Scope();await using var target=new ProjectSqlTests.Scope();await Authorize(actor,true);
        var input=new UserCreateInput {DomainLogin=target.Login,DisplayName="Test"};
        if(kind=="role")input.Role=(AppRole)999;
        if(kind=="permission")input.PermissionIds=[int.MaxValue];
        if(kind=="retired")input.PermissionIds=[await actor.Db.Permissions.Where(x=>x.Code==PermissionCodes.ReassignProjects).Select(x=>x.Id).SingleAsync()];
        if(kind=="login")input.DomainLogin="username";
        if(kind=="display")input.DisplayName="  ";
        await Assert.ThrowsAsync<ValidationException>(()=>Service(actor).CreateAsync(input,default));
        Assert.False(await actor.Db.AppUsers.AnyAsync(x=>x.DomainLogin==target.Login));
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Production_missing_or_inactive_profile_is_denied_on_every_portal_route(bool inactive)
    {
        await using var scope=new ProjectSqlTests.Scope();
        if(inactive){var user=await scope.Users.GetCurrentAsync();await scope.Db.AppUsers.Where(x=>x.Id==user.Id).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.IsActive,false));}
        await using var baseline=new AuthenticatedFactory();
        await using var host=baseline.WithWebHostBuilder(b=>b.ConfigureServices(s=>s.AddScoped<IAppUserService>(_=>scope.MakeUsers(scope.Db,"Production"))));
        using var client=host.CreateClient();client.DefaultRequestHeaders.Add("X-Test-User","reader");
        foreach(var path in new[]{"/","/Projects","/Reports","/Attachments/Upload?projectId=5","/Administration","/Users/Create","/Approvals","/Archive"}) {
            var response=await client.GetAsync(path);Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
            var html=await response.Content.ReadAsStringAsync();Assert.Contains("Your Windows account is not authorized",html);Assert.Contains("Windows account:",html);
        }
        Assert.Equal(inactive?1:0,await scope.Db.AppUsers.CountAsync(x=>x.DomainLogin==scope.Login));
    }
    [Fact] public async Task Edit_updates_name_active_permissions_with_audit_and_login_is_unchanged()
    {
        await using var actor=new ProjectSqlTests.Scope();await using var target=new ProjectSqlTests.Scope();await Authorize(actor,true);
        var id=await Service(actor).CreateAsync(new(){DomainLogin=target.Login,DisplayName="Test"},default);
        var input=(await Service(actor).GetAsync(id,default))!.Input;input.DisplayName="Changed";input.IsActive=false;input.Role=AppRole.Manager;
        input.PermissionIds=[await actor.Db.Permissions.Where(x=>x.Code==PermissionCodes.ViewReports).Select(x=>x.Id).SingleAsync()];
        await Service(actor).SaveAsync(input,default);actor.Db.ChangeTracker.Clear();
        input=(await Service(actor).GetAsync(id,default))!.Input;input.IsActive=true;input.PermissionIds=[];await Service(actor).SaveAsync(input,default);
        var user=await target.Users.GetCurrentAsync();Assert.Equal(target.Login,user.DomainLogin);Assert.Equal("Changed",user.DisplayName);Assert.True(user.IsActive);Assert.Empty(user.Permissions);
        var fields=await actor.Db.AuditLogs.Where(x=>x.EntityType==nameof(AppUser)&&x.EntityId==id).Select(x=>x.FieldName).ToListAsync();
        foreach(var field in new[]{"DisplayName","Role","IsActive","Permissions"})Assert.Contains(field,fields);
    }
    [Fact] public async Task Create_http_requires_csrf_preserves_role_binding_errors_and_saves_valid_form()
    {
        await using var actor=new ProjectSqlTests.Scope();await using var target=new ProjectSqlTests.Scope();await Authorize(actor,true);
        await using var baseline=new AuthenticatedFactory();await using var host=baseline.WithWebHostBuilder(b=>b.ConfigureServices(s=>s.AddScoped<IAppUserService>(_=>actor.Users)));
        using var client=host.CreateClient(new(){AllowAutoRedirect=false});client.DefaultRequestHeaders.Add("X-Test-User","reader");
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsync("/Users/Create",new FormUrlEncodedContent([]))).StatusCode);
        var html=await client.GetStringAsync("/Users/Create");
        var token=WebUtility.HtmlDecode(Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]*)\"").Groups[1].Value);
        var data=new Dictionary<string,string>{{"__RequestVerificationToken",token},{"Input.DomainLogin","  "+target.Login+"  "},{"Input.DisplayName","Test"},{"Input.Role","invalid"},{"Input.IsActive","true"}};
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsync("/Users/Create",new FormUrlEncodedContent(data))).StatusCode);
        Assert.False(await actor.Db.AppUsers.AnyAsync(x=>x.DomainLogin==target.Login));
        data["Input.Role"]="ProjectManager";
        Assert.Equal(HttpStatusCode.Redirect,(await client.PostAsync("/Users/Create",new FormUrlEncodedContent(data))).StatusCode);
        Assert.True(await actor.Db.AppUsers.AnyAsync(x=>x.DomainLogin==target.Login));
    }
    [Fact] public async Task Bootstrap_refuses_existing_allow_list_without_elevating_users()
    {
        await using var scope=new ProjectSqlTests.Scope();var user=await scope.Users.GetCurrentAsync();
        await Assert.ThrowsAsync<ValidationException>(()=>new SuperAdminBootstrap(scope.Db,TimeProvider.System).RunAsync(scope.Login,"Test"));
        Assert.Equal(AppRole.ProjectManager,(await scope.Users.GetCurrentAsync()).Role);
    }
}
