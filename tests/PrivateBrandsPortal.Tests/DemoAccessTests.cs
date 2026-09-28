using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PrivateBrandsPortal.Web.Configuration;
using PrivateBrandsPortal.Web.Interfaces;
using PrivateBrandsPortal.Web.Models.Entities;
using PrivateBrandsPortal.Web.Models.Enums;
using PrivateBrandsPortal.Web.Services;

namespace PrivateBrandsPortal.Tests;

[Collection("SQL integration")]
public sealed class DemoAccessTests
{
    private sealed class EnvironmentStub(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
    private sealed class Current(string login, bool authenticated) : ICurrentUserService
    {
        public string DomainLogin => login;
        public bool IsAuthenticated => authenticated;
        public string DisplayName => login;
        public string RoleLabel => "ProjectManager";
    }
    internal static DemoAccess Access(string environment = "Development", bool enabled = true,
        string configured = "TEST\\reader", string login = "TEST\\reader", bool authenticated = true) =>
        new(new EnvironmentStub(environment), Options.Create(new DemoAccessOptions { Enabled = enabled, UserDomainLogin = configured }), new Current(login, authenticated));

    [Theory]
    [InlineData("Development", true, "TEST\\reader", true, true)]
    [InlineData("Development", true, "test\\READER", true, true)]
    [InlineData("Development", true, "TEST\\other", true, false)]
    [InlineData("Development", false, "TEST\\reader", true, false)]
    [InlineData("Production", true, "TEST\\reader", true, false)]
    [InlineData("Staging", true, "TEST\\reader", true, false)]
    [InlineData("Demo", true, "TEST\\reader", true, false)]
    [InlineData("Development", true, "", true, false)]
    [InlineData("Development", true, "TEST\\reader", false, false)]
    public async Task Demo_permission_is_explicit_and_never_grants_admin(string environment, bool enabled, string configured, bool authenticated, bool expected)
    {
        var demo = Access(environment, enabled, configured, authenticated: authenticated);
        var requirement = new AppRoleRequirement(AppRole.Manager);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "TEST\\reader")], authenticated ? "Test" : null));
        var context = new AuthorizationHandlerContext([requirement], principal, null);
        await new ManagerAuthorization(new PolicyAppUser("ProjectManager"), demo).HandleAsync(context);
        Assert.Equal(expected, context.HasSucceeded);
        var admin = new AppRoleRequirement(AppRole.Admin);
        var adminContext = new AuthorizationHandlerContext([admin], principal, null);
        await new ManagerAuthorization(new PolicyAppUser("ProjectManager"), demo).HandleAsync(adminContext);
        Assert.False(adminContext.HasSucceeded);
        Assert.False(demo.AllowsManagerReview(new AppUser { DomainLogin = "TEST\\reader", DisplayName = "Inactive", IsActive = false }));
        Assert.False(demo.AllowsManagerReview(new AppUser { DomainLogin = "TEST\\another", DisplayName = "Mismatched profile" }));
    }

    [Theory]
    [InlineData("/Projects", "My projects")]
    [InlineData("/Approvals", "Awaiting review")]
    public async Task Same_project_manager_can_open_both_routes(string path, string expected)
    {
        await using var baseline = new AuthenticatedFactory();
        await using var factory = baseline.WithWebHostBuilder(b => b.UseEnvironment("Development").ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?> { ["DemoAccess:Enabled"] = "true", ["DemoAccess:UserDomainLogin"] = "TEST\\reader" })));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "reader");
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(expected, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(ReviewDecision.Approved, ReviewDecision.EditedAndApproved, ProjectStatus.Approved)]
    [InlineData(ReviewDecision.Rejected, ReviewDecision.Rejected, ProjectStatus.Rejected)]
    [InlineData(ReviewDecision.Approved, ReviewDecision.Rejected, ProjectStatus.PartiallyApproved)]
    public async Task Same_user_completes_review_without_role_change_or_further_routing(ReviewDecision first, ReviewDecision second, ProjectStatus expected)
    {
        await using var s = new ProjectSqlTests.Scope();
        var demo = Access(configured: s.Login, login: s.Login);
        var reviews = new ApprovalService(s.Db, s.Users, TimeProvider.System, demo);
        var id = await s.Projects.SaveDraftAsync(WizardTests.ValidDraft());
        var draft = (await s.Projects.DetailsAsync(id))!;
        await reviews.SubmitAsync(id, draft.UpdatedAtUtc);
        Assert.Contains(await reviews.QueueAsync(), x => x.Id == id);
        Assert.True(await reviews.CountAsync() > 0);
        for (var index = 0; index < 2; index++)
        {
            var current = (await reviews.ReviewAsync(id))!;
            var decision = index == 0 ? first : second;
            var input = (await reviews.FormAsync(id, current.Products[index].Id, decision))!.Input;
            input.Comment = decision == ReviewDecision.Rejected ? "Demo rejection" : null;
            if (input.Product is not null) input.Product.Quantity += 100;
            await reviews.DecideAsync(input);
        }
        var final = (await s.Projects.DetailsAsync(id))!;
        Assert.Equal(expected, final.Status);
        Assert.All(final.Products, p => Assert.Single(p.Reviews));
        Assert.DoesNotContain(await reviews.QueueAsync(), x => x.Id == id);
        Assert.Null(await reviews.FormAsync(id, final.Products[0].Id, ReviewDecision.Approved));
        Assert.Equal(AppRole.ProjectManager, (await s.Users.GetCurrentAsync()).Role);
        Assert.Contains(await s.Projects.ListAsync(), x => x.Id == id);
        Assert.Equal(7, s.Db.Model.GetEntityTypes().Count()); // No downstream task model.
        if (second == ReviewDecision.EditedAndApproved) Assert.Single(final.Products[1].Changes);
        // Backend uses the same gate as policies, even if a caller skips HTTP authorization.
        var disabled = new ApprovalService(s.Db, s.Users, TimeProvider.System, Access("Production", configured: s.Login, login: s.Login));
        await Assert.ThrowsAsync<PortalAccessException>(() => disabled.ReviewAsync(id));
        Assert.Equal(0, await disabled.CountAsync());
        Assert.Equal(expected, await s.Db.Projects.Where(x => x.Id == id).Select(x => x.Status).SingleAsync());
    }
}
