using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PrivateBrandsPortal.Web.Configuration;
using PrivateBrandsPortal.Web.Controllers;
using PrivateBrandsPortal.Web.Interfaces;
using PrivateBrandsPortal.Web.Models.Enums;
using PrivateBrandsPortal.Web.Services;
namespace PrivateBrandsPortal.Tests;
[Collection("SQL integration")]
public sealed class WorkflowUiEventTests
{
    private sealed class MemoryTempData : ITempDataProvider {
        public IDictionary<string,object> LoadTempData(HttpContext c)=>new Dictionary<string,object>();
        public void SaveTempData(HttpContext c,IDictionary<string,object> v){}
    }
    private static T WithTemp<T>(T controller)where T:Controller {
        controller.TempData=new TempDataDictionary(new DefaultHttpContext(),new MemoryTempData());return controller;
    }
    private static async Task<int> Accepted(ProjectSqlTests.Scope s){
        var id=await CommercialWorkflowTests.Submitted(s);await CommercialWorkflowTests.Decide(s,id,0,ReviewDecision.Approved);return id;
    }
    [Theory][InlineData(true)][InlineData(false)]
    public async Task Sales_event_is_created_only_after_success(bool success){
        await using var s=new ProjectSqlTests.Scope();var id=await Accepted(s);
        if(success){
            await CommercialWorkflowTests.Status(s,id,0,CommercialStatus.ImplementationIntoProduction);
            await CommercialWorkflowTests.ApproveImplementation(s,id,(await s.Projects.DetailsAsync(id))!.Products[0].Id);
        }
        var p=(await s.Projects.DetailsAsync(id))!;
        var controller=WithTemp(new CommercialController(CommercialWorkflowTests.Commercial(s)));
        await controller.Change(new(){ProjectId=id,ProductId=p.Products[0].Id,Version=p.UpdatedAtUtc,Status=CommercialStatus.SalesAndDelivery},default);
        Assert.Equal(success?WorkflowUiEvents.SalesAndDeliveryCompleted:null,controller.TempData[WorkflowUiEvents.Key]);
    }
    [Theory][InlineData(true)][InlineData(false)]
    public async Task Rejection_event_is_created_only_after_success(bool success){
        await using var s=new ProjectSqlTests.Scope();var id=await Accepted(s);
        var p=(await s.Projects.DetailsAsync(id))!;
        var service=new CustomerClosureService(s.Db,s.Users,TimeProvider.System);
        var form=(await service.FormAsync(id,p.Products[0].Id,default))!;
        form.Input.Confirm=true;form.Input.ReasonId=success?form.Reasons.First().Id:null;
        var controller=WithTemp(new CustomerClosureController(service));
        await controller.Close(form.Input,default);
        Assert.Equal(success?WorkflowUiEvents.CustomerRejected:null,controller.TempData[WorkflowUiEvents.Key]);
        if(success){Assert.Contains("Product closed",controller.TempData["Success"]!.ToString());Assert.DoesNotContain("Project completed",controller.TempData["Success"]!.ToString());}
    }
    [Fact]
    public async Task Ordinary_commercial_status_does_not_create_animation(){
        await using var s=new ProjectSqlTests.Scope();var id=await Accepted(s);var p=(await s.Projects.DetailsAsync(id))!;
        var controller=WithTemp(new CommercialController(CommercialWorkflowTests.Commercial(s)));
        await controller.Change(new(){ProjectId=id,ProductId=p.Products[0].Id,Version=p.UpdatedAtUtc,Status=CommercialStatus.OfferUnderNegotiation},default);
        Assert.Null(controller.TempData[WorkflowUiEvents.Key]);
    }
    [Fact]
    public async Task Event_survives_redirect_and_is_consumed_by_first_details_response(){
        await using var s=new ProjectSqlTests.Scope();var id=await Accepted(s);var p=(await s.Projects.DetailsAsync(id))!;
        await using var baseline=new AuthenticatedFactory();
        await using var host=baseline.WithWebHostBuilder(b=>b.ConfigureServices(services=>{
            services.AddScoped<IAppUserService>(_=>s.Users);services.AddScoped<IProjectService>(_=>s.Projects);
        }));
        using var client=host.CreateClient(new(){AllowAutoRedirect=false});client.DefaultRequestHeaders.Add("X-Test-User","reader");
        var url=$"/CustomerClosure/Close/{id}?productId={p.Products[0].Id}";
        var html=await client.GetStringAsync(url);
        string Hidden(string name)=>WebUtility.HtmlDecode(Regex.Match(html,$"name=\"{Regex.Escape(name)}\"[^>]*value=\"([^\"]*)\"").Groups[1].Value);
        var reason=await s.Db.CustomerRejectionReasons.Where(x=>x.IsActive).Select(x=>x.Id).FirstAsync();
        var response=await client.PostAsync("/CustomerClosure/Close",new FormUrlEncodedContent(new Dictionary<string,string>{
            ["__RequestVerificationToken"]=Hidden("__RequestVerificationToken"),["Input.ProjectId"]=id.ToString(),
            ["Input.ProductId"]=p.Products[0].Id.ToString(),["Input.Version"]=Hidden("Input.Version"),
            ["Input.ReasonId"]=reason.ToString(),["Input.Confirm"]="true"}));
        Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);
        var first=await client.GetStringAsync(response.Headers.Location);
        Assert.Contains("data-workflow-feedback=\"CustomerRejected\"",first);
        Assert.Contains("workflow-feedback.js",first);
        var second=await client.GetStringAsync(response.Headers.Location);
        Assert.DoesNotContain("data-workflow-feedback=",second);
    }
    private sealed class WriteFailure : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData data,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,CancellationToken ct=default)
            =>throw new InvalidOperationException("Deliberate terminal write failure.");
    }
    [Theory][InlineData(true)][InlineData(false)]
    public async Task Database_failure_does_not_create_event_or_change_status(bool rejection){
        await using var s=new ProjectSqlTests.Scope();var id=await Accepted(s);
        if(!rejection){
            await CommercialWorkflowTests.Status(s,id,0,CommercialStatus.ImplementationIntoProduction);
            await CommercialWorkflowTests.ApproveImplementation(s,id,(await s.Projects.DetailsAsync(id))!.Products[0].Id);
        }
        var before=(await s.Projects.DetailsAsync(id))!;
        await using var failing=s.NewContext(new WriteFailure());
        if(rejection){
            var service=new CustomerClosureService(failing,s.MakeUsers(failing),TimeProvider.System);
            var form=(await service.FormAsync(id,before.Products[0].Id,default))!;
            form.Input.Confirm=true;form.Input.ReasonId=form.Reasons.First().Id;
            var controller=WithTemp(new CustomerClosureController(service));
            await Assert.ThrowsAsync<InvalidOperationException>(()=>controller.Close(form.Input,default));
            Assert.Null(controller.TempData[WorkflowUiEvents.Key]);
        } else {
            var controller=WithTemp(new CommercialController(new CommercialService(failing,s.MakeUsers(failing),TimeProvider.System)));
            await Assert.ThrowsAsync<InvalidOperationException>(()=>controller.Change(new(){ProjectId=id,ProductId=before.Products[0].Id,Version=before.UpdatedAtUtc,Status=CommercialStatus.SalesAndDelivery},default));
            Assert.Null(controller.TempData[WorkflowUiEvents.Key]);
        }
        var after=(await s.Projects.DetailsAsync(id))!;
        Assert.Equal(before.UpdatedAtUtc,after.UpdatedAtUtc);
        Assert.Equal(before.Products[0].CommercialStatus,after.Products[0].CommercialStatus);
    }
}
