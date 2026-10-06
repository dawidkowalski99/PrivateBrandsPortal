using PrivateBrandsPortal.Web.Models.Enums;

namespace PrivateBrandsPortal.Web.Models.Entities;

public sealed class ProjectProduct
{
    public int? ProductSubcategoryId { get; set; }
    public ProductSubcategory? ProductSubcategory { get; set; }
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    public int? ProductTypeId { get; set; }
    public ProductType? ProductType { get; set; }
    public int? ProductCategoryId { get; set; }
    public ProductCategory? ProductCategory { get; set; }
    public string? Subcategory { get; set; }
    public CommercialStatus? CommercialStatus { get; set; }
    public required string SKU { get; set; }
    public int Quantity { get; set; }
    public decimal EstimatedValue { get; set; }
    public decimal EstimatedMargin { get; set; }
    public FormulaStatus FormulaStatus { get; set; } = FormulaStatus.ReadyToGo;
    public ProductReviewStatus ReviewStatus { get; set; } = ProductReviewStatus.Pending;
    public string? ReviewComment { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<ProductReview> Reviews { get; set; } = new List<ProductReview>();
}
