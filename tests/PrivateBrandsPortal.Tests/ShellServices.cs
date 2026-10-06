using PrivateBrandsPortal.Web.Interfaces;
using PrivateBrandsPortal.Web.Models.Entities;
using PrivateBrandsPortal.Web.ViewModels;
namespace PrivateBrandsPortal.Tests;

internal sealed class ShellAppUser(Microsoft.AspNetCore.Http.IHttpContextAccessor accessor) : IAppUserService
{
    public Task<AppUser> GetCurrentAsync(CancellationToken ct = default) =>
        Task.FromResult(new AppUser { Id = 1, DomainLogin = "TEST\\reader", DisplayName = "Test reader", Role = accessor.HttpContext?.Request.Headers["X-Test-AppRole"].ToString() == "Manager" ? PrivateBrandsPortal.Web.Models.Enums.AppRole.Manager : PrivateBrandsPortal.Web.Models.Enums.AppRole.ProjectManager });
}
internal sealed class ShellProjects : IProjectService
{
    public Task<int> AwaitingPmAsync(CancellationToken ct = default) => Task.FromResult(0);
    public Task<IReadOnlyList<ProjectListItemViewModel>> ListAsync(CancellationToken ct = default, string? search = null) =>
        Task.FromResult<IReadOnlyList<ProjectListItemViewModel>>([]);
    public Task<ProjectDetailsViewModel?> DetailsAsync(int id, CancellationToken ct = default) => Task.FromResult<ProjectDetailsViewModel?>(null);
    public Task<DraftInput?> LoadDraftAsync(int id, CancellationToken ct = default) => Task.FromResult<DraftInput?>(null);
    public Task<int> SaveDraftAsync(DraftInput input, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<LookupItem>> CountriesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<LookupItem>>([]);
    public Task<IReadOnlyList<LookupItem>> ProductCategoriesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<LookupItem>>([]);
}

internal sealed class ShellDashboard : IDashboardService
{
    public Task<DashboardViewModel> GetAsync(CancellationToken ct=default)=>Task.FromResult(new DashboardViewModel{CanCreate=true});
    public Task<AwaitingPmPage> AwaitingAsync(int page,CancellationToken ct=default)=>Task.FromResult(new AwaitingPmPage([],1,1));
    public Task<ProjectDetailsViewModel?> OverviewAsync(int id,CancellationToken ct=default)=>Task.FromResult<ProjectDetailsViewModel?>(null);
}
