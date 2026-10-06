using System.ComponentModel.DataAnnotations;
namespace PrivateBrandsPortal.Web.ViewModels;
public enum DictionaryKind { ProductCategories=1, RejectionReasons=2, Countries=3, Customers=4, ProductSubcategories=5 }
public sealed class DictionaryInput : IValidatableObject
{
    public int Id {get;set;}
    [StringLength(50), Display(Name="Code")] public string? Code {get;set;}
    [Range(1,int.MaxValue), Display(Name="Product Category")] public int? ProductCategoryId {get;set;}
    public string? CategoryName {get;set;}
    public IReadOnlyList<LookupItem> Categories {get;set;} = [];
    public IEnumerable<ValidationResult> Validate(ValidationContext context) {
        if(Kind==DictionaryKind.Countries && (Code is null || !System.Text.RegularExpressions.Regex.IsMatch(Code, "^[A-Za-z]{2}$")))
            yield return new("Use a two-letter country code.", [nameof(Code)]);
        if(Kind==DictionaryKind.ProductSubcategories && ProductCategoryId is null)
            yield return new("Select a product category.", [nameof(ProductCategoryId)]);
        if(Kind!=DictionaryKind.Customers && Name.Length>100) yield return new("Use at most 100 characters.", [nameof(Name)]);
    }
    [EnumDataType(typeof(DictionaryKind))] public DictionaryKind Kind {get;set;}
    [Required,StringLength(200)] public string Name {get;set;}="";
    [StringLength(1000)] public string? Description {get;set;}
    [Display(Name="Active")] public bool IsActive {get;set;}=true;
    [Range(0,100000),Display(Name="Display Order")] public int DisplayOrder {get;set;}
    [Display(Name="Comment required")] public bool RequiresComment {get;set;}
    public DateTimeOffset? Version {get;set;}
}
public sealed record DictionaryPage(DictionaryKind Kind,IReadOnlyList<DictionaryInput> Items) {
    public string? Search {get;init;}
    public int? CategoryId {get;init;}
    public IReadOnlyList<LookupItem> Categories {get;init;} = [];
}
