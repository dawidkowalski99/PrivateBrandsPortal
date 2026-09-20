using System.ComponentModel.DataAnnotations;

namespace PrivateBrandsPortal.Web.Models.Enums;

public enum AuditChangeType
{
    [Display(Name = "Created")]
    Created = 1,
    [Display(Name = "Updated")]
    Updated = 2,
    [Display(Name = "Deleted")]
    Deleted = 3,
    [Display(Name = "Manager edit")]
    ManagerEdit = 4,
    [Display(Name = "Project submitted")]
    ProjectSubmitted = 5,
}
