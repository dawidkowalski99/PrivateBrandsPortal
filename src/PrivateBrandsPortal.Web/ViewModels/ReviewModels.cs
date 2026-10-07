using System.ComponentModel.DataAnnotations;
using PrivateBrandsPortal.Web.Models.Enums;
namespace PrivateBrandsPortal.Web.ViewModels;

public sealed record ApprovalItem(int Id, string ProjectNumber, string Customer, string Country,
    string ProjectManager, DateTimeOffset? SubmittedAtUtc, int Products, int Reviewed, ProjectStatus Status);
public sealed record ReviewHistoryItem(ReviewDecision Decision, string? Comment, string Reviewer, DateTimeOffset ReviewedAtUtc) { public string? RejectionReason { get; init; } }
public sealed record ManagerChange(string FieldName, string? OldValue, string? NewValue, string ChangedBy, DateTimeOffset ChangedAtUtc)
{
    public string Label => FieldName switch { "EstimatedMargin" => "Estimated Margin", "EstimatedValue" => "Estimated Value", "ProductTypeId" => "Product Type", "ProductCategoryId" => "Product Category", "FormulaStatus" => "Formula", _ => FieldName };
    public string? OldProductTypeName { get; init; }
    public string? NewProductTypeName { get; init; }
    public string OldDisplay => Format(OldValue, OldProductTypeName);
    public string NewDisplay => Format(NewValue, NewProductTypeName);
    private string Format(string? value, string? productTypeName)
    {
        if (FieldName is "ProductTypeId" or "ProductCategoryId") return productTypeName ?? $"Product type #{value}";
        if (FieldName == "FormulaStatus") return value switch { "ReadyToGo" => "Ready to go", "NewFormula" => "New formula", _ => value ?? "—" };
        if (FieldName is "Quantity" or "EstimatedValue" or "EstimatedMargin" && decimal.TryParse(value,
            System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var number))
            return FieldName switch { "Quantity" => number.ToString("N0"), "EstimatedValue" => number.ToString("N2") + " PLN", _ => number.ToString("N2") + "%" };
        if (FieldName == "CommercialStatus") return string.IsNullOrEmpty(value) ? "Awaiting PM update" : Enum.TryParse<CommercialStatus>(value, out var status) ? EnumLabel.Text(status) : value; return value ?? "—";
    }
}
public sealed class ReviewInput : IValidatableObject
{
    [Range(1, int.MaxValue)] public int ProjectId { get; set; }
    [Range(1, int.MaxValue)] public int ProductId { get; set; }
    [Required] public DateTimeOffset? ProjectVersion { get; set; }
    [Required] public DateTimeOffset? ProductVersion { get; set; }
    [Required, EnumDataType(typeof(ReviewDecision))] public ReviewDecision? Decision { get; set; }
    [StringLength(2000)] public string? Comment { get; set; }
    [Display(Name = "Rejection Reason")] public int? RejectionReasonId { get; set; }
    public ProductInput? Product { get; set; }
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (Decision == ReviewDecision.Rejected && (!RejectionReasonId.HasValue || RejectionReasonId <= 0))
            yield return new("A rejection reason is required.", [nameof(RejectionReasonId)]);
        if (Decision == ReviewDecision.EditedAndApproved && Product is null)
            yield return new("Product data is required.", [nameof(Product)]);
    }
}
public sealed class ReviewFormModel
{
    public IReadOnlyList<PrivateBrandsPortal.Web.Services.SubcategoryOption> Subcategories { get; set; } = [];
    public ProjectDetailsViewModel Project { get; set; } = new();
    public ReviewInput Input { get; set; } = new();
    public IReadOnlyList<LookupItem> FormulaOptions {get;set;} = [];
    public IReadOnlyList<LookupItem> ProductTypes { get; set; } = [];
    public IReadOnlyList<ReasonItem> RejectionReasons { get; set; } = [];
}
public sealed record ReasonItem(int Id, string Name, bool RequiresComment);
