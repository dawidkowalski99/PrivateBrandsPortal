using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PrivateBrandsPortal.Web.Services;
using PrivateBrandsPortal.Web.ViewModels;
namespace PrivateBrandsPortal.Web.Controllers;
[Authorize,ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class ImplementationController(ImplementationService service):Controller
{
    public async Task<IActionResult> Index(CancellationToken ct){try{return View(await service.QueueAsync(ct));}catch(PortalAccessException){return Forbid();}}
    public async Task<IActionResult> Review(int id,CancellationToken ct){try{var m=await service.ReviewAsync(id,ct);return m is null?NotFound():View(m);}catch(PortalAccessException){return Forbid();}}
    [HttpPost]public async Task<IActionResult> Review([Bind(Prefix="Input")] ImplementationDecisionInput input,CancellationToken ct)
    {
        try {
            if(ModelState.IsValid){await service.DecideAsync(input,ct);TempData["Success"]="Implementation decision saved.";return RedirectToAction(nameof(Index));}
        }catch(PortalAccessException){return Forbid();}catch(ValidationException ex){ModelState.AddModelError("",ex.Message);}
        try{var m=await service.ReviewAsync(input.Id,ct);if(m is null)return NotFound();m.Input=input;return View(m);}catch(PortalAccessException){return Forbid();}
    }
    [HttpPost]public async Task<IActionResult> Resubmit(int projectId,int productId,DateTimeOffset? version,CancellationToken ct)
    {
        if(!ModelState.IsValid||version is null)return BadRequest();
        try{await service.ResubmitAsync(projectId,productId,version.Value,ct);TempData["Success"]="Implementation approval requested.";}
        catch(PortalAccessException){return Forbid();}catch(ValidationException ex){TempData["Error"]=ex.Message;}
        return RedirectToAction("Details","Projects",new{id=projectId});
    }
}
