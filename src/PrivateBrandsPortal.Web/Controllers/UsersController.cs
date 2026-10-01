using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PrivateBrandsPortal.Web.Services;
using PrivateBrandsPortal.Web.ViewModels;

namespace PrivateBrandsPortal.Web.Controllers;

[Authorize(Policy = PermissionCodes.ManageUsers), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class UsersController(UserAdministrationService service) : Controller
{
    public async Task<IActionResult> Index(string? search, CancellationToken ct)
    {
        if (search?.Length > 200) return BadRequest();
        return View(await service.ListAsync(search, ct));
    }
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        try { var page = await service.GetAsync(id, ct); return page is null ? NotFound() : View(page); }
        catch (PortalAccessException) { return Forbid(); }
    }
    [HttpPost]
    public async Task<IActionResult> Edit([Bind(Prefix = "Input")] UserEditInput input, CancellationToken ct)
    {
        try {
            if (ModelState.IsValid) {
                await service.SaveAsync(input, ct);
                TempData["Success"] = "User access updated.";
                return RedirectToAction("Index", "Dashboard");
            }
        }
        catch (PortalAccessException) { return Forbid(); }
        catch (ValidationException ex) { ModelState.AddModelError("", ex.Message); }
        try {
            var page = await service.GetAsync(input.Id, ct);
            if (page is null) return NotFound();
            page.Input = input; return View(page);
        }
        catch (PortalAccessException) { return Forbid(); }
    }
}
