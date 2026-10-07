using PrivateBrandsPortal.Web.Models.Enums;
namespace PrivateBrandsPortal.Web.Models.Entities;
public sealed class ProjectAttachment
{
    public int Id {get;set;}
    public int ProjectId {get;set;}
    public Project Project {get;set;}=null!;
    public int? ProjectProductId {get;set;}
    public ProjectProduct? ProjectProduct {get;set;}
    public AttachmentType AttachmentType {get;set;}
    public required string OriginalFileName {get;set;}
    public required string StorageKey {get;set;}
    public required string ContentType {get;set;}
    public long FileSize {get;set;}
    public string? Sha256 {get;set;}
    public int UploadedByUserId {get;set;}
    public AppUser UploadedByUser {get;set;}=null!;
    public DateTimeOffset UploadedAtUtc {get;set;}
    public string? Description {get;set;}
    public DateTimeOffset? DeletedAtUtc {get;set;}
    public int? DeletedByUserId {get;set;}
    public AppUser? DeletedByUser {get;set;}
}
