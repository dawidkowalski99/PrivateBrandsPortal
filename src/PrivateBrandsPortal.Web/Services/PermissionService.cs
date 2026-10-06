using PrivateBrandsPortal.Web.Interfaces;
using PrivateBrandsPortal.Web.Models.Entities;
using PrivateBrandsPortal.Web.Models.Enums;

namespace PrivateBrandsPortal.Web.Services;

public static class PermissionCodes
{
    public const string ReassignProjects = "REASSIGN_PROJECTS";
    public const string ManageUsers = "MANAGE_USERS";
    public const string ManageDictionaries = "MANAGE_DICTIONARIES";
    public const string ViewReports = "VIEW_REPORTS";
    public const string ExportReports = "EXPORT_REPORTS";
}

public sealed class PermissionService(IAppUserService users) : IPermissionService
{
    // AppUserService loads the profile and its grants once per HTTP request.
    public static bool HasPermission(AppUser user, string code) => user.IsActive &&
        (code == PermissionCodes.ReassignProjects
            ? user.Role is AppRole.Manager or AppRole.SuperAdmin
            : user.Role == AppRole.SuperAdmin || user.Permissions.Any(x => x.Permission.IsActive && x.Permission.Code == code));

    public async Task<bool> HasAsync(string code, CancellationToken ct = default)
    {
        try { return HasPermission(await users.GetCurrentAsync(ct), code); }
        catch (PortalAccessException) { return false; }
    }

    public async Task RequireAsync(string code, CancellationToken ct = default)
    {
        if (!await HasAsync(code, ct)) throw new PortalAccessException();
    }
}
