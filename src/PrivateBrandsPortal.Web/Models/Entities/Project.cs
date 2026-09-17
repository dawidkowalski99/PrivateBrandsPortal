using PrivateBrandsPortal.Web.Models.Enums;

namespace PrivateBrandsPortal.Web.Models.Entities;

public sealed class Project
{
    public int Id { get; set; }
    public required string ProjectNumber { get; set; }
    public required string Customer { get; set; }
    public int CountryId { get; set; }
    public Country Country { get; set; } = null!;
    public int ProjectManagerId { get; set; }
    public AppUser ProjectManager { get; set; } = null!;
    public ProjectStatus Status { get; set; } = ProjectStatus.Draft;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SubmittedAtUtc { get; set; }
    public ICollection<ProjectProduct> Products { get; set; } = new List<ProjectProduct>();
}

