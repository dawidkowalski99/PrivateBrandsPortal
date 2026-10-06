using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using PrivateBrandsPortal.Web.Data;
using PrivateBrandsPortal.Web.Interfaces;
using PrivateBrandsPortal.Web.Models.Entities;
using PrivateBrandsPortal.Web.Models.Enums;
using PrivateBrandsPortal.Web.ViewModels;

namespace PrivateBrandsPortal.Web.Services;

public sealed class UserAdministrationService(ApplicationDbContext db, IAppUserService users,
    IPermissionService permissions, TimeProvider clock)
{
    public static void ProtectLastSuperAdmin(AppUser target, UserEditInput input, bool hasOtherActiveSuperAdmin)
    {
        if (target.Role == AppRole.SuperAdmin && target.IsActive && (input.Role != AppRole.SuperAdmin || !input.IsActive)
            && !hasOtherActiveSuperAdmin)
            throw new ValidationException("The last active SuperAdmin cannot be deactivated or lose the SuperAdmin role.");
    }
    public async Task<UserListPage> ListAsync(string? search, CancellationToken ct)
    {
        await permissions.RequireAsync(PermissionCodes.ManageUsers, ct);
        search = search?.Trim();
        if (search?.Length > 200) throw new ValidationException("Search supports up to 200 characters.");
        var query = db.AppUsers.AsNoTracking();
        if (!string.IsNullOrEmpty(search)) query = query.Where(x => x.DomainLogin.Contains(search) || x.DisplayName.Contains(search));
        var rows = await query.OrderBy(x => x.DomainLogin).Select(x => new UserListItem(x.Id, x.DomainLogin,
            x.DisplayName, x.Role, x.IsActive, x.CreatedAtUtc, x.UpdatedAtUtc,
            x.Permissions.Where(p => p.Permission.IsActive).OrderBy(p => p.Permission.DisplayOrder).Select(p => p.Permission.Name).ToList())).ToListAsync(ct);
        return new(search, rows);
    }

    public async Task<UserEditPage?> GetAsync(int id, CancellationToken ct)
    {
        await permissions.RequireAsync(PermissionCodes.ManageUsers, ct);
        var actor = await users.GetCurrentAsync(ct);
        var target = await db.AppUsers.AsNoTracking().Include(x => x.Permissions).ThenInclude(x => x.Permission).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (target is null) return null;
        if (target.Role == AppRole.SuperAdmin && actor.Role != AppRole.SuperAdmin) throw new PortalAccessException();
        return new UserEditPage {
            Input = new() { Id = id, Version = target.UpdatedAtUtc, Role = target.Role, IsActive = target.IsActive,
                PermissionIds = target.Permissions.Where(x => x.Permission.Code != PermissionCodes.ReassignProjects).Select(x => x.PermissionId).ToList() },
            DomainLogin = target.DomainLogin, DisplayName = target.DisplayName,
            CanAssignSuperAdmin = actor.Role == AppRole.SuperAdmin,
            Permissions = await db.Permissions.AsNoTracking().Where(x => x.Code != PermissionCodes.ReassignProjects).OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name)
                .Select(x => new PermissionOption(x.Id, x.Name, x.IsActive)).ToListAsync(ct)
        };
    }

    public async Task SaveAsync(UserEditInput input, CancellationToken ct)
    {
        Validator.ValidateObject(input, new ValidationContext(input), true);
        var actorId = (await users.GetCurrentAsync(ct)).Id;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Serialize administrative changes, including two concurrent attempts to remove the last admins.
        // The DEV bootstrap helper uses the same transaction-owned lock.
        await db.Database.ExecuteSqlRawAsync("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'PrivateBrandsPortal.UserAdministration', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=15000; IF @r < 0 THROW 51000, 'User administration is busy. Try again.', 1;", ct);
        var actor = await db.AppUsers.AsNoTracking().Include(x => x.Permissions).ThenInclude(x => x.Permission)
            .SingleAsync(x => x.Id == actorId, ct);
        if (!PermissionService.HasPermission(actor, PermissionCodes.ManageUsers)) throw new PortalAccessException();
        var target = await db.AppUsers.Include(x => x.Permissions).SingleOrDefaultAsync(x => x.Id == input.Id, ct)
            ?? throw new ValidationException("User unavailable.");
        if (target.UpdatedAtUtc != input.Version) throw new ValidationException("User changed. Reopen the form.");
        if (actor.Role != AppRole.SuperAdmin && (target.Role == AppRole.SuperAdmin || input.Role == AppRole.SuperAdmin))
            throw new PortalAccessException();
        ProtectLastSuperAdmin(target, input,
            await db.AppUsers.AnyAsync(x => x.Id != target.Id && x.IsActive && x.Role == AppRole.SuperAdmin, ct));
        var ids = input.PermissionIds.Distinct().ToArray();
        if (await db.Permissions.CountAsync(x => ids.Contains(x.Id) && x.IsActive && x.Code != PermissionCodes.ReassignProjects, ct) != ids.Length)
            throw new ValidationException("Select active permissions only.");
        var now = clock.GetUtcNow();
        if (now <= target.UpdatedAtUtc) now = target.UpdatedAtUtc.AddTicks(1);
        void Audit(string field, string oldValue, string newValue) {
            if (oldValue != newValue) db.AuditLogs.Add(new AuditLog { EntityType = nameof(AppUser), EntityId = target.Id,
                FieldName = field, OldValue = oldValue, NewValue = newValue, ChangedByUserId = actor.Id,
                ChangedAtUtc = now, ChangeType = AuditChangeType.Updated });
        }
        Audit(nameof(AppUser.Role), target.Role.ToString(), input.Role.ToString());
        Audit(nameof(AppUser.IsActive), target.IsActive.ToString(), input.IsActive.ToString());
        Audit("Permissions", string.Join(',', target.Permissions.Select(x => x.PermissionId).Order()), string.Join(',', ids.Order()));
        target.Role = input.Role; target.IsActive = input.IsActive; target.UpdatedAtUtc = now;
        db.AppUserPermissions.RemoveRange(target.Permissions.Where(x => !ids.Contains(x.PermissionId)));
        foreach (var id in ids.Where(id => target.Permissions.All(x => x.PermissionId != id)))
            db.AppUserPermissions.Add(new AppUserPermission { AppUserId = target.Id, PermissionId = id });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
