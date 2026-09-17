using System.ComponentModel.DataAnnotations;

namespace PrivateBrandsPortal.Web.Models.Enums;

public enum ReviewDecision
{
    [Display(Name = "Approved")]
    Approved = 1,
    [Display(Name = "Rejected")]
    Rejected = 2,
    [Display(Name = "Edited and approved")]
    EditedAndApproved = 3,
}

