namespace PrivateBrandsPortal.Web.Models.Entities;
public sealed class RejectionReason
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
    // Stable business rule for Other, retained even when its display name changes.
    public bool RequiresComment { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
