using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PrivateBrandsPortal.Web.Configuration;
using PrivateBrandsPortal.Web.Services;
using PrivateBrandsPortal.Web.ViewModels;
namespace PrivateBrandsPortal.Web.Controllers;
[Authorize,ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class DictionariesController(DictionaryService service,DictionaryDeletionService deletion):Controller
{
    [HttpGet]public async Task<IActionResult> Delete(DictionaryKind kind,int id,CancellationToken ct){try{var m=await deletion.FormAsync(kind,id,ct);return m is null?NotFound():View(m);}catch(PortalAccessException){return Forbid();}}
    [HttpPost,ActionName("Delete")]public async Task<IActionResult> DeleteConfirmed(DictionaryKind kind,int id,DateTimeOffset? version,CancellationToken ct){
        if(!ModelState.IsValid)return BadRequest();
        try{await deletion.DeleteAsync(kind,id,version,ct);TempData["Success"]="Dictionary value deleted.";}
        catch(PortalAccessException){return Forbid();}
        catch(ValidationException ex){TempData["Error"]=ex.Message;}
        return RedirectToAction(nameof(Index),new{kind});
    }
    public async Task<IActionResult> Index(DictionaryKind kind,CancellationToken ct,string? search=null,int? categoryId=null)
    {
        if(!Enum.IsDefined(kind)||search?.Length>200)return BadRequest();
        try{return View(await service.ListAsync(kind,ct,search,categoryId));}
        catch(PortalAccessException){return Forbid();}
    }
    [Authorize(Policy=PortalAuthorization.AdministerPortal),HttpGet]public async Task<IActionResult> Edit(DictionaryKind kind,int id,CancellationToken ct){if(!Enum.IsDefined(kind))return NotFound();var model=await service.GetAsync(kind,id,ct);return model is null?NotFound():View(model);}
    [Authorize(Policy=PortalAuthorization.AdministerPortal),HttpPost]public async Task<IActionResult> Edit(DictionaryInput input,CancellationToken ct){if(ModelState.IsValid)try{await service.SaveAsync(input,ct);return RedirectToAction(nameof(Index),new{kind=input.Kind});}catch(ValidationException ex){ModelState.AddModelError("",ex.Message);}input.Categories=await service.CategoriesAsync(ct);input.Countries=await service.CountriesAsync(ct);return View(input);}
}
