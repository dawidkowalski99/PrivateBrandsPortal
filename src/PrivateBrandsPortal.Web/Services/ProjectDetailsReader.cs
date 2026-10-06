using Microsoft.EntityFrameworkCore;
using PrivateBrandsPortal.Web.Data;
using PrivateBrandsPortal.Web.Models.Entities;
using PrivateBrandsPortal.Web.Models.Enums;
using PrivateBrandsPortal.Web.ViewModels;
namespace PrivateBrandsPortal.Web.Services;

// One shared read model for the owner's details and the manager's review screen.
public static class ProjectDetailsReader
{
    public static async Task<ProjectDetailsViewModel?> ReadAsync(ApplicationDbContext db, IQueryable<Project> query, CancellationToken ct)
    {
        var p = await query.AsNoTracking().Include(x => x.Country).Include(x => x.ProjectManager)
            .Include(x => x.Products).ThenInclude(x => x.ProductType).Include(x => x.Products).ThenInclude(x => x.ProductCategory)
            .Include(x => x.Products).ThenInclude(x => x.Reviews).ThenInclude(x => x.Reviewer).Include(x => x.Products).ThenInclude(x => x.Reviews).ThenInclude(x => x.RejectionReason)
            .AsSplitQuery().SingleOrDefaultAsync(ct);
        if (p is null) return null;
        var ids = p.Products.Select(x => x.Id).ToArray();
        var changes = await db.AuditLogs.AsNoTracking()
            .Where(x => x.EntityType == nameof(ProjectProduct) && ids.Contains(x.EntityId) && (x.ChangeType == AuditChangeType.ManagerEdit || x.FieldName == "CommercialStatus"))
            .Include(x => x.ChangedByUser).OrderBy(x => x.ChangedAtUtc).ThenBy(x => x.Id).ToListAsync(ct);
        var typeIds = changes.Where(a => a.FieldName == nameof(ProjectProduct.ProductTypeId))
            .SelectMany(a => new[] { a.OldValue, a.NewValue }).Select(v => int.TryParse(v, out var id) ? id : 0).Distinct().ToArray();
        var typeNames = typeIds.Length == 0 ? new Dictionary<int, string>() :
            await db.ProductTypes.AsNoTracking().Where(t => typeIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        string? TypeName(string? value) => int.TryParse(value, out var id) ? typeNames.GetValueOrDefault(id) : null;
        var categories = await db.ProductCategories.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        string? CategoryName(string? value) => int.TryParse(value, out var id) ? categories.GetValueOrDefault(id) : null;
        return new ProjectDetailsViewModel {
            Transfers = await db.AuditLogs.AsNoTracking().Where(x => x.EntityType == nameof(Project) && x.EntityId == p.Id && x.ChangeType == AuditChangeType.ProjectReassigned)
                .OrderByDescending(x => x.ChangedAtUtc).ThenByDescending(x => x.Id)
                .Select(x => new ProjectTransferHistory(x.OldValue, x.NewValue, x.ChangedByUser.DisplayName, x.ChangedAtUtc, x.Reason)).ToListAsync(ct),
            Id = p.Id, ProjectNumber = p.ProjectNumber, Customer = p.Customer, Country = p.Country.Name,
            ProjectManager = Name(p.ProjectManager), Status = p.Status, CreatedAtUtc = p.CreatedAtUtc,
            UpdatedAtUtc = p.UpdatedAtUtc, SubmittedAtUtc = p.SubmittedAtUtc, ArchivedAtUtc = p.ArchivedAtUtc,
            Products = p.Products.OrderBy(x => x.Id).Select(x => new ProductCardViewModel(
                x.Subcategory ?? x.ProductType?.Name ?? "Legacy product", x.SKU, x.Quantity, x.EstimatedValue, x.EstimatedMargin, x.FormulaStatus) {
                Id = x.Id, ProductTypeId = x.ProductTypeId, ProductCategoryId = x.ProductCategoryId, ProductSubcategoryId = x.ProductSubcategoryId, Category = x.ProductCategory?.Name, CommercialStatus = x.CommercialStatus, UpdatedAtUtc = x.UpdatedAtUtc, ReviewStatus = x.ReviewStatus,
                Reviews = x.Reviews.OrderBy(r => r.ReviewedAtUtc).ThenBy(r => r.Id)
                    .Select(r => new ReviewHistoryItem(r.Decision, r.Comment, Name(r.Reviewer), r.ReviewedAtUtc) { RejectionReason = r.RejectionReasonName ?? r.RejectionReason?.Name }).ToList(),
                CommercialHistory = changes.Where(a => a.EntityId == x.Id && a.FieldName == "CommercialStatus").Select(a => new ManagerChange(a.FieldName,a.OldValue,a.NewValue,Name(a.ChangedByUser),a.ChangedAtUtc)).ToList(), Changes = changes.Where(a => a.EntityId == x.Id && a.ChangeType == AuditChangeType.ManagerEdit).Select(a => new ManagerChange(a.FieldName,
                    a.OldValue, a.NewValue, Name(a.ChangedByUser), a.ChangedAtUtc) { OldProductTypeName = a.FieldName == "ProductCategoryId" ? CategoryName(a.OldValue) : TypeName(a.OldValue), NewProductTypeName = a.FieldName == "ProductCategoryId" ? CategoryName(a.NewValue) : TypeName(a.NewValue) }).ToList()
            }).ToList()
        };
    }
    public static string Name(AppUser user) => string.IsNullOrWhiteSpace(user.DisplayName) ? user.DomainLogin : user.DisplayName;
}
