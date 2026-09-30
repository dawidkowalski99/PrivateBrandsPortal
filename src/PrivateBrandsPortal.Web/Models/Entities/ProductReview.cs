using PrivateBrandsPortal.Web.Models.Enums;

namespace PrivateBrandsPortal.Web.Models.Entities;

public sealed class ProductReview
{
    public int Id { get; set; }
    public int ProjectProductId { get; set; }
    public ProjectProduct ProjectProduct { get; set; } = null!;
    public int ReviewerId { get; set; }
    public AppUser Reviewer { get; set; } = null!;
    public ReviewDecision Decision { get; set; }
    public string? Comment { get; set; }
    public int? RejectionReasonId { get; set; }
    public RejectionReason? RejectionReason { get; set; }
    public string? RejectionReasonName { get; set; }
    public DateTimeOffset ReviewedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
