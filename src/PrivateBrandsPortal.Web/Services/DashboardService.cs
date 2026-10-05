using Microsoft.EntityFrameworkCore;
using PrivateBrandsPortal.Web.Data;
using PrivateBrandsPortal.Web.Interfaces;
using PrivateBrandsPortal.Web.Models.Entities;
using PrivateBrandsPortal.Web.Models.Enums;
using PrivateBrandsPortal.Web.ViewModels;

namespace PrivateBrandsPortal.Web.Services;

public sealed class DashboardService(ApplicationDbContext db, IAppUserService users,
    IProjectService projects, TimeProvider clock) : IDashboardService
{
    private async Task<AppUser> Current(CancellationToken ct)
    {
        var user = await users.GetCurrentAsync(ct);
        if (!user.IsActive) throw new PortalAccessException();
        return user;
    }
    private IQueryable<Project> Scope(AppUser user) => user.Role == AppRole.SuperAdmin
        ? db.Projects.AsNoTracking() : db.Projects.AsNoTracking().Where(x => x.ProjectManagerId == user.Id);

    private static IQueryable<DashboardProject> Rows(IQueryable<Project> query, int owner) => query
        .OrderByDescending(x => x.UpdatedAtUtc).ThenByDescending(x => x.Id)
        .Select(x => new DashboardProject(x.Id, x.ProjectNumber, x.Customer, x.Country.Name,
            x.ProjectManager.DisplayName, x.Products.Count, x.Status, x.UpdatedAtUtc, x.ProjectManagerId == owner));

    public async Task<DashboardViewModel> GetAsync(CancellationToken ct = default)
    {
        var user = await Current(ct);
        var query = Scope(user);
        var now = clock.GetUtcNow(); var from = now.AddDays(-7);
        var counts = await query.GroupBy(x => 1).Select(g => new {
            Awaiting = g.Count(x => x.Status == ProjectStatus.AwaitingManagerReview || x.Status == ProjectStatus.PartiallyReviewed),
            Recent = g.Count(x => x.UpdatedAtUtc >= from && x.UpdatedAtUtc <= now),
            Completed = g.Count(x => x.ArchivedAtUtc != null)
        }).SingleOrDefaultAsync(ct);
        return new() {
            IsGlobal = user.Role == AppRole.SuperAdmin, CanCreate = WorkflowAccess.Allows(user, AppRole.ProjectManager),
            ActiveProjects = await query.Active().CountAsync(ct), AwaitingApproval = counts?.Awaiting ?? 0,
            RecentlyChanged = counts?.Recent ?? 0, Completed = counts?.Completed ?? 0,
            AwaitingPmUpdate = await projects.AwaitingPmAsync(ct), RecentProjects = await Rows(query, user.Id).Take(6).ToListAsync(ct)
        };
    }

    public async Task<AwaitingPmPage> AwaitingAsync(int page, CancellationToken ct = default)
    {
        var user = await Current(ct);
        if (!WorkflowAccess.Allows(user, AppRole.ProjectManager)) throw new PortalAccessException();
        var products = db.ProjectProducts.AsNoTracking().AwaitingPmUpdate();
        var query = Scope(user).Where(p => products.Any(x => x.ProjectId == p.Id));
        var pages = Math.Max(1, (int)Math.Ceiling(await query.CountAsync(ct) / 50m));
        page = Math.Clamp(page, 1, pages);
        return new(await Rows(query, user.Id).Skip((page - 1) * 50).Take(50).ToListAsync(ct), page, pages);
    }

    public async Task<ProjectDetailsViewModel?> OverviewAsync(int id, CancellationToken ct = default)
    {
        var user = await Current(ct);
        if (user.Role != AppRole.SuperAdmin) throw new PortalAccessException();
        return await ProjectDetailsReader.ReadAsync(db, db.Projects.Where(x => x.Id == id), ct);
    }
}
