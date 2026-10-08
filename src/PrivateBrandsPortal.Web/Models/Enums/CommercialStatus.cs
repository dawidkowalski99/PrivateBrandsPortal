using System.ComponentModel.DataAnnotations;
namespace PrivateBrandsPortal.Web.Models.Enums;

public enum CommercialStatus
{
    [Display(Name = "Price offer submitted")] PriceOfferSubmitted = 1,
    [Display(Name = "Offer under negotiation")] OfferUnderNegotiation = 2,
    [Display(Name = "Project approved by Customer / order")] CustomerApprovedOrder = 3,
    [Display(Name = "Rejected by Customer")] CustomerNotApproved = 4,
    [Display(Name = "Implementation into production")] ImplementationIntoProduction = 5,
    [Display(Name = "Sales and delivery")] SalesAndDelivery = 6
}
