using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PrivateBrandsPortal.Web.Data;
using PrivateBrandsPortal.Web.Interfaces;
using PrivateBrandsPortal.Web.Models.Entities;
using PrivateBrandsPortal.Web.Models.Enums;
using PrivateBrandsPortal.Web.ViewModels;
namespace PrivateBrandsPortal.Web.Services;

public sealed class ApprovalService(ApplicationDbContext db, IAppUserService users, TimeProvider clock, DemoAccess demo) : IApprovalService
{
    public static ProjectStatus CalculateStatus(IEnumerable<ProductReviewStatus> statuses)
    {
        var values = statuses.ToArray();
        if (values.Length == 0 || values.All(x => x == ProductReviewStatus.Pending)) return ProjectStatus.AwaitingManagerReview;
        if (values.Any(x => x == ProductReviewStatus.Pending)) return ProjectStatus.PartiallyReviewed;
        if (values.All(x => x == ProductReviewStatus.Rejected)) return ProjectStatus.Rejected;
        if (values.All(x => x is ProductReviewStatus.Approved or ProductReviewStatus.EditedAndApproved)) return ProjectStatus.Approved;
        return ProjectStatus.PartiallyApproved;
    }
    private async Task<AppUser> RequireAsync(AppRole role, CancellationToken ct)
    {
        var user = await users.GetCurrentAsync(ct);
        if (!user.IsActive || (user.Role != role && !(role == AppRole.Manager && demo.AllowsManagerReview(user)))) throw new PortalAccessException();
        return user;
    }
    private IQueryable<Project> Waiting() => db.Projects.Where(x =>
        (x.Status == ProjectStatus.AwaitingManagerReview || x.Status == ProjectStatus.PartiallyReviewed)
        && x.Products.Any(p => p.ReviewStatus == ProductReviewStatus.Pending));
    public async Task<int> CountAsync(CancellationToken ct = default)
    {
        var user = await users.GetCurrentAsync(ct);
        return user.IsActive && (user.Role == AppRole.Manager || demo.AllowsManagerReview(user)) ? await Waiting().CountAsync(ct) : 0;
    }
    public async Task<IReadOnlyList<ApprovalItem>> QueueAsync(CancellationToken ct = default)
    {
        await RequireAsync(AppRole.Manager, ct);
        var rows = await Waiting().AsNoTracking().OrderBy(x => x.SubmittedAtUtc).ThenBy(x => x.Id)
            .Select(x => new { x.Id, x.ProjectNumber, x.Customer, Country = x.Country.Name,
                x.ProjectManager.DisplayName, x.ProjectManager.DomainLogin, x.SubmittedAtUtc,
                Products = x.Products.Count, Reviewed = x.Products.Count(p => p.ReviewStatus != ProductReviewStatus.Pending), x.Status }).ToListAsync(ct);
        return rows.Select(x => new ApprovalItem(x.Id, x.ProjectNumber, x.Customer, x.Country,
            string.IsNullOrWhiteSpace(x.DisplayName) ? x.DomainLogin : x.DisplayName,
            x.SubmittedAtUtc, x.Products, x.Reviewed, x.Status)).ToList();
    }
    public async Task<ProjectDetailsViewModel?> ReviewAsync(int id, CancellationToken ct = default)
    {
        await RequireAsync(AppRole.Manager, ct);
        return await ProjectDetailsReader.ReadAsync(db, db.Projects.Where(x => x.Id == id &&
            (x.Status == ProjectStatus.AwaitingManagerReview || x.Status == ProjectStatus.PartiallyReviewed ||
             x.Status == ProjectStatus.Approved || x.Status == ProjectStatus.Rejected || x.Status == ProjectStatus.PartiallyApproved)), ct);
    }
    public async Task<ReviewFormModel?> FormAsync(int projectId, int productId, ReviewDecision decision, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(decision)) return null;
        var project = await ReviewAsync(projectId, ct);
        if (project is null || project.Status is not (ProjectStatus.AwaitingManagerReview or ProjectStatus.PartiallyReviewed)) return null;
        var product = project.Products.SingleOrDefault(x => x.Id == productId && x.ReviewStatus == ProductReviewStatus.Pending);
        if (product is null) return null;
        return new ReviewFormModel { Project = project,
            RejectionReasons = await db.RejectionReasons.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name).Select(x => new ReasonItem(x.Id,x.Name,x.RequiresComment)).ToListAsync(ct), ProductTypes = await db.ProductCategories.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name).Select(x => new LookupItem(x.Id, x.Name)).ToListAsync(ct),
            Input = new ReviewInput { ProjectId = projectId, ProductId = productId, ProjectVersion = project.UpdatedAtUtc,
                ProductVersion = product.UpdatedAtUtc, Decision = decision,
                Product = decision == ReviewDecision.EditedAndApproved ? new ProductInput {
                    ProductCategoryId = product.ProductCategoryId, Subcategory = product.ProductType, SKU = product.SKU, Quantity = product.Quantity,
                    EstimatedValue = product.EstimatedValue, EstimatedMargin = product.EstimatedMargin, FormulaStatus = product.FormulaStatus } : null }
        };
    }
    public async Task SubmitAsync(int id, DateTimeOffset version, CancellationToken ct = default)
    {
        var user = await RequireAsync(AppRole.ProjectManager, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = Later(version);
        var changed = await db.Projects.Where(x => x.Id == id && x.ProjectManagerId == user.Id && x.Status == ProjectStatus.Draft && x.UpdatedAtUtc == version)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.UpdatedAtUtc, now), ct);
        if (changed != 1) throw new ValidationException("This draft changed or is unavailable. Reopen it before submitting.");
        var project = await db.Projects.Include(x => x.Products).SingleAsync(x => x.Id == id, ct);
        await db.Entry(project).ReloadAsync(ct); // Also safe when called in the same scope as SaveDraft.
        var input = new DraftInput { Brief = new BriefInput { Customer = project.Customer, CountryId = project.CountryId },
            Products = project.Products.Select(p => new ProductInput { ProductCategoryId = p.ProductCategoryId, Subcategory = p.Subcategory ?? "", SKU = p.SKU,
                Quantity = p.Quantity, EstimatedValue = p.EstimatedValue, EstimatedMargin = p.EstimatedMargin, FormulaStatus = p.FormulaStatus }).ToList() };
        // Historical products may have no category; they are loaded from SQL, never supplied by the request.

        ProjectService.Validate(input, allowLegacyCategories: true);
        if (!await db.Countries.AnyAsync(x => x.Id == project.CountryId && x.IsActive, ct)) throw new ValidationException("Select an active country.");
        var types = project.Products.Where(x => x.ProductCategoryId.HasValue).Select(x => x.ProductCategoryId!.Value).Distinct().ToArray();
        if (await db.ProductCategories.CountAsync(x => types.Contains(x.Id) && x.IsActive, ct) != types.Length) throw new ValidationException("All product categories must be active.");
        if (project.Products.Any(x => x.ReviewStatus != ProductReviewStatus.Pending) || await db.ProductReviews.AnyAsync(x => x.ProjectProduct.ProjectId == id, ct))
            throw new ValidationException("This project already has review decisions.");
        project.Status = ProjectStatus.AwaitingManagerReview;
        project.SubmittedAtUtc = now;
        project.UpdatedAtUtc = now;
        db.AuditLogs.Add(new AuditLog { EntityType = nameof(Project), EntityId = id, FieldName = nameof(Project.Status),
            OldValue = ProjectStatus.Draft.ToString(), NewValue = project.Status.ToString(), ChangedByUserId = user.Id,
            ChangedAtUtc = now, ChangeType = AuditChangeType.ProjectSubmitted });
        await db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);
    }
    private DateTimeOffset Later(DateTimeOffset version) { var now = clock.GetUtcNow(); return now > version ? now : version.AddTicks(1); }
    public async Task DecideAsync(ReviewInput input, CancellationToken ct = default)
    {
        var user = await RequireAsync(AppRole.Manager, ct);
        input.Comment = input.Comment?.Trim();
        Validator.ValidateObject(input, new ValidationContext(input), true);
        RejectionReason? rejection = null;
        if (input.Decision == ReviewDecision.Rejected)
        {
            rejection = await db.RejectionReasons.AsNoTracking().SingleOrDefaultAsync(x => x.Id == input.RejectionReasonId && x.IsActive, ct);
            if (rejection is null) throw new ValidationException("Select an active rejection reason.");
            if (rejection.RequiresComment && string.IsNullOrWhiteSpace(input.Comment)) throw new ValidationException("A comment is required for this rejection reason (Other).");
        }
        if (input.Decision == ReviewDecision.EditedAndApproved)
        {
            input.Product!.SKU = (input.Product.SKU ?? "").Trim(); input.Product.Subcategory = (input.Product.Subcategory ?? "").Trim();
            Validator.ValidateObject(input.Product, new ValidationContext(input.Product), true);
            if (!await db.ProductCategories.AnyAsync(x => x.Id == input.Product.ProductCategoryId && x.IsActive, ct)) throw new ValidationException("Select an active product category.");
        }
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = Later(input.ProjectVersion!.Value);
        // Lock the parent first: concurrent decisions on different products cannot lose the aggregate status.
        var changed = await db.Projects.Where(x => x.Id == input.ProjectId && x.UpdatedAtUtc == input.ProjectVersion &&
            (x.Status == ProjectStatus.AwaitingManagerReview || x.Status == ProjectStatus.PartiallyReviewed))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.UpdatedAtUtc, now), ct);
        if (changed != 1) throw new ValidationException("The project changed or review is closed. Reopen review to continue.");
        var product = await db.ProjectProducts.SingleOrDefaultAsync(x => x.Id == input.ProductId && x.ProjectId == input.ProjectId, ct);
        if (product is null) throw new ValidationException("Product unavailable in this project.");
        await db.Entry(product).ReloadAsync(ct);
        if (product.ReviewStatus != ProductReviewStatus.Pending || product.UpdatedAtUtc != input.ProductVersion ||
            await db.ProductReviews.AnyAsync(x => x.ProjectProductId == product.Id, ct))
            throw new ValidationException("This product changed or has already been reviewed. Reopen review.");
        if (input.Decision == ReviewDecision.EditedAndApproved)
        {
            var p = input.Product!;
            void Audit(string field, object? oldValue, object? newValue)
            {
                var oldText = Convert.ToString(oldValue, CultureInfo.InvariantCulture);
                var newText = Convert.ToString(newValue, CultureInfo.InvariantCulture);
                if (oldText == newText) return;
                db.AuditLogs.Add(new AuditLog { EntityType = nameof(ProjectProduct), EntityId = product.Id, FieldName = field,
                    OldValue = oldText, NewValue = newText, ChangedByUserId = user.Id, ChangedAtUtc = now, ChangeType = AuditChangeType.ManagerEdit });
            }
            Audit(nameof(product.ProductCategoryId), product.ProductCategoryId, p.ProductCategoryId); Audit(nameof(product.Subcategory), product.Subcategory, p.Subcategory);
            Audit(nameof(product.SKU), product.SKU, p.SKU);
            Audit(nameof(product.Quantity), product.Quantity, p.Quantity!.Value);
            // Decimal equality ignores scale: unchanged 25 and 25.00 produce no history.
            if (product.EstimatedValue != p.EstimatedValue) Audit(nameof(product.EstimatedValue), product.EstimatedValue, p.EstimatedValue!.Value);
            if (product.EstimatedMargin != p.EstimatedMargin) Audit(nameof(product.EstimatedMargin), product.EstimatedMargin, p.EstimatedMargin!.Value);
            Audit(nameof(product.FormulaStatus), product.FormulaStatus, p.FormulaStatus!.Value);
            product.ProductCategoryId = p.ProductCategoryId; product.Subcategory = p.Subcategory; product.SKU = p.SKU; product.Quantity = p.Quantity!.Value;
            product.EstimatedValue = p.EstimatedValue!.Value; product.EstimatedMargin = p.EstimatedMargin!.Value; product.FormulaStatus = p.FormulaStatus!.Value;
        }
        product.ReviewStatus = input.Decision switch { ReviewDecision.Approved => ProductReviewStatus.Approved,
            ReviewDecision.Rejected => ProductReviewStatus.Rejected, _ => ProductReviewStatus.EditedAndApproved };
        product.ReviewComment = input.Comment;
        product.UpdatedAtUtc = now;
        db.ProductReviews.Add(new ProductReview { ProjectProductId = product.Id, ReviewerId = user.Id,
            Decision = input.Decision!.Value, Comment = input.Comment, RejectionReasonId = rejection?.Id, RejectionReasonName = rejection?.Name, ReviewedAtUtc = now });
        await db.SaveChangesAsync(ct);
        var statuses = await db.ProjectProducts.AsNoTracking().Where(x => x.ProjectId == input.ProjectId).Select(x => x.ReviewStatus).ToListAsync(ct);
        await db.Projects.Where(x => x.Id == input.ProjectId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, CalculateStatus(statuses)), ct);
        await CommercialService.ArchiveIfCompleteAsync(db, input.ProjectId, now, ct);
        await tx.CommitAsync(ct);
    }
}
