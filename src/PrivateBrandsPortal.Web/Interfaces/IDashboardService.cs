using PrivateBrandsPortal.Web.ViewModels;
namespace PrivateBrandsPortal.Web.Interfaces;

public interface IDashboardService
{
    Task<DashboardViewModel> GetAsync(CancellationToken ct = default);
    Task<AwaitingPmPage> AwaitingAsync(int page, CancellationToken ct = default);
    Task<ProjectDetailsViewModel?> OverviewAsync(int id, CancellationToken ct = default);
}
