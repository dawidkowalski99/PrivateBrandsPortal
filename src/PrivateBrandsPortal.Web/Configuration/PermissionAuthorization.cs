using Microsoft.AspNetCore.Authorization;
using PrivateBrandsPortal.Web.Interfaces;

namespace PrivateBrandsPortal.Web.Configuration;

public sealed record PermissionRequirement(string Code) : IAuthorizationRequirement;
public sealed class PermissionAuthorization(IPermissionService permissions) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated == true && await permissions.HasAsync(requirement.Code))
            context.Succeed(requirement);
    }
}
