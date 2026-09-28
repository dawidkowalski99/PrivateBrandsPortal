using Microsoft.Extensions.Options;
using PrivateBrandsPortal.Web.Configuration;
using PrivateBrandsPortal.Web.Interfaces;
using PrivateBrandsPortal.Web.Models.Entities;
using PrivateBrandsPortal.Web.Models.Enums;

namespace PrivateBrandsPortal.Web.Services;

// An additional review permission, never a change to the stored role.
public sealed class DemoAccess(IHostEnvironment environment, IOptions<DemoAccessOptions> options,
    ICurrentUserService current)
{
    public bool AllowsManagerReview(AppUser user) =>
        environment.IsDevelopment() && options.Value.Enabled &&
        current.IsAuthenticated && user.IsActive && user.Role == AppRole.ProjectManager &&
        !string.IsNullOrWhiteSpace(options.Value.UserDomainLogin) &&
        string.Equals(current.DomainLogin, options.Value.UserDomainLogin, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(user.DomainLogin, current.DomainLogin, StringComparison.OrdinalIgnoreCase);
}
