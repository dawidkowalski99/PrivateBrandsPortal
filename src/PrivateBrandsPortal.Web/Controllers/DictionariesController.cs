using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PrivateBrandsPortal.Web.Configuration;
using PrivateBrandsPortal.Web.Services;
using PrivateBrandsPortal.Web.ViewModels;
namespace PrivateBrandsPortal.Web.Controllers;
[Authorize(Policy=PortalAuthorization.AdministerPortal),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class DictionariesController(DictionaryService service):Controller
{
    public async Task<IActionResult> Index(DictionaryKind kind,CancellationToken ct)=>!Enum.IsDefined(kind)?NotFound():View(await service.ListAsync(kind,ct));
    [HttpGet]public async Task<IActionResult> Edit(DictionaryKind kind,int id,CancellationToken ct){if(!Enum.IsDefined(kind))return NotFound();var model=await service.GetAsync(kind,id,ct);return model is null?NotFound():View(model);}
    [HttpPost]public async Task<IActionResult> Edit(DictionaryInput input,CancellationToken ct){if(ModelState.IsValid)try{await service.SaveAsync(input,ct);return RedirectToAction(nameof(Index),new{kind=input.Kind});}catch(ValidationException ex){ModelState.AddModelError("",ex.Message);}return View(input);}
}
