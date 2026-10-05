using System.ComponentModel.DataAnnotations;
using System.Data;
using Microsoft.EntityFrameworkCore;
using PrivateBrandsPortal.Web.Data;
using PrivateBrandsPortal.Web.Interfaces;
using PrivateBrandsPortal.Web.Models.Entities;
using PrivateBrandsPortal.Web.Models.Enums;
using PrivateBrandsPortal.Web.ViewModels;

namespace PrivateBrandsPortal.Web.Services;

public sealed class ProjectTransferService(ApplicationDbContext db, IAppUserService users,
    IPermissionService permissions, TimeProvider clock)
{
    private static string Identity(AppUser user) => $"{ProjectDetailsReader.Name(user)} ({user.DomainLogin}; #{user.Id})";
    public async Task<ProjectTransferPage?> FormAsync(int id, CancellationToken ct)
    {
        await permissions.RequireAsync(PermissionCodes.ReassignProjects, ct);
        var actor = await users.GetCurrentAsync(ct);
        var project = await db.Projects.AsNoTracking().Include(x => x.ProjectManager).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (project is null) return null;
        var candidates = await db.AppUsers.AsNoTracking().Where(x => x.IsActive && x.Id != project.ProjectManagerId &&
            (x.Role == AppRole.ProjectManager || x.Role == AppRole.SuperAdmin)).OrderBy(x => x.DisplayName).ThenBy(x => x.Id)
            .Select(x => new { x.Id, x.DisplayName, x.DomainLogin }).ToListAsync(ct);
        return new() {
            ProjectNumber = project.ProjectNumber, CurrentProjectManager = Identity(project.ProjectManager),
            CanOpenProject = project.ProjectManagerId == actor.Id && WorkflowAccess.Allows(actor, AppRole.ProjectManager),
            Input = new() { ProjectId = id, Version = project.UpdatedAtUtc },
            ProjectManagers = candidates.Select(x => new LookupItem(x.Id, $"{x.DisplayName} ({x.DomainLogin})")).ToList()
        };
    }
    public async Task TransferAsync(ProjectTransferInput input, CancellationToken ct = default)
    {
        input.Reason = (input.Reason ?? "").Trim();
        Validator.ValidateObject(input, new ValidationContext(input), true);
        await permissions.RequireAsync(PermissionCodes.ReassignProjects, ct);
        var actorId = (await users.GetCurrentAsync(ct)).Id;
        var project = await db.Projects.AsNoTracking().Include(x => x.ProjectManager).SingleOrDefaultAsync(x => x.Id == input.ProjectId, ct)
            ?? throw new ValidationException("Project unavailable.");
        // Keep the actor's grants and target eligibility stable until commit.
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var actor = await db.AppUsers.AsNoTracking().Include(x => x.Permissions).ThenInclude(x => x.Permission)
            .SingleOrDefaultAsync(x => x.Id == actorId, ct);
        if (actor is null || !PermissionService.HasPermission(actor, PermissionCodes.ReassignProjects)) throw new PortalAccessException();
        if (project.UpdatedAtUtc != input.Version)
            throw new ValidationException("The project has changed. Reload it and try again.");
        var target = await db.AppUsers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == input.NewProjectManagerId, ct);
        if (target is null || !WorkflowAccess.Allows(target, AppRole.ProjectManager))
            throw new ValidationException("Select an active Project Manager or SuperAdmin.");
        // The atomic versioned UPDATE below is the serialization point for project changes.
        if (project.ProjectManagerId == target.Id) throw new ValidationException("Select a different Project Manager.");
        var now = clock.GetUtcNow(); if (now <= project.UpdatedAtUtc) now = project.UpdatedAtUtc.AddTicks(1);
        var changed = await db.Projects.Where(x => x.Id == input.ProjectId && x.UpdatedAtUtc == input.Version && x.ProjectManagerId == project.ProjectManagerId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ProjectManagerId, target.Id).SetProperty(x => x.UpdatedAtUtc, now), ct);
        if (changed != 1) throw new ValidationException("The project has changed. Reload it and try again.");
        db.AuditLogs.Add(new AuditLog { EntityType = nameof(Project), EntityId = project.Id, FieldName = "ProjectManager",
            OldValue = Identity(project.ProjectManager), NewValue = Identity(target), ChangedByUserId = actor.Id,
            ChangedAtUtc = now, ChangeType = AuditChangeType.ProjectReassigned, Reason = input.Reason });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
