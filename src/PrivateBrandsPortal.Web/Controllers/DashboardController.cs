using Microsoft.AspNetCore.Mvc;
using PrivateBrandsPortal.Web.Interfaces;
using PrivateBrandsPortal.Web.Services;
namespace PrivateBrandsPortal.Web.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DashboardController(IDashboardService dashboard) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct) => View(await dashboard.GetAsync(ct));
    public async Task<IActionResult> AwaitingPm(int page = 1, CancellationToken ct = default)
    {
        try { return View(await dashboard.AwaitingAsync(page, ct)); }
        catch (PortalAccessException) { return Forbid(); }
    }
    public async Task<IActionResult> Project(int id, CancellationToken ct)
    {
        try {
            var model = await dashboard.OverviewAsync(id, ct);
            if (model is null) return NotFound();
            ViewData["ReadOnly"] = true;
            return View("~/Views/Projects/Details.cshtml", model);
        }
        catch (PortalAccessException) { return Forbid(); }
    }
}
