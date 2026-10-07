using Microsoft.AspNetCore.Mvc;
using PrivateBrandsPortal.Web.Services;
namespace PrivateBrandsPortal.Web.ViewComponents;
public sealed class AttachmentsViewComponent(AttachmentService service) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(int projectId)
    {
        var model=await service.PanelAsync(projectId,HttpContext.RequestAborted);
        return model is null ? Content("") : View(model);
    }
}
