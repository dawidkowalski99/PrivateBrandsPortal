using System.ComponentModel.DataAnnotations;
using PrivateBrandsPortal.Web.Models.Enums;
namespace PrivateBrandsPortal.Web.ViewModels;

// This metadata lives only in the server-side wizard. It is never bound from a form.
public sealed record TemporaryBrief(string StorageKey, string FileName, string ContentType, long Size,
    string Sha256, int OwnerId, Guid WizardToken, DateTimeOffset UploadedAtUtc);
public sealed class AttachmentInput
{
    [Range(1, int.MaxValue)] public int ProjectId { get; set; }
    [Range(1, int.MaxValue), Display(Name="Product (optional)")] public int? ProjectProductId { get; set; }
    [Required, EnumDataType(typeof(AttachmentType)), Display(Name="Attachment type")] public AttachmentType? Type { get; set; }
    [Required] public IFormFile? File { get; set; }
    [StringLength(1000)] public string? Description { get; set; }
}
public sealed record AttachmentItem(int Id, AttachmentType Type, string FileName, long Size,
    string UploadedBy, DateTimeOffset UploadedAtUtc, string? SKU, string? Description);
public sealed record AttachmentPanel(int ProjectId, bool CanUpload, IReadOnlyList<AttachmentItem> Items,
    IReadOnlyList<AttachmentRequirement> Requirements);
public sealed record AttachmentRequirement(int ProductId, string SKU, bool Offer, bool Calculation);
public sealed record AttachmentUploadModel(AttachmentInput Input, IReadOnlyList<LookupItem> Products);
