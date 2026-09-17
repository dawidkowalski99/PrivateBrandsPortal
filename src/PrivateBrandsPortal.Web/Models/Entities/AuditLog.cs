using PrivateBrandsPortal.Web.Models.Enums;

namespace PrivateBrandsPortal.Web.Models.Entities;

public sealed class AuditLog
{
    public long Id { get; set; }
    public required string EntityType { get; set; }
    public int EntityId { get; set; }
    public required string FieldName { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public int ChangedByUserId { get; set; }
    public AppUser ChangedByUser { get; set; } = null!;
    public DateTimeOffset ChangedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public AuditChangeType ChangeType { get; set; }
}

