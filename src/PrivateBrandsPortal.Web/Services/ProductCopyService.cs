using Microsoft.EntityFrameworkCore;
using PrivateBrandsPortal.Web.Data;
using PrivateBrandsPortal.Web.Interfaces;
using PrivateBrandsPortal.Web.Models.Enums;
using PrivateBrandsPortal.Web.ViewModels;

namespace PrivateBrandsPortal.Web.Services;

public sealed record CopySource(int Id, string ProjectNumber, string Customer, string Subcategory, string SKU);
public sealed class ProductCopyService(ApplicationDbContext db, IAppUserService users)
{
    private async Task<int> Owner(CancellationToken ct)
    {
        var user = await users.GetCurrentAsync(ct);
        if (!WorkflowAccess.Allows(user, AppRole.ProjectManager)) throw new PortalAccessException();
        return user.Id;
    }
    public async Task<IReadOnlyList<CopySource>> SourcesAsync(string? search, CancellationToken ct)
    {
        var owner = await Owner(ct);
        var query = db.ProjectProducts.AsNoTracking().Where(x => x.Project.ProjectManagerId == owner);
        if (!string.IsNullOrWhiteSpace(search)) {
            var term = search.Trim();
            query = query.Where(x => x.SKU.Contains(term) || x.Project.ProjectNumber.Contains(term) || x.Project.Customer.Contains(term));
        }
        return await query.OrderByDescending(x => x.Project.UpdatedAtUtc).ThenBy(x => x.Id).Take(100)
            .Select(x => new CopySource(x.Id, x.Project.ProjectNumber, x.Project.Customer,
                x.Subcategory ?? EF.Functions.Collate(x.ProductType!.Name, "Latin1_General_100_CI_AS"), x.SKU)).ToListAsync(ct);
    }
    public async Task<ProductInput?> CopyAsync(int sourceId, CancellationToken ct)
    {
        var owner = await Owner(ct);
        var source = await db.ProjectProducts.AsNoTracking().Include(x => x.ProductType).Include(x => x.ProductCategory)
            .SingleOrDefaultAsync(x => x.Id == sourceId && x.Project.ProjectManagerId == owner, ct);
        return source is null ? null : new ProductInput {
            ProductCategoryId = source.ProductCategory?.IsActive == true ? source.ProductCategoryId : null,
            Subcategory = source.Subcategory ?? source.ProductType?.Name ?? "", SKU = source.SKU,
            Quantity = source.Quantity, EstimatedValue = source.EstimatedValue,
            EstimatedMargin = source.EstimatedMargin, FormulaStatus = source.FormulaStatus
        };
    }
    public static ProductInput Duplicate(ProductInput source) => new() {
        ProductCategoryId = source.ProductCategoryId, Subcategory = source.Subcategory, SKU = source.SKU,
        Quantity = source.Quantity, EstimatedValue = source.EstimatedValue,
        EstimatedMargin = source.EstimatedMargin, FormulaStatus = source.FormulaStatus
    };
}
