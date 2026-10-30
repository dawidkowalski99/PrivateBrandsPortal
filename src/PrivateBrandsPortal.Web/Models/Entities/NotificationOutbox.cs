namespace PrivateBrandsPortal.Web.Models.Entities;

public enum NotificationStatus { Pending, Processing, Retry, Blocked, Sent, Simulated, Failed }
public enum NotificationAudience { ProjectManager, Approver, ImplementationApprover }

// Immutable event snapshots deliberately survive deletion of the referenced business data.
public sealed class NotificationOutbox
{
    public long Id { get; set; }
    public required string EventKey { get; set; }
    public required string EventType { get; set; }
    public int ProjectId { get; set; }
    public int? ProductId { get; set; }
    public int ActorUserId { get; set; }
    public int TargetUserId { get; set; }
    public NotificationAudience Audience { get; set; }
    public int? RecipientUserId { get; set; }
    public string? RecipientEmail { get; set; }
    public required string Subject { get; set; }
    public required string Body { get; set; }
    public required string RelativePath { get; set; }
    public NotificationStatus Status { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset NextAttemptAtUtc { get; set; }
    public DateTimeOffset? LastAttemptAtUtc { get; set; }
    public DateTimeOffset? SentAtUtc { get; set; }
    public DateTimeOffset? ProcessedAtUtc { get; set; }
    public string? LastError { get; set; }
}
