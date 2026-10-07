using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using PrivateBrandsPortal.Web.Models.Enums;
using PrivateBrandsPortal.Web.Services;
using PrivateBrandsPortal.Web.ViewModels;
namespace PrivateBrandsPortal.Tests;
[Collection("SQL integration")]
public sealed class ProjectClosureTests
{
    private static CustomerClosureService Service(ProjectSqlTests.Scope s)=>new(s.Db,s.Users,TimeProvider.System);
    private static async Task<int> Accepted(ProjectSqlTests.Scope s){
        var id=await CommercialWorkflowTests.Submitted(s);await CommercialWorkflowTests.Decide(s,id,0,ReviewDecision.Approved);return id;
    }
    [Fact]
    public async Task Closure_archives_preserves_documents_review_and_reason_snapshot(){
        await using var s=new ProjectSqlTests.Scope();var id=await Accepted(s);
        await CommercialWorkflowTests.SupplyDocuments(s,id);
        var form=(await Service(s).FormAsync(id,default))!;form.Input.Confirm=true;
        form.Input.ReasonId=await s.Db.CustomerRejectionReasons.Where(x=>x.Code=="PRICE_TOO_HIGH").Select(x=>x.Id).SingleAsync();
        form.Input.Comment=" Customer declined ";await Service(s).CloseAsync(form.Input,default);
        var p=(await s.Projects.DetailsAsync(id))!;
        Assert.NotNull(p.ArchivedAtUtc);Assert.Equal(ProjectStatus.Cancelled,p.Status);
        Assert.Equal("The price is too high",p.CustomerRejectionReason);Assert.Equal("Customer declined",p.CustomerRejectionComment);
        Assert.Equal(CommercialStatus.CustomerNotApproved,p.Products[0].CommercialStatus);Assert.Equal(ProductReviewStatus.Pending,p.Products[1].ReviewStatus);
        Assert.Single(p.Products[0].Reviews);Assert.Equal(2,await s.Db.ProjectAttachments.CountAsync(x=>x.ProjectId==id));
        Assert.Contains(await CommercialWorkflowTests.Commercial(s).ArchiveAsync(default),x=>x.Id==id && x.CustomerRejectionReason=="The price is too high");
        Assert.True(await s.Db.AuditLogs.AnyAsync(x=>x.EntityId==id && x.FieldName=="ProjectCustomerRejected"));
        Assert.Null(await Service(s).FormAsync(id,default));
        Assert.Null(await CommercialWorkflowTests.Commercial(s).FormAsync(id,p.Products[0].Id,default));
    }
    [Theory]
    [InlineData(false,true)]
    [InlineData(true,false)]
    public async Task Closure_requires_reason_and_explicit_confirmation(bool reason,bool confirmation){
        await using var s=new ProjectSqlTests.Scope();var id=await Accepted(s);var f=(await Service(s).FormAsync(id,default))!;
        f.Input.Confirm=confirmation;if(reason)f.Input.ReasonId=f.Reasons.First().Id;
        await Assert.ThrowsAsync<ValidationException>(()=>Service(s).CloseAsync(f.Input,default));
        Assert.Null((await s.Projects.DetailsAsync(id))!.ArchivedAtUtc);
    }
    [Fact]
    public async Task Closure_rejects_foreign_owner_and_inactive_reason(){
        await using var s=new ProjectSqlTests.Scope();await using var other=new ProjectSqlTests.Scope();
        var id=await Accepted(s);var f=(await Service(s).FormAsync(id,default))!;
        var reason=new PrivateBrandsPortal.Web.Models.Entities.CustomerRejectionReason{Code="TEST_"+Guid.NewGuid().ToString("N"),Name=s.Login+" reason",IsActive=false};
        s.Db.CustomerRejectionReasons.Add(reason);await s.Db.SaveChangesAsync();f.Input.Confirm=true;f.Input.ReasonId=reason.Id;
        Assert.Null(await Service(other).FormAsync(id,default));
        await Assert.ThrowsAsync<ValidationException>(()=>Service(other).CloseAsync(f.Input,default));
        await Assert.ThrowsAsync<ValidationException>(()=>Service(s).CloseAsync(f.Input,default));
    }
    [Fact]
    public async Task Closure_preserves_delivered_product_and_closes_unfinished_sibling(){
        await using var s=new ProjectSqlTests.Scope();var id=await Accepted(s);await CommercialWorkflowTests.Decide(s,id,1,ReviewDecision.Approved);
        await CommercialWorkflowTests.Status(s,id,0,CommercialStatus.SalesAndDelivery);
        var f=(await Service(s).FormAsync(id,default))!;f.Input.ReasonId=f.Reasons.First().Id;f.Input.Confirm=true;
        await Service(s).CloseAsync(f.Input,default);
        var p=(await s.Projects.DetailsAsync(id))!;
        Assert.Equal(CommercialStatus.SalesAndDelivery,p.Products[0].CommercialStatus);
        Assert.Equal(CommercialStatus.CustomerNotApproved,p.Products[1].CommercialStatus);Assert.NotNull(p.ArchivedAtUtc);
    }
    [Theory]
    [InlineData("/Implementation/Review")]
    [InlineData("/Implementation/Resubmit")]
    [InlineData("/CustomerClosure/Close")]
    [InlineData("/Dictionaries/Delete")]
    public async Task Mutation_requires_csrf(string path){
        await using var factory=new AuthenticatedFactory();using var client=factory.CreateClient();client.DefaultRequestHeaders.Add("X-Test-User","reader");
        var response=await client.PostAsync(path,new FormUrlEncodedContent([]));
        Assert.Contains(response.StatusCode,new[]{System.Net.HttpStatusCode.BadRequest,System.Net.HttpStatusCode.Forbidden});
    }
}
