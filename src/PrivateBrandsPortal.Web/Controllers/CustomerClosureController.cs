using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PrivateBrandsPortal.Web.Configuration;
using PrivateBrandsPortal.Web.Services;
using PrivateBrandsPortal.Web.ViewModels;
namespace PrivateBrandsPortal.Web.Controllers;
[Authorize,ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class CustomerClosureController(CustomerClosureService service):Controller
{
    public async Task<IActionResult> Close(int id,int productId,CancellationToken ct)
    {
        try{var m=await service.FormAsync(id,productId,ct);return m is null?NotFound():View(m);}
        catch(PortalAccessException){return Forbid();}
    }
    [HttpPost]public async Task<IActionResult> Close([Bind(Prefix="Input")]CustomerClosureInput input,CancellationToken ct)
    {
        try{
            if(ModelState.IsValid){
                var result=await service.CloseAsync(input,ct);
                TempData["Success"]="Product closed — rejected by Customer. "+result.Reason+
                    (result.Archived?" Project completed and moved to Archive.":"");
                TempData[WorkflowUiEvents.Key]=WorkflowUiEvents.CustomerRejected;
                return RedirectToAction("Details","Projects",new{id=input.ProjectId});
            }
        }catch(PortalAccessException){return Forbid();}
        catch(ValidationException ex){ModelState.AddModelError("",ex.Message);}
        var m=await service.FormAsync(input.ProjectId,input.ProductId,ct);
        if(m is null)return NotFound();m.Input=input;return View(m);
    }
}
