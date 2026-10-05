using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PrivateBrandsPortal.Web.Services;
using PrivateBrandsPortal.Web.ViewModels;
namespace PrivateBrandsPortal.Web.Controllers;

[Authorize(Policy = PermissionCodes.ReassignProjects), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TransfersController(ProjectTransferService transfers) : Controller
{
    public async Task<IActionResult> Project(int id, CancellationToken ct)
    {
        var model = await transfers.FormAsync(id, ct);
        return model is null ? NotFound() : View(model);
    }
    [HttpPost]
    public async Task<IActionResult> Project([Bind(Prefix="Input")] ProjectTransferInput input, CancellationToken ct)
    {
        try {
            if(ModelState.IsValid){
                await transfers.TransferAsync(input,ct);
                TempData["Success"]="Project transferred. Ownership and dashboard metrics have been updated.";
                return RedirectToAction("Index","Dashboard");
            }
        }
        catch(PortalAccessException){return Forbid();}
        catch(ValidationException ex){ModelState.AddModelError("",ex.Message);}
        var model=await transfers.FormAsync(input.ProjectId,ct);
        if(model is null)return NotFound();
        model.Input=input;return View(model);
    }
}
