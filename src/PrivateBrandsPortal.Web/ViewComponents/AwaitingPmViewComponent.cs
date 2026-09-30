using Microsoft.AspNetCore.Mvc;
using PrivateBrandsPortal.Web.Interfaces;
namespace PrivateBrandsPortal.Web.ViewComponents;
public sealed class AwaitingPmViewComponent(IProjectService projects):ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()=>View(await projects.AwaitingPmAsync(HttpContext.RequestAborted));
}
