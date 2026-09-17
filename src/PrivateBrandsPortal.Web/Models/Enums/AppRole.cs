using System.ComponentModel.DataAnnotations;

namespace PrivateBrandsPortal.Web.Models.Enums;

public enum AppRole
{
    [Display(Name = "Project Manager")]
    ProjectManager = 1,
    [Display(Name = "Manager")]
    Manager = 2,
    [Display(Name = "Admin")]
    Admin = 3,
}

