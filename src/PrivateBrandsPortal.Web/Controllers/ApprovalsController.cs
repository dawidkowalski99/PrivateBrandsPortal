using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PrivateBrandsPortal.Web.Interfaces;
using PrivateBrandsPortal.Web.Models.Enums;
using PrivateBrandsPortal.Web.ViewModels;
namespace PrivateBrandsPortal.Web.Controllers;

[Authorize(Policy = "ManagerReview")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ApprovalsController(IApprovalService approvals) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct) => View(await approvals.QueueAsync(ct));
    public async Task<IActionResult> Review(int id, CancellationToken ct)
    {
        var model = await approvals.ReviewAsync(id, ct);
        return model is null ? NotFound() : View(model);
    }
    [HttpGet]
    public async Task<IActionResult> Decision(int projectId, int productId, ReviewDecision decision, CancellationToken ct)
    {
        var model = await approvals.FormAsync(projectId, productId, decision, ct);
        return model is null ? NotFound() : View(model);
    }
    [HttpPost]
    public async Task<IActionResult> Decision([Bind(Prefix = "Input")] ReviewInput input, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            try
            {
                await approvals.DecideAsync(input, ct);
                TempData["Success"] = "Product decision saved.";
                return RedirectToAction(nameof(Review), new { id = input.ProjectId });
            }
            catch (ValidationException ex) { ModelState.AddModelError("", ex.Message); }
        }
        var model = await approvals.FormAsync(input.ProjectId, input.ProductId, input.Decision ?? 0, ct);
        if (model is null) { TempData["Error"] = "Review changed or is unavailable. Refresh the project."; return RedirectToAction(nameof(Review), new { id = input.ProjectId }); }
        // Preserve the submitted versions on validation errors. A stale edit requires an explicit reload.
        model.Input = input;
        return View(model);
    }
}
