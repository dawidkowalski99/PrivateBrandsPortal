using System.ComponentModel.DataAnnotations;

namespace PrivateBrandsPortal.Web.Models.Enums;

public enum ProjectStatus
{
    [Display(Name = "Draft")]
    Draft = 0,
    [Display(Name = "Awaiting Manager Review")]
    AwaitingManagerReview = 1,
    [Display(Name = "Partially Reviewed")]
    PartiallyReviewed = 2,
    [Display(Name = "Approved")]
    Approved = 3,
    [Display(Name = "Rejected")]
    Rejected = 4,
    [Display(Name = "In Progress")]
    InProgress = 5,
    [Display(Name = "Completed")]
    Completed = 6,
    [Display(Name = "Cancelled")]
    Cancelled = 7,
    [Display(Name = "Partially Approved")]
    PartiallyApproved = 8,
}
