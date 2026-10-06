using PrivateBrandsPortal.Web.Models.Entities;
using PrivateBrandsPortal.Web.Models.Enums;

namespace PrivateBrandsPortal.Web.Services;

public static class ProjectQueries
{
    public static IQueryable<Project> Search(this IQueryable<Project> query, string? search)
    {
        var term = search?.Trim();
        if (term?.Length > 200) throw new System.ComponentModel.DataAnnotations.ValidationException("Search supports up to 200 characters.");
        return string.IsNullOrEmpty(term) ? query : query.Where(x => x.ProjectNumber.Contains(term) || x.Customer.Contains(term) || x.Products.Any(p => p.SKU.Contains(term)));
    }
    public static IQueryable<Project> Active(this IQueryable<Project> query) =>
        query.Where(x => x.ArchivedAtUtc == null && x.Status != ProjectStatus.Rejected);

    public static IQueryable<ProjectProduct> AwaitingPmUpdate(this IQueryable<ProjectProduct> query) =>
        query.Where(x => x.CommercialStatus == null &&
            (x.ReviewStatus == ProductReviewStatus.Approved || x.ReviewStatus == ProductReviewStatus.EditedAndApproved));
}
