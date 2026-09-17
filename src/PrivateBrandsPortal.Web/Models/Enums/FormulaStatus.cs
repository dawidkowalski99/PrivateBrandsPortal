using System.ComponentModel.DataAnnotations;

namespace PrivateBrandsPortal.Web.Models.Enums;

public enum FormulaStatus
{
    [Display(Name = "Ready to go")]
    ReadyToGo = 0,
    [Display(Name = "New formula")]
    NewFormula = 1,
}

