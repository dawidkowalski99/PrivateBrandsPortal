using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using PrivateBrandsPortal.Web.Interfaces;
using PrivateBrandsPortal.Web.Models.Enums;
using PrivateBrandsPortal.Web.Services;
namespace PrivateBrandsPortal.Web.Configuration;

public sealed class ProjectAccessFilter(IAppUserService users) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        try
        {
            var user = await users.GetCurrentAsync(context.HttpContext.RequestAborted);
            if (!WorkflowAccess.Allows(user, AppRole.ProjectManager)) { context.Result = new ForbidResult(); return; }
            await next();
        }
        catch (PortalAccessException) { context.Result = new ForbidResult(); }
    }
}

