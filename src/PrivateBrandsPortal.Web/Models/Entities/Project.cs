using PrivateBrandsPortal.Web.Models.Enums;

namespace PrivateBrandsPortal.Web.Models.Entities;

public sealed class Project
{
    public int? CustomerRejectionReasonId {get;set;}
    public CustomerRejectionReason? CustomerRejectionReason {get;set;}
    public string? CustomerRejectionReasonName {get;set;}
    public string? CustomerRejectionComment {get;set;}
    public DateTimeOffset? CustomerRejectedAtUtc {get;set;}
    public int? CustomerRejectedByUserId {get;set;}
    public AppUser? CustomerRejectedByUser {get;set;}
    public int? CustomerId { get; set; }
    public Customer? CustomerEntry { get; set; }
    public int Id { get; set; }
    public int? CreatedByUserId { get; set; }
    public AppUser? CreatedByUser { get; set; }
    public bool RequiresManagerApproval { get; set; } = true;
    public required string ProjectNumber { get; set; }
    public required string Customer { get; set; }
    public int CountryId { get; set; }
    public Country Country { get; set; } = null!;
    public int ProjectManagerId { get; set; }
    public AppUser ProjectManager { get; set; } = null!;
    public ProjectStatus Status { get; set; } = ProjectStatus.Draft;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SubmittedAtUtc { get; set; }
    public DateTimeOffset? ArchivedAtUtc { get; set; }
    public ICollection<ProjectProduct> Products { get; set; } = new List<ProjectProduct>();
}
