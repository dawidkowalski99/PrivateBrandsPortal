using System.ComponentModel.DataAnnotations;
using PrivateBrandsPortal.Web.Models.Enums;
namespace PrivateBrandsPortal.Web.ViewModels;
public sealed class CommercialInput
{
    [Range(1,int.MaxValue)] public int ProjectId { get; set; }
    [Range(1,int.MaxValue)] public int ProductId { get; set; }
    [Required] public DateTimeOffset? Version { get; set; }
    [Required, EnumDataType(typeof(CommercialStatus)), Display(Name="Commercial Status")]
    public CommercialStatus? Status { get; set; }
}
public sealed class CommercialForm
{
    public string ProjectNumber { get; set; } = "";
    public string SKU { get; set; } = "";
    public CommercialInput Input { get; set; } = new();
}
public sealed record ArchiveItem(int Id, string ProjectNumber, string Customer, string Country,
    string ProjectManager, int Products, int Completed, DateTimeOffset? ArchivedAtUtc,string? CustomerRejectionReason=null);
