using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using PrivateBrandsPortal.Web.Data;
using PrivateBrandsPortal.Web.Interfaces;
using PrivateBrandsPortal.Web.Models.Entities;
using PrivateBrandsPortal.Web.Models.Enums;
using PrivateBrandsPortal.Web.ViewModels;
namespace PrivateBrandsPortal.Web.Services;

public sealed class ProjectService(ApplicationDbContext db, IAppUserService users,
    IProjectNumberGenerator numbers, TimeProvider clock, AttachmentService? attachments = null) : IProjectService
{
    private async Task<AppUser> OwnerAsync(CancellationToken ct)
    {
        var user = await users.GetCurrentAsync(ct);
        if (!WorkflowAccess.Allows(user, AppRole.ProjectManager)) throw new PortalAccessException();
        return user;
    }
    public async Task<int> AwaitingPmAsync(CancellationToken ct = default)
    {
        var user=await users.GetCurrentAsync(ct);
        if(!WorkflowAccess.Allows(user, AppRole.ProjectManager))return 0;
        var query = db.ProjectProducts.AsNoTracking().AwaitingPmUpdate();
        if(user.Role != AppRole.SuperAdmin) query = query.Where(x => x.Project.ProjectManagerId == user.Id);
        return await query.CountAsync(ct);
    }
    public async Task<IReadOnlyList<LookupItem>> CountriesAsync(CancellationToken ct = default) =>
        await db.Countries.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name)
            .Select(x => new LookupItem(x.Id, x.Name)).ToListAsync(ct);
    public async Task<IReadOnlyList<LookupItem>> ProductCategoriesAsync(CancellationToken ct = default) =>
        await db.ProductCategories.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name)
            .Select(x => new LookupItem(x.Id, x.Name)).ToListAsync(ct);
    public async Task<IReadOnlyList<ProjectListItemViewModel>> ListAsync(CancellationToken ct = default, string? search = null)
    {
        var user = await OwnerAsync(ct);
        return await db.Projects.AsNoTracking().Where(x => (user.Role == AppRole.SuperAdmin || x.ProjectManagerId == user.Id) && x.ArchivedAtUtc == null).Search(search)
            .OrderByDescending(x => x.UpdatedAtUtc).ThenByDescending(x => x.Id)
            .Select(x => new ProjectListItemViewModel {
                Id = x.Id, ProjectNumber = x.ProjectNumber, Customer = x.Customer, Country = x.Country.Name,
                Status = x.Status, ProductCount = x.Products.Count,
                EstimatedValue = x.Products.Sum(p => p.EstimatedValue),
                CreatedAtUtc = x.CreatedAtUtc, UpdatedAtUtc = x.UpdatedAtUtc }).ToListAsync(ct);
    }
    public async Task<ProjectDetailsViewModel?> DetailsAsync(int id, CancellationToken ct = default)
    {
        var user = await OwnerAsync(ct);
        return await ProjectDetailsReader.ReadAsync(db,
            db.Projects.Where(x => x.Id == id && x.ProjectManagerId == user.Id), ct);
    }
    public async Task<DraftInput?> LoadDraftAsync(int id, CancellationToken ct = default)
    {
        var user = await OwnerAsync(ct);
        var project = await db.Projects.AsNoTracking().Include(x => x.Products).ThenInclude(x => x.ProductType).Include(x => x.Products).ThenInclude(x => x.FormulaOption)
            .SingleOrDefaultAsync(x => x.Id == id && x.ProjectManagerId == user.Id && x.Status == ProjectStatus.Draft, ct);
        if (project is null) return null;
        return new DraftInput {
            ProjectId = project.Id, OriginalUpdatedAtUtc = project.UpdatedAtUtc,
            Brief = new BriefInput { CustomerId = project.CustomerId, Customer = project.Customer, CountryId = project.CountryId },
            Products = project.Products.OrderBy(x => x.Id).Select(x => new ProductInput {
                PersistedId = x.Id, ProductTypeId = x.ProductTypeId, ProductCategoryId = x.ProductCategoryId, ProductSubcategoryId = x.ProductSubcategoryId, Subcategory = x.Subcategory ?? x.ProductType?.Name ?? "Legacy product", SKU = x.SKU, Quantity = x.Quantity,
                EstimatedValue = x.EstimatedValue, EstimatedMargin = x.EstimatedMargin, FormulaOptionId=x.FormulaOptionId, FormulaName=x.FormulaOption?.Name, FormulaStatus = x.FormulaStatus
            }).ToList()
        };
    }
    public static void Validate(DraftInput input, bool allowLegacyCategories = false)
    {
        input.Brief.Customer = (input.Brief.Customer ?? "").Trim();
        Validator.ValidateObject(input.Brief, new ValidationContext(input.Brief), true);
        if (!allowLegacyCategories && input.Brief.CustomerId is null) throw new ValidationException("Select a customer.");
        if (input.Products.Count == 0) throw new ValidationException("Add at least one product.");
        if (input.Products.Count > 500) throw new ValidationException("A draft supports up to 500 products.");
        foreach (var product in input.Products)
        {
            if (!allowLegacyCategories && product.ProductSubcategoryId is null) throw new ValidationException("Select a product subcategory.");
            product.SKU = (product.SKU ?? "").Trim(); product.Subcategory = (product.Subcategory ?? "").Trim();
            var errors = new List<ValidationResult>();
            Validator.TryValidateObject(product, new ValidationContext(product), errors, true);
            var error = errors.FirstOrDefault(e => !(allowLegacyCategories && product.ProductCategoryId is null && e.MemberNames.SequenceEqual(new[] { nameof(ProductInput.ProductCategoryId) })));
            if (error is not null) throw new ValidationException(error.ErrorMessage);
        }
        if (input.Products.Select(p => p.Key).Distinct().Count() != input.Products.Count)
            throw new ValidationException("Duplicate product keys.");
    }
    public async Task<int> SaveDraftAsync(DraftInput input, CancellationToken ct = default)
    {
        var user = await OwnerAsync(ct);
        Validate(input, allowLegacyCategories: true);
        if (!await db.Countries.AnyAsync(x => x.Id == input.Brief.CountryId && x.IsActive, ct))
            throw new ValidationException("Select an active country.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var now = clock.GetUtcNow();
        Project project;
        if (input.ProjectId is int id)
        {
            if (input.OriginalUpdatedAtUtc is not { } original) throw new ValidationException("Missing draft version.");
            if (now <= original) now = original.AddTicks(1);
            // Compare-and-swap obtains a write lock until commit. No silent last-write-wins.
            var changed = await db.Projects.Where(x => x.Id == id && x.ProjectManagerId == user.Id
                && x.Status == ProjectStatus.Draft && x.UpdatedAtUtc == original)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.UpdatedAtUtc, now), ct);
            if (changed != 1) throw new ValidationException("This draft changed or is unavailable. Reopen it from Projects before editing.");
            project = await db.Projects.Include(x => x.Products).SingleAsync(x => x.Id == id && x.ProjectManagerId == user.Id, ct);
            if (project.Products.Any(x => x.ReviewStatus != ProductReviewStatus.Pending)
                || await db.ProductReviews.AnyAsync(x => x.ProjectProduct.ProjectId == id, ct))
                throw new ValidationException("Reviewed products cannot be edited as a draft.");
            var existingIds = project.Products.Select(x => x.Id).ToHashSet();
            var submittedIds = input.Products.Where(x => x.PersistedId.HasValue).Select(x => x.PersistedId!.Value).ToArray();
            if (submittedIds.Distinct().Count() != submittedIds.Length || submittedIds.Any(x => !existingIds.Contains(x)))
                throw new ValidationException("Invalid product reference.");
            var removedIds=project.Products.Where(x=>!submittedIds.Contains(x.Id)).Select(x=>x.Id).ToArray();
            if(await db.ProjectAttachments.AnyAsync(x=>x.ProjectProductId.HasValue && removedIds.Contains(x.ProjectProductId.Value),ct))throw new ValidationException("A product with attachments cannot be removed. Keep its document history.");
            db.ProjectProducts.RemoveRange(project.Products.Where(x => !submittedIds.Contains(x.Id)));
        }
        else
        {
            if (input.Products.Any(x => x.PersistedId.HasValue)) throw new ValidationException("Invalid product reference.");
            project = new Project { ProjectNumber = await numbers.GenerateAsync(ct), Customer = input.Brief.Customer,
                ProjectManagerId = user.Id, CreatedByUserId = user.Id, RequiresManagerApproval = user.Role == AppRole.ProjectManager, CreatedAtUtc = now, UpdatedAtUtc = now, Status = ProjectStatus.Draft };
            db.Projects.Add(project);
        }
        var lookups = new ProjectDictionaryService(db);
        await lookups.ResolveBriefAsync(input.Brief, input.ProjectId.HasValue
            ? new BriefInput { CustomerId = project.CustomerId, Customer = project.Customer } : null, ct);
        project.CustomerId = input.Brief.CustomerId;
        project.Customer = input.Brief.Customer;
        project.CountryId = input.Brief.CountryId!.Value;
        project.UpdatedAtUtc = now;
        foreach (var item in input.Products)
        {
            var product = item.PersistedId is int productId
                ? project.Products.Single(x => x.Id == productId)
                : new ProjectProduct { SKU = item.SKU, CreatedAtUtc = now, ReviewStatus = ProductReviewStatus.Pending };
            await lookups.ResolveProductAsync(item, item.PersistedId.HasValue ? new ProductInput {
                ProductCategoryId = product.ProductCategoryId, ProductSubcategoryId = product.ProductSubcategoryId, Subcategory = product.Subcategory ?? ""
            } : null, ct);
            if (item.PersistedId is null) project.Products.Add(product);
            // Keep a historical null snapshot (ProductType fallback) when the dictionary selection is unchanged.
            if (product.Subcategory is not null || !item.PersistedId.HasValue || product.ProductCategoryId != item.ProductCategoryId || product.ProductSubcategoryId != item.ProductSubcategoryId)
                product.Subcategory = item.Subcategory;
            product.ProductSubcategoryId = item.ProductSubcategoryId;
            product.ProductCategoryId = item.ProductCategoryId; // Preserve legacy ProductTypeId on existing products.
            product.SKU = item.SKU;
            product.Quantity = item.Quantity!.Value;
            product.EstimatedValue = item.EstimatedValue!.Value;
            product.EstimatedMargin = item.EstimatedMargin!.Value;
            await lookups.ResolveFormulaAsync(item, item.PersistedId.HasValue ? product : null, ct);
            product.FormulaOptionId = item.FormulaOptionId;
            product.FormulaStatus = item.FormulaStatus!.Value;
            product.UpdatedAtUtc = now;
        }
        await db.SaveChangesAsync(ct);
        var createdKeys = new List<string>();
        try {
            if(input.TemporaryBriefs.Count>0) {
                if(attachments is null) throw new ValidationException("Attachment storage is unavailable.");
                await attachments.PromoteBriefsAsync(project,input.TemporaryBriefs,input.WizardToken,createdKeys,ct);
                await db.SaveChangesAsync(ct);
            }
            await transaction.CommitAsync(ct);
        } catch { if(attachments is not null) await attachments.CleanupFilesAsync(createdKeys); throw; }
        if(attachments is not null && input.TemporaryBriefs.Count>0) {
            await attachments.CleanupFilesAsync(input.TemporaryBriefs.Select(x=>x.StorageKey));
            input.TemporaryBriefs.Clear();
        }
        return project.Id;
    }
}
