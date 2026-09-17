using Microsoft.AspNetCore.Authorization;
namespace PrivateBrandsPortal.Web.Configuration;

public static class PortalAuthorization
{
    public const string ReviewProjects = "ReviewProjects";
    public const string AdministerPortal = "AdministerPortal";
    // Application roles are separate from Windows group claims.
    public const string RoleClaim = "privatebrands:role";
    public static void Configure(AuthorizationOptions options)
    {
        options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
        options.AddPolicy(ReviewProjects, p => p.RequireAuthenticatedUser().RequireClaim(RoleClaim, "Manager", "Admin"));
        options.AddPolicy(AdministerPortal, p => p.RequireAuthenticatedUser().RequireClaim(RoleClaim, "Admin"));
    }
}
