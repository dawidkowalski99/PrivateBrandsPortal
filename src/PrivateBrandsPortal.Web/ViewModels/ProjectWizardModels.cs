using System.ComponentModel.DataAnnotations;
using PrivateBrandsPortal.Web.Models.Enums;
namespace PrivateBrandsPortal.Web.ViewModels;

public sealed class BriefInput
{
    [Range(1, int.MaxValue), Display(Name = "Customer")] public int? CustomerId { get; set; }
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever, Microsoft.AspNetCore.Mvc.ModelBinding.Validation.ValidateNever, StringLength(200)]
    public string Customer { get; set; } = "";
    [Required, Range(1, int.MaxValue), Display(Name = "Country")]
    public int? CountryId { get; set; }
}
public sealed class ProductInput : IValidatableObject
{
    [Range(1, int.MaxValue), Display(Name = "Product Subcategory")] public int? ProductSubcategoryId { get; set; }
    public Guid Key { get; set; } = Guid.NewGuid();
    public int? PersistedId { get; set; }
    [Range(1, int.MaxValue), Display(Name = "Legacy Product Type")]
    public int? ProductTypeId { get; set; }
    [Required, Range(1, int.MaxValue), Display(Name = "Product Category")]
    public int? ProductCategoryId { get; set; }
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever, Microsoft.AspNetCore.Mvc.ModelBinding.Validation.ValidateNever, StringLength(100), Display(Name = "Subcategory")]
    public string Subcategory { get; set; } = "";
    [Required, StringLength(100)]
    public string SKU { get; set; } = "";
    [Required, Range(1, int.MaxValue)]
    public int? Quantity { get; set; }
    [Required, Range(typeof(decimal), "0", "9999999999999999.99", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true), Display(Name = "Estimated Value")]
    public decimal? EstimatedValue { get; set; }
    [Required, Range(typeof(decimal), "0", "100", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true), Display(Name = "Estimated Margin %")]
    public decimal? EstimatedMargin { get; set; }
    [Range(1,int.MaxValue), Display(Name = "Formula")] public int? FormulaOptionId { get; set; }
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever, Microsoft.AspNetCore.Mvc.ModelBinding.Validation.ValidateNever]
    public string? FormulaName { get; set; }
    [EnumDataType(typeof(FormulaStatus)), Display(Name = "Legacy formula")]
    public FormulaStatus? FormulaStatus { get; set; }
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (EstimatedValue.HasValue && decimal.Round(EstimatedValue.Value, 2) != EstimatedValue)
            yield return new("Use at most two decimal places.", [nameof(EstimatedValue)]);
        if (EstimatedMargin.HasValue && decimal.Round(EstimatedMargin.Value, 2) != EstimatedMargin)
            yield return new("Use at most two decimal places.", [nameof(EstimatedMargin)]);
    }
}
public sealed class DraftInput
{
    public Guid WizardToken { get; set; }
    public List<TemporaryBrief> TemporaryBriefs { get; set; } = [];
    public int? ProjectId { get; set; }
    public DateTimeOffset? OriginalUpdatedAtUtc { get; set; }
    public BriefInput Brief { get; set; } = new();
    public List<ProductInput> Products { get; set; } = [];
}
public sealed record LookupItem(int Id, string Name);
public sealed class WizardViewModel
{
    public IReadOnlyList<PrivateBrandsPortal.Web.Services.CustomerOption> Customers { get; set; } = [];
    public IReadOnlyList<PrivateBrandsPortal.Web.Services.SubcategoryOption> Subcategories { get; set; } = [];
    public ProductCardViewModel Card(ProductInput product) => new(
        product.Subcategory,
        product.SKU, product.Quantity!.Value, product.EstimatedValue!.Value,
        product.EstimatedMargin!.Value, product.FormulaStatus ?? FormulaStatus.ReadyToGo) { FormulaOptionId=product.FormulaOptionId, FormulaName=product.FormulaName, Category = ProductTypes.FirstOrDefault(x => x.Id == product.ProductCategoryId)?.Name };
    public Guid Token { get; set; }
    public int Revision { get; set; }
    public int Step { get; set; } = 1;
    public DraftInput Draft { get; set; } = new();
    public BriefInput Brief { get; set; } = new();
    public ProductInput Product { get; set; } = new();
    public bool ShowProductForm { get; set; }
    public string ProjectManager { get; set; } = "";
    public IReadOnlyList<LookupItem> FormulaOptions { get; set; } = [];
    public IReadOnlyList<LookupItem> Countries { get; set; } = [];
    public IReadOnlyList<LookupItem> ProductTypes { get; set; } = [];
}
public sealed class ProjectListItemViewModel
{
    public int Id { get; set; }
    public string ProjectNumber { get; set; } = "";
    public string Customer { get; set; } = "";
    public string Country { get; set; } = "";
    public ProjectStatus Status { get; set; }
    public int ProductCount { get; set; }
    public decimal EstimatedValue { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
public sealed class ProjectDetailsViewModel
{
    public string? CustomerRejectionReason {get;set;} public string? CustomerRejectionComment {get;set;} public string? CustomerRejectedBy {get;set;} public DateTimeOffset? CustomerRejectedAtUtc {get;set;}
    public bool RequiresManagerApproval {get;set;} = true;
    public int ProjectManagerId {get;set;}
    public List<ProjectTransferHistory> Transfers { get; set; } = [];
    public DateTimeOffset? SubmittedAtUtc { get; set; }
    public DateTimeOffset? ArchivedAtUtc { get; set; }
    public int Id { get; set; }
    public string ProjectNumber { get; set; } = "";
    public string Customer { get; set; } = "";
    public string Country { get; set; } = "";
    public string ProjectManager { get; set; } = "";
    public ProjectStatus Status { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public List<ProductCardViewModel> Products { get; set; } = [];
}
public sealed record ProductCardViewModel(string ProductType, string SKU, int Quantity,
    decimal EstimatedValue, decimal EstimatedMargin, FormulaStatus FormulaStatus)
{
    public string? CustomerRejectionReason {get;init;} public string? CustomerRejectionComment {get;init;} public string? CustomerRejectedBy {get;init;} public DateTimeOffset? CustomerRejectedAtUtc {get;init;}
    public List<ImplementationHistory> ImplementationHistory {get;init;} = [];
    public int? FormulaOptionId { get; init; }
    public string? FormulaName { get; init; }
    public int Id { get; init; }
    public int? ProductTypeId { get; init; }
    public int? ProductCategoryId { get; init; }
    public int? ProductSubcategoryId { get; init; }
    public string? Category { get; init; }
    public CommercialStatus? CommercialStatus { get; init; }
    public List<ManagerChange> CommercialHistory { get; init; } = [];
    public DateTimeOffset UpdatedAtUtc { get; init; }
    public ProductReviewStatus ReviewStatus { get; init; }
    public List<ReviewHistoryItem> Reviews { get; init; } = [];
    public List<ManagerChange> Changes { get; init; } = [];
}
