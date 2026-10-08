using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using PrivateBrandsPortal.Web.Configuration;
using PrivateBrandsPortal.Web.Services;
using PrivateBrandsPortal.Web.ViewModels;
namespace PrivateBrandsPortal.Web.Controllers;
[ServiceFilter(typeof(ProjectAccessFilter)), ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class CommercialController(CommercialService service) : Controller
{
    [HttpGet] public async Task<IActionResult> Change(int projectId,int productId,CancellationToken ct)
    { var model=await service.FormAsync(projectId,productId,ct); return model is null ? NotFound() : View(model); }
    [HttpPost] public async Task<IActionResult> Change([Bind(Prefix="Input")] CommercialInput input,CancellationToken ct)
    {
        if(ModelState.IsValid) try { await service.UpdateAsync(input,ct); TempData["Success"]=input.Status==PrivateBrandsPortal.Web.Models.Enums.CommercialStatus.SalesAndDelivery ? "Sales & Delivery completed" : "Commercial status updated."; if(input.Status==PrivateBrandsPortal.Web.Models.Enums.CommercialStatus.SalesAndDelivery)TempData[WorkflowUiEvents.Key]=WorkflowUiEvents.SalesAndDeliveryCompleted; return RedirectToAction("Details","Projects",new{id=input.ProjectId}); }
        catch(ValidationException ex){ ModelState.AddModelError("",ex.Message); }
        var model=await service.FormAsync(input.ProjectId,input.ProductId,ct); if(model is null) return NotFound();
        model.Input=input; return View(model);
    }
}
[ServiceFilter(typeof(ProjectAccessFilter)), ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class ArchiveController(CommercialService service) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct,string? search=null){if(search?.Length>200)return BadRequest();ViewData["Search"]=search;return View(await service.ArchiveAsync(ct,search));}
}
