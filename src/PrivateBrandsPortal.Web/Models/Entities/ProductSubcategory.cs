namespace PrivateBrandsPortal.Web.Models.Entities;
public sealed class ProductSubcategory
{
    public int Id { get; set; }
    public int ProductCategoryId { get; set; }
    public ProductCategory ProductCategory { get; set; } = null!;
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
