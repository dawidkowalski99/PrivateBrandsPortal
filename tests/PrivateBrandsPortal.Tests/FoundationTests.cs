using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PrivateBrandsPortal.Web.Configuration;
using PrivateBrandsPortal.Web.Data;

namespace PrivateBrandsPortal.Tests;

public sealed class FoundationTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/Projects")]
    [InlineData("/Approvals")]
    [InlineData("/Administration")]
    public async Task Anonymous_requests_require_authentication(string path)
    {
        await using var factory = new AuthenticatedFactory();
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/", "Recent Projects")]
    [InlineData("/Projects", "My projects")]
    [InlineData("/Approvals", "Awaiting review")]
    [InlineData("/Administration", "Product Types")]
    public async Task Authenticated_shell_renders_without_database(string path, string expected)
    {
        await using var factory = new AuthenticatedFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "reader");
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains(expected, html);
        Assert.Contains("TEST\\reader", html);
        Assert.Contains("Role not assigned", html);
    }

    [Fact]
    public async Task Database_provider_is_sql_server_with_business_schema()
    {
        await using var factory = new AuthenticatedFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", db.Database.ProviderName);
        Assert.Equal(7, db.Model.GetEntityTypes().Count());
    }

    [Theory]
    [InlineData(null, false, false)]
    [InlineData("ProjectManager", false, false)]
    [InlineData("Manager", true, false)]
    [InlineData("Admin", true, true)]
    public async Task App_role_policies_enforce_permissions(string? role, bool review, bool admin)
    {
        await using var factory = new AuthenticatedFactory();
        using var scope = factory.Services.CreateScope();
        var authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        var claims = new List<Claim> { new(ClaimTypes.Name, "TEST\\reader"), new(ClaimTypes.Role, "Admin") };
        if (role is not null) claims.Add(new(PortalAuthorization.RoleClaim, role));
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        Assert.Equal(review, (await authorization.AuthorizeAsync(user, null, PortalAuthorization.ReviewProjects)).Succeeded);
        Assert.Equal(admin, (await authorization.AuthorizeAsync(user, null, PortalAuthorization.AdministerPortal)).Succeeded);
    }

    [Fact]
    public async Task Error_page_is_safe_and_not_cached()
    {
        await using var factory = new AuthenticatedFactory();
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/Home/Error");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Request ID:", html);
        Assert.DoesNotContain("StackTrace", html);
    }
}

// Test-only authentication: never registered or configurable in the application.
public sealed class AuthenticatedFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // TestServer has no real connection context required by Negotiate.
            // Windows SSO is verified separately against the actual Kestrel host.
            services.RemoveAll<IConfigureOptions<AuthenticationOptions>>();
            services.AddAuthentication("Test")
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", _ => { });
        });
    }
}

public sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey("X-Test-User"))
            return Task.FromResult(AuthenticateResult.NoResult());
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "TEST\\reader")], Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}
