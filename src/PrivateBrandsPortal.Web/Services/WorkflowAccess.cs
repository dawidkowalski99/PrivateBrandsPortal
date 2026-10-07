using PrivateBrandsPortal.Web.Models.Entities;
using PrivateBrandsPortal.Web.Models.Enums;

namespace PrivateBrandsPortal.Web.Services;

public static class WorkflowAccess
{
    // SuperAdmin can present both business workflows. Ownership, record state and
    // concurrency checks remain the responsibility of each operation.
    public static bool Allows(AppUser user, AppRole role) =>
        user.IsActive && (user.Role == role || user.Role == AppRole.SuperAdmin || (role == AppRole.ProjectManager && user.Role == AppRole.Manager));
}
