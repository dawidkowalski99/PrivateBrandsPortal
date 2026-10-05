using System.ComponentModel.DataAnnotations;

namespace PrivateBrandsPortal.Web.ViewModels;

public sealed class ProjectTransferInput
{
    [Range(1, int.MaxValue)] public int ProjectId { get; set; }
    [Required] public DateTimeOffset? Version { get; set; }
    [Required, Range(1, int.MaxValue), Display(Name = "New Project Manager")]
    public int? NewProjectManagerId { get; set; }
    [Required, StringLength(1000)] public string Reason { get; set; } = "";
}
public sealed class ProjectTransferPage
{
    public required string ProjectNumber { get; init; }
    public required string CurrentProjectManager { get; init; }
    public bool CanOpenProject { get; init; }
    public required ProjectTransferInput Input { get; set; }
    public IReadOnlyList<LookupItem> ProjectManagers { get; init; } = [];
}
public sealed record ProjectTransferHistory(string? OldManager, string? NewManager,
    string ChangedBy, DateTimeOffset ChangedAtUtc, string? Reason);
