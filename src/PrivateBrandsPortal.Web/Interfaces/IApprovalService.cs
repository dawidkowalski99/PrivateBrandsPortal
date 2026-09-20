using PrivateBrandsPortal.Web.Models.Enums;
using PrivateBrandsPortal.Web.ViewModels;
namespace PrivateBrandsPortal.Web.Interfaces;
public interface IApprovalService
{
    Task SubmitAsync(int id, DateTimeOffset version, CancellationToken ct = default);
    Task<IReadOnlyList<ApprovalItem>> QueueAsync(CancellationToken ct = default);
    Task<int> CountAsync(CancellationToken ct = default);
    Task<ProjectDetailsViewModel?> ReviewAsync(int id, CancellationToken ct = default);
    Task<ReviewFormModel?> FormAsync(int projectId, int productId, ReviewDecision decision, CancellationToken ct = default);
    Task DecideAsync(ReviewInput input, CancellationToken ct = default);
}
