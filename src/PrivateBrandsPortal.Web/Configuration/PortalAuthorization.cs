using Microsoft.AspNetCore.Authorization;
using PrivateBrandsPortal.Web.Models.Enums;
namespace PrivateBrandsPortal.Web.Configuration;

public static class PortalAuthorization
{
    public const string ReviewProjects = "ReviewProjects";
    public const string ManagerReview = "ManagerReview";
    public const string AdministerPortal = "AdministerPortal";
    // Kept for backwards compatibility; business authorization never trusts incoming role claims.
    public const string RoleClaim = "privatebrands:role";
    public static void Configure(AuthorizationOptions options)
    {
        options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
        options.AddPolicy(ReviewProjects, p => p.RequireAuthenticatedUser().AddRequirements(new AppRoleRequirement(AppRole.Manager, AppRole.Admin)));
        options.AddPolicy(ManagerReview, p => p.RequireAuthenticatedUser().AddRequirements(new AppRoleRequirement(AppRole.Manager)));
        options.AddPolicy(AdministerPortal, p => p.RequireAuthenticatedUser().AddRequirements(new AppRoleRequirement(AppRole.Admin)));
    }
}
