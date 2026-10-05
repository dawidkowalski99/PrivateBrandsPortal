using PrivateBrandsPortal.Web.Models.Entities;
using PrivateBrandsPortal.Web.Models.Enums;

namespace PrivateBrandsPortal.Web.Services;

public static class ProjectQueries
{
    public static IQueryable<Project> Active(this IQueryable<Project> query) =>
        query.Where(x => x.ArchivedAtUtc == null && x.Status != ProjectStatus.Rejected);

    public static IQueryable<ProjectProduct> AwaitingPmUpdate(this IQueryable<ProjectProduct> query) =>
        query.Where(x => x.CommercialStatus == null &&
            (x.ReviewStatus == ProductReviewStatus.Approved || x.ReviewStatus == ProductReviewStatus.EditedAndApproved));
}
