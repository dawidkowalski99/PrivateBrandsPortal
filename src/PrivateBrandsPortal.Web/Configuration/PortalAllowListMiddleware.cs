using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using PrivateBrandsPortal.Web.Interfaces;
using PrivateBrandsPortal.Web.Services;
namespace PrivateBrandsPortal.Web.Configuration;

public sealed class PortalAllowListMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context,IAppUserService users)
    {
        if(context.User.Identity?.IsAuthenticated==true && context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is null)
        {
            try { if(!(await users.GetCurrentAsync(context.RequestAborted)).IsActive)throw new PortalAccessException(); }
            catch(PortalAccessException)
            {
                context.Response.Headers.CacheControl="no-store";
                var result=new ViewResult {ViewName="~/Views/Home/AccessDenied.cshtml",StatusCode=403,
                    ViewData=new ViewDataDictionary<string>(new EmptyModelMetadataProvider(),new ModelStateDictionary()){Model=context.User.Identity.Name ?? ""}};
                await result.ExecuteResultAsync(new ActionContext(context,context.GetRouteData(),new ActionDescriptor()));
                return;
            }
        }
        await next(context);
    }
}
