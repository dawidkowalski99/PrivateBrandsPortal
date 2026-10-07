using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PrivateBrandsPortal.Web.Services;
using PrivateBrandsPortal.Web.ViewModels;
namespace PrivateBrandsPortal.Web.Controllers;
[Authorize,ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class CustomerClosureController(CustomerClosureService service):Controller
{
    public async Task<IActionResult> Close(int id,CancellationToken ct){var m=await service.FormAsync(id,ct);return m is null?NotFound():View(m);}
    [HttpPost]public async Task<IActionResult> Close([Bind(Prefix="Input")]CustomerClosureInput input,CancellationToken ct)
    {
        if(ModelState.IsValid)try{var reason=await service.CloseAsync(input,ct);TempData["Success"]="Project closed — not approved by Customer. "+reason;TempData["WorkflowEvent"]="closed";return RedirectToAction("Details","Projects",new{id=input.ProjectId});}
        catch(ValidationException ex){ModelState.AddModelError("",ex.Message);}
        var m=await service.FormAsync(input.ProjectId,ct);if(m is null)return NotFound();m.Input=input;return View(m);
    }
}
