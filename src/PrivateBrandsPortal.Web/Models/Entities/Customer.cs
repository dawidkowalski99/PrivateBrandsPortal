namespace PrivateBrandsPortal.Web.Models.Entities;
public sealed class Customer
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Code { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
