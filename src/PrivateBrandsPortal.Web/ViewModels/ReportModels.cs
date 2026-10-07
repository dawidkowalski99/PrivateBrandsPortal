using System.ComponentModel.DataAnnotations;
using PrivateBrandsPortal.Web.Models.Enums;
namespace PrivateBrandsPortal.Web.ViewModels;
public enum ArchiveFilter { All,Active,Archived }
public sealed class ReportFilter:IValidatableObject
{
    [Range(1,int.MaxValue),Display(Name="Project Manager")]public int? ProjectManagerId{get;set;}
    [DataType(DataType.Date),Display(Name="Date From (UTC)")]public DateOnly? DateFrom{get;set;}
    [DataType(DataType.Date),Display(Name="Date To (UTC)")]public DateOnly? DateTo{get;set;}
    [Range(1,int.MaxValue),Display(Name="Country")]public int? CountryId{get;set;}
    [EnumDataType(typeof(ProjectStatus)),Display(Name="Project Status")]public ProjectStatus? ProjectStatus{get;set;}
    [EnumDataType(typeof(ProductReviewStatus)),Display(Name="Manager Decision")]public ProductReviewStatus? ReviewStatus{get;set;}
    [EnumDataType(typeof(CommercialStatus)),Display(Name="Commercial Status")]public CommercialStatus? CommercialStatus{get;set;}
    [Range(1,int.MaxValue),Display(Name="Product Category")]public int? ProductCategoryId{get;set;}
    [StringLength(100)]public string? Subcategory{get;set;}
    [StringLength(200)]public string? Customer{get;set;}
    [EnumDataType(typeof(ArchiveFilter))]public ArchiveFilter Archived{get;set;}
    [Range(1,int.MaxValue)]public int Page{get;set;}=1;
    public IEnumerable<ValidationResult> Validate(ValidationContext context){if(DateFrom>DateTo)yield return new("Date From must not exceed Date To.",[nameof(DateTo)]);if(DateTo==DateOnly.MaxValue)yield return new("Select an earlier end date.",[nameof(DateTo)]);}
    public Dictionary<string,string> Routes(int? page=null){var r=new Dictionary<string,string>();foreach(var p in GetType().GetProperties()){var v=p.GetValue(this);if(v is not null)r[p.Name]=v is DateOnly d?d.ToString("yyyy-MM-dd"):Convert.ToString(v,System.Globalization.CultureInfo.InvariantCulture)!;}r[nameof(Page)]=(page??Page).ToString();return r;}
}
public sealed class ReportTotals
{
    public int Projects{get;set;} public int SKUs{get;set;} public int Approved{get;set;} public int EditedApproved{get;set;} public int Rejected{get;set;} public int Delivered{get;set;}
}
public sealed class ManagerReport
{
    public int Id{get;set;} public string Name{get;set;}="";public ReportTotals Totals{get;set;}=new();
}
public sealed class ReportRow
{
    public ImplementationApprovalStatus? ImplementationStatus {get;set;} public string? CustomerRejectionReason {get;set;}
    public string Formula{get;set;}="";
    public string ProjectNumber{get;set;}="";public string ProjectManager{get;set;}="";public string Customer{get;set;}="";public string Country{get;set;}="";
    public ProjectStatus ProjectStatus{get;set;}public string? Category{get;set;}public string Subcategory{get;set;}="";public string SKU{get;set;}="";
    public int Quantity{get;set;}public decimal EstimatedValue{get;set;}public decimal EstimatedMargin{get;set;}public ProductReviewStatus ReviewStatus{get;set;}
    public string? RejectionReason{get;set;}public CommercialStatus? CommercialStatus{get;set;}public DateTimeOffset CreatedAtUtc{get;set;}public DateTimeOffset? ArchivedAtUtc{get;set;}
}
public sealed class ReportPage
{
    public ReportFilter Filter{get;set;}=new();public ReportTotals Totals{get;set;}=new();public List<ManagerReport> Managers{get;set;}=[];public List<ReportRow> Rows{get;set;}=[];
    public IReadOnlyList<LookupItem> Users{get;set;}=[];public IReadOnlyList<LookupItem> Countries{get;set;}=[];public IReadOnlyList<LookupItem> Categories{get;set;}=[];
    public int Pages=>Math.Max(1,(int)Math.Ceiling(Totals.SKUs/50m));
}
