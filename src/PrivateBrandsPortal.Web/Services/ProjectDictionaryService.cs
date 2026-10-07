using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using PrivateBrandsPortal.Web.Data;
using PrivateBrandsPortal.Web.ViewModels;
namespace PrivateBrandsPortal.Web.Services;
public sealed record CustomerOption(int Id, string Name, int? DefaultCountryId);
public sealed record SubcategoryOption(int Id, int CategoryId, string Name);
public sealed class ProjectDictionaryService(ApplicationDbContext db)
{
    private Dictionary<int, Models.Entities.FormulaOption>? formulas;
    public async Task<IReadOnlyList<LookupItem>> FormulasAsync(CancellationToken ct = default) =>
        await db.FormulaOptions.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name)
            .Select(x => new LookupItem(x.Id, x.Name)).ToListAsync(ct);

    public async Task ResolveFormulaAsync(ProductInput input, Models.Entities.ProjectProduct? saved, CancellationToken ct)
    {
        formulas ??= await db.FormulaOptions.AsNoTracking().ToDictionaryAsync(x=>x.Id,ct);
        if (saved is not null && saved.FormulaOptionId == input.FormulaOptionId)
        {
            input.FormulaStatus = saved.FormulaStatus;
            input.FormulaName = saved.FormulaOptionId.HasValue
                ? formulas.GetValueOrDefault(saved.FormulaOptionId.Value)?.Name : null;
            return;
        }
        var formula = input.FormulaOptionId.HasValue ? formulas.GetValueOrDefault(input.FormulaOptionId.Value) : null;
        if(formula is null || !formula.IsActive) throw new ValidationException("Select an active formula option.");
        input.FormulaName = formula.Name;
        // The legacy column remains for historical compatibility; dictionary identity drives all new UI.
        input.FormulaStatus = formula.Code == "READY_TO_GO" ? Models.Enums.FormulaStatus.ReadyToGo : Models.Enums.FormulaStatus.NewFormula;
    }
    private Dictionary<int, SubcategoryOption>? activeSubcategories;
    public async Task<IReadOnlyList<CustomerOption>> CustomersAsync(CancellationToken ct = default) =>
        await db.Customers.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name)
            .Select(x => new CustomerOption(x.Id, x.Name, x.DefaultCountry != null && x.DefaultCountry.IsActive ? x.DefaultCountryId : null)).ToListAsync(ct);
    public async Task<IReadOnlyList<SubcategoryOption>> SubcategoriesAsync(CancellationToken ct = default) =>
        await db.ProductSubcategories.AsNoTracking().Where(x => x.IsActive && x.ProductCategory.IsActive)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name).Select(x => new SubcategoryOption(x.Id, x.ProductCategoryId, x.Name)).ToListAsync(ct);
    public async Task ResolveBriefAsync(BriefInput input, BriefInput? saved, CancellationToken ct)
    {
        if (saved is not null && saved.CustomerId == input.CustomerId) { input.Customer = saved.Customer; return; }
        var customer = await db.Customers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == input.CustomerId && x.IsActive, ct)
            ?? throw new ValidationException("Select an active customer.");
        input.Customer = customer.Name;
    }
    public async Task ResolveProductAsync(ProductInput input, ProductInput? saved, CancellationToken ct)
    {
        if (saved is not null && input.ProductCategoryId == saved.ProductCategoryId && input.ProductSubcategoryId == saved.ProductSubcategoryId)
        { input.Subcategory = saved.Subcategory; return; }
        activeSubcategories ??= (await SubcategoriesAsync(ct)).ToDictionary(x => x.Id);
        var subcategory = input.ProductSubcategoryId is int id ? activeSubcategories.GetValueOrDefault(id) : null;
        if (subcategory is null || subcategory.CategoryId != input.ProductCategoryId)
            throw new ValidationException("Select an active subcategory belonging to the selected category.");
        input.Subcategory = subcategory.Name;
    }
}
