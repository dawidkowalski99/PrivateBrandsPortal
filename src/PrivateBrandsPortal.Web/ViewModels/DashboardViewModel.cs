using PrivateBrandsPortal.Web.Models.Enums;

namespace PrivateBrandsPortal.Web.ViewModels;

public sealed class DashboardViewModel
{
    public bool IsGlobal { get; init; }
    public bool CanCreate { get; init; }
    public int ActiveProjects { get; init; }
    public int AwaitingApproval { get; init; }
    public int RecentlyChanged { get; init; }
    public int Completed { get; init; }
    public int AwaitingPmUpdate { get; init; }
    public IReadOnlyList<DashboardProject> RecentProjects { get; init; } = [];
}
public sealed record DashboardProject(int Id, string ProjectNumber, string Customer, string Country,
    string ProjectManager, int SKUs, ProjectStatus Status, DateTimeOffset UpdatedAtUtc, bool IsOwned);
public sealed record AwaitingPmPage(IReadOnlyList<DashboardProject> Projects, int Page, int Pages);
