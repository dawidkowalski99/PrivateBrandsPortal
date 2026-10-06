using PrivateBrandsPortal.Web.ViewModels;
namespace PrivateBrandsPortal.Web.Interfaces;
public interface IProjectService
{
    Task<int> AwaitingPmAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ProjectListItemViewModel>> ListAsync(CancellationToken ct = default, string? search = null);
    Task<ProjectDetailsViewModel?> DetailsAsync(int id, CancellationToken ct = default);
    Task<DraftInput?> LoadDraftAsync(int id, CancellationToken ct = default);
    Task<int> SaveDraftAsync(DraftInput input, CancellationToken ct = default);
    Task<IReadOnlyList<LookupItem>> CountriesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<LookupItem>> ProductCategoriesAsync(CancellationToken ct = default);
}
