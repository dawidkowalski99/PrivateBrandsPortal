using PrivateBrandsPortal.Web.Models.Enums;
namespace PrivateBrandsPortal.Web.Models.Entities;
public sealed class ImplementationApproval
{
    public int Id {get;set;}
    public int ProjectProductId {get;set;}
    public ProjectProduct ProjectProduct {get;set;} = null!;
    public ImplementationApprovalStatus Status {get;set;} = ImplementationApprovalStatus.Pending;
    public int RequestedByUserId {get;set;}
    public AppUser RequestedByUser {get;set;} = null!;
    public DateTimeOffset RequestedAtUtc {get;set;}
    public int? ReviewerId {get;set;}
    public AppUser? Reviewer {get;set;}
    public DateTimeOffset? ReviewedAtUtc {get;set;}
    public string? Comment {get;set;}
}
