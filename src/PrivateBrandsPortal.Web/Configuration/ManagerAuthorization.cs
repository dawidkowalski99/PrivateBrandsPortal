using Microsoft.AspNetCore.Authorization;
using PrivateBrandsPortal.Web.Interfaces;
using PrivateBrandsPortal.Web.Models.Enums;
using PrivateBrandsPortal.Web.Services;
namespace PrivateBrandsPortal.Web.Configuration;

public sealed class AppRoleRequirement(params AppRole[] roles) : IAuthorizationRequirement { public IReadOnlyList<AppRole> Roles { get; } = roles; }
public sealed class ManagerAuthorization(IAppUserService users) : AuthorizationHandler<AppRoleRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, AppRoleRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true) return;
        try
        {
            var user = await users.GetCurrentAsync();
            if (user.IsActive && requirement.Roles.Contains(user.Role)) context.Succeed(requirement);
        }
        catch (PortalAccessException) { }
    }
}
