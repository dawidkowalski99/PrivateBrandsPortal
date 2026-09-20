using PrivateBrandsPortal.Web.Configuration;
using PrivateBrandsPortal.Web.Interfaces;
namespace PrivateBrandsPortal.Web.Services;

public sealed class CurrentUserService(IHttpContextAccessor accessor) : ICurrentUserService
{
    public bool IsAuthenticated => accessor.HttpContext?.User.Identity?.IsAuthenticated == true;
    public string? DomainLogin => IsAuthenticated ? accessor.HttpContext?.User.Identity?.Name : null;
    public string DisplayName => accessor.HttpContext?.Items["PortalAppUser"] is Models.Entities.AppUser user ? ProjectDetailsReader.Name(user) : DomainLogin ?? "Unknown user";
    public string RoleLabel => (accessor.HttpContext?.Items["PortalAppUser"] as Models.Entities.AppUser)?.Role.ToString()
        ?? "Role not assigned";
}
