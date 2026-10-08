using System.ComponentModel.DataAnnotations;
using PrivateBrandsPortal.Web.Models.Enums;
namespace PrivateBrandsPortal.Web.ViewModels;
public sealed class ImplementationDecisionInput
{
    [Range(1,int.MaxValue)]public int Id {get;set;}
    [Required]public DateTimeOffset? Version {get;set;}
    [Required,EnumDataType(typeof(ImplementationApprovalStatus))]public ImplementationApprovalStatus? Decision {get;set;}
    [StringLength(1000)]public string? Comment {get;set;}
}
public sealed record ImplementationHistory(int Id,ImplementationApprovalStatus Status,DateTimeOffset RequestedAtUtc,string RequestedBy,
    DateTimeOffset? ReviewedAtUtc,string? Reviewer,string? Comment);
public sealed class ImplementationReviewModel
{
    public required ProjectDetailsViewModel Project {get;init;}
    public required ProductCardViewModel Product {get;init;}
    public required ImplementationDecisionInput Input {get;set;}
    public IReadOnlyList<AttachmentItem> Documents {get;init;}=[];
    public bool CanDecide {get;init;}
}
public sealed record ImplementationQueueItem(int Id,int ProjectId,string ProjectNumber,string Customer,string Owner,
    string? Category,string? Subcategory,string SKU,DateTimeOffset RequestedAtUtc,bool Offer,bool Calculation);
public sealed class CustomerClosureInput
{
    [Range(1,int.MaxValue)]public int ProjectId {get;set;}
    [Range(1,int.MaxValue)]public int ProductId {get;set;}
    [Required]public DateTimeOffset? Version {get;set;}
    [Required,Range(1,int.MaxValue),Display(Name="Reason")]public int? ReasonId {get;set;}
    [StringLength(1000)]public string? Comment {get;set;}
    [Range(typeof(bool),"true","true",ErrorMessage="Confirm closing this product.")]public bool Confirm {get;set;}
}
public sealed class CustomerClosureModel
{
    public string ProjectNumber {get;set;}="";
    public string SKU {get;set;}="";
    public CustomerClosureInput Input {get;set;}=new();
    public IReadOnlyList<LookupItem> Reasons {get;set;}=[];
}

public sealed record CustomerClosureResult(string Reason,bool Archived);
