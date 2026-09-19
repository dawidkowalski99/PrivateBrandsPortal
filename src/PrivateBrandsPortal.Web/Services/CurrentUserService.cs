using PrivateBrandsPortal.Web.Configuration;
using PrivateBrandsPortal.Web.Interfaces;
namespace PrivateBrandsPortal.Web.Services;

public sealed class CurrentUserService(IHttpContextAccessor accessor) : ICurrentUserService
{
    public bool IsAuthenticated => accessor.HttpContext?.User.Identity?.IsAuthenticated == true;
    public string? DomainLogin => IsAuthenticated ? accessor.HttpContext?.User.Identity?.Name : null;
    public string DisplayName => DomainLogin ?? "Unknown user";
    public string RoleLabel => (accessor.HttpContext?.Items["PortalAppUser"] as Models.Entities.AppUser)?.Role.ToString()
        ?? accessor.HttpContext?.User.FindFirst(PortalAuthorization.RoleClaim)?.Value ?? "Role not assigned";
}
