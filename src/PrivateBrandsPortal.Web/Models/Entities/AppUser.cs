using PrivateBrandsPortal.Web.Models.Enums;

namespace PrivateBrandsPortal.Web.Models.Entities;

public sealed class AppUser
{
    public int Id { get; set; }
    public required string DomainLogin { get; set; }
    public required string DisplayName { get; set; }
    public string? Email { get; set; }
    public string? Department { get; set; }
    public AppRole Role { get; set; } = AppRole.ProjectManager;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<AppUserPermission> Permissions { get; set; } = new List<AppUserPermission>();
    public ICollection<Project> Projects { get; set; } = new List<Project>();
    public ICollection<ProductReview> ProductReviews { get; set; } = new List<ProductReview>();
    public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
}
