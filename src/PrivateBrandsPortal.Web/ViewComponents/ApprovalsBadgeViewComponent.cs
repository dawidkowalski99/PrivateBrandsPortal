using Microsoft.AspNetCore.Mvc;
using PrivateBrandsPortal.Web.Interfaces;
using PrivateBrandsPortal.Web.Services;
namespace PrivateBrandsPortal.Web.ViewComponents;
public sealed class ApprovalsBadgeViewComponent(IApprovalService approvals) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        // The safe error page must render even if SQL is unavailable.
        if (HttpContext.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerPathFeature>() is not null ||
            (ViewContext.RouteData.Values["controller"]?.ToString() == "Home" && ViewContext.RouteData.Values["action"]?.ToString() == "Error") ||
            HttpContext.User.Identity?.IsAuthenticated != true) return Content("");
        try { var count = await approvals.CountAsync(HttpContext.RequestAborted); return Content(count > 0 ? $" ({count})" : ""); }
        catch (PortalAccessException) { return Content(""); }
    }
}
