using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PrivateBrandsPortal.Web.Services;
using PrivateBrandsPortal.Web.ViewModels;
namespace PrivateBrandsPortal.Web.Controllers;

[Authorize]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class AttachmentsController(AttachmentService attachments) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Upload(int projectId, CancellationToken ct)
    {
        try { return View(await attachments.UploadFormAsync(new AttachmentInput{ProjectId=projectId},ct)); }
        catch(PortalAccessException) { return Forbid(); }
        catch(ValidationException ex) { TempData["Error"]=ex.Message;return RedirectToAction("Details","Projects",new{id=projectId}); }
    }
    [HttpPost]
    public async Task<IActionResult> Upload([Bind(Prefix="Input")] AttachmentInput input,CancellationToken ct)
    {
        try {
            if(ModelState.IsValid) {
                await attachments.UploadAsync(input,ct);
                TempData["Success"]="Attachment uploaded.";
                return RedirectToAction("Details","Projects",new{id=input.ProjectId});
            }
        }
        catch(PortalAccessException) { return Forbid(); }
        catch(ValidationException ex) { ModelState.AddModelError("",ex.Message); }
        try { return View(await attachments.UploadFormAsync(input,ct)); }
        catch(PortalAccessException) { return Forbid(); }
        catch(ValidationException ex) { TempData["Error"]=ex.Message;return RedirectToAction("Details","Projects",new{id=input.ProjectId}); }
    }
    [HttpGet]
    public async Task<IActionResult> Download(int id,CancellationToken ct)
    {
        try {
            var file=await attachments.DownloadAsync(id,ct);
            Response.Headers["X-Content-Type-Options"]="nosniff";
            return File(file.Stream,file.ContentType,file.Name);
        }
        catch(PortalAccessException) { return NotFound(); }
        catch(ValidationException ex) { return Problem(title:ex.Message,statusCode:503); }
    }
}
