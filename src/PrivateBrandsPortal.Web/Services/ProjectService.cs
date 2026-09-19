using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using PrivateBrandsPortal.Web.Data;
using PrivateBrandsPortal.Web.Interfaces;
using PrivateBrandsPortal.Web.Models.Entities;
using PrivateBrandsPortal.Web.Models.Enums;
using PrivateBrandsPortal.Web.ViewModels;
namespace PrivateBrandsPortal.Web.Services;

public sealed class ProjectService(ApplicationDbContext db, IAppUserService users,
    IProjectNumberGenerator numbers, TimeProvider clock) : IProjectService
{
    private async Task<AppUser> OwnerAsync(CancellationToken ct)
    {
        var user = await users.GetCurrentAsync(ct);
        if (!user.IsActive || user.Role != AppRole.ProjectManager) throw new PortalAccessException();
        return user;
    }
    public async Task<IReadOnlyList<LookupItem>> CountriesAsync(CancellationToken ct = default) =>
        await db.Countries.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name)
            .Select(x => new LookupItem(x.Id, x.Name)).ToListAsync(ct);
    public async Task<IReadOnlyList<LookupItem>> ProductTypesAsync(CancellationToken ct = default) =>
        await db.ProductTypes.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name)
            .Select(x => new LookupItem(x.Id, x.Name)).ToListAsync(ct);
    public async Task<IReadOnlyList<ProjectListItemViewModel>> ListAsync(CancellationToken ct = default)
    {
        var user = await OwnerAsync(ct);
        return await db.Projects.AsNoTracking().Where(x => x.ProjectManagerId == user.Id)
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
        return await db.Projects.AsNoTracking().Where(x => x.Id == id && x.ProjectManagerId == user.Id)
            .Select(x => new ProjectDetailsViewModel {
                Id = x.Id, ProjectNumber = x.ProjectNumber, Customer = x.Customer, Country = x.Country.Name,
                ProjectManager = x.ProjectManager.DisplayName, Status = x.Status,
                CreatedAtUtc = x.CreatedAtUtc, UpdatedAtUtc = x.UpdatedAtUtc,
                Products = x.Products.OrderBy(p => p.Id).Select(p => new ProductCardViewModel(
                    p.ProductType.Name, p.SKU, p.Quantity, p.EstimatedValue, p.EstimatedMargin, p.FormulaStatus)).ToList()
            }).SingleOrDefaultAsync(ct);
    }
    public async Task<DraftInput?> LoadDraftAsync(int id, CancellationToken ct = default)
    {
        var user = await OwnerAsync(ct);
        var project = await db.Projects.AsNoTracking().Include(x => x.Products)
            .SingleOrDefaultAsync(x => x.Id == id && x.ProjectManagerId == user.Id && x.Status == ProjectStatus.Draft, ct);
        if (project is null) return null;
        return new DraftInput {
            ProjectId = project.Id, OriginalUpdatedAtUtc = project.UpdatedAtUtc,
            Brief = new BriefInput { Customer = project.Customer, CountryId = project.CountryId },
            Products = project.Products.OrderBy(x => x.Id).Select(x => new ProductInput {
                PersistedId = x.Id, ProductTypeId = x.ProductTypeId, SKU = x.SKU, Quantity = x.Quantity,
                EstimatedValue = x.EstimatedValue, EstimatedMargin = x.EstimatedMargin, FormulaStatus = x.FormulaStatus
            }).ToList()
        };
    }
    public static void Validate(DraftInput input)
    {
        input.Brief.Customer = (input.Brief.Customer ?? "").Trim();
        Validator.ValidateObject(input.Brief, new ValidationContext(input.Brief), true);
        if (input.Products.Count == 0) throw new ValidationException("Add at least one product.");
        if (input.Products.Count > 500) throw new ValidationException("A draft supports up to 500 products.");
        foreach (var product in input.Products)
        {
            product.SKU = (product.SKU ?? "").Trim();
            Validator.ValidateObject(product, new ValidationContext(product), true);
        }
        if (input.Products.Select(p => p.Key).Distinct().Count() != input.Products.Count)
            throw new ValidationException("Duplicate product keys.");
    }
    public async Task<int> SaveDraftAsync(DraftInput input, CancellationToken ct = default)
    {
        var user = await OwnerAsync(ct);
        Validate(input);
        if (!await db.Countries.AnyAsync(x => x.Id == input.Brief.CountryId && x.IsActive, ct))
            throw new ValidationException("Select an active country.");
        var types = input.Products.Select(p => p.ProductTypeId!.Value).Distinct().ToArray();
        if (await db.ProductTypes.CountAsync(x => types.Contains(x.Id) && x.IsActive, ct) != types.Length)
            throw new ValidationException("One or more product types are no longer active.");

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
            db.ProjectProducts.RemoveRange(project.Products.Where(x => !submittedIds.Contains(x.Id)));
        }
        else
        {
            if (input.Products.Any(x => x.PersistedId.HasValue)) throw new ValidationException("Invalid product reference.");
            project = new Project { ProjectNumber = await numbers.GenerateAsync(ct), Customer = input.Brief.Customer,
                ProjectManagerId = user.Id, CreatedAtUtc = now, UpdatedAtUtc = now, Status = ProjectStatus.Draft };
            db.Projects.Add(project);
        }
        project.Customer = input.Brief.Customer;
        project.CountryId = input.Brief.CountryId!.Value;
        project.UpdatedAtUtc = now;
        foreach (var item in input.Products)
        {
            var product = item.PersistedId is int productId
                ? project.Products.Single(x => x.Id == productId)
                : new ProjectProduct { SKU = item.SKU, CreatedAtUtc = now, ReviewStatus = ProductReviewStatus.Pending };
            if (item.PersistedId is null) project.Products.Add(product);
            product.ProductTypeId = item.ProductTypeId!.Value;
            product.SKU = item.SKU;
            product.Quantity = item.Quantity!.Value;
            product.EstimatedValue = item.EstimatedValue!.Value;
            product.EstimatedMargin = item.EstimatedMargin!.Value;
            product.FormulaStatus = item.FormulaStatus!.Value;
            product.UpdatedAtUtc = now;
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return project.Id;
    }
}

