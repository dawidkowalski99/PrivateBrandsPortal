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
    private static async Task<int> Accepted(ProjectSqlTests.Scope s,bool three=false){
        var draft=WizardTests.ValidDraft();
        if(three){var third=ProductCopyService.Duplicate(draft.Products[0]);third.SKU="THIRD-SKU";draft.Products.Add(third);}
        var id=await CommercialWorkflowTests.Submitted(s,draft);
        for(var i=0;i<draft.Products.Count;i++)await CommercialWorkflowTests.Decide(s,id,i,ReviewDecision.Approved);
        return id;
    }
    private static async Task<CustomerClosureModel> Form(ProjectSqlTests.Scope s,int id,int index=0){
        var p=(await s.Projects.DetailsAsync(id))!;
        return (await Service(s).FormAsync(id,p.Products[index].Id,default))!;
    }
    private static async Task Reject(ProjectSqlTests.Scope s,int id,int index=0){
        var f=await Form(s,id,index);f.Input.Confirm=true;f.Input.ReasonId=f.Reasons.First().Id;
        await Service(s).CloseAsync(f.Input,default);
    }
    [Fact]
    public async Task Rejection_preserves_documents_review_and_product_reason_snapshot(){
        await using var s=new ProjectSqlTests.Scope();var id=await Accepted(s);
        await CommercialWorkflowTests.SupplyDocuments(s,id);
        var f=await Form(s,id);f.Input.Confirm=true;
        f.Input.ReasonId=await s.Db.CustomerRejectionReasons.Where(x=>x.Code=="PRICE_TOO_HIGH").Select(x=>x.Id).SingleAsync();
        f.Input.Comment=" Customer declined ";await Service(s).CloseAsync(f.Input,default);
        var p=(await s.Projects.DetailsAsync(id))!;
        Assert.Null(p.ArchivedAtUtc);Assert.Equal(ProjectStatus.Approved,p.Status);Assert.Null(p.CustomerRejectionReason);
        Assert.Equal("The price is too high",p.Products[0].CustomerRejectionReason);Assert.Equal("Customer declined",p.Products[0].CustomerRejectionComment);
        Assert.Equal(CommercialStatus.CustomerNotApproved,p.Products[0].CommercialStatus);Assert.Null(p.Products[1].CommercialStatus);
        Assert.Single(p.Products[0].Reviews);Assert.Equal(2,await s.Db.ProjectAttachments.CountAsync(x=>x.ProjectId==id));
        Assert.True(await s.Db.AuditLogs.AnyAsync(x=>x.EntityId==p.Products[0].Id && x.FieldName=="ProductCustomerRejected"));
        Assert.Null(await Service(s).FormAsync(id,p.Products[0].Id,default));
        Assert.Null(await CommercialWorkflowTests.Commercial(s).FormAsync(id,p.Products[0].Id,default));
    }
    [Theory][InlineData(false,true)][InlineData(true,false)]
    public async Task Rejection_requires_reason_and_explicit_confirmation(bool reason,bool confirmation){
        await using var s=new ProjectSqlTests.Scope();var id=await Accepted(s);var f=await Form(s,id);
        f.Input.Confirm=confirmation;if(reason)f.Input.ReasonId=f.Reasons.First().Id;
        await Assert.ThrowsAsync<ValidationException>(()=>Service(s).CloseAsync(f.Input,default));
        Assert.Null((await s.Projects.DetailsAsync(id))!.Products[0].CommercialStatus);
    }
    [Fact]
    public async Task Rejection_rejects_foreign_owner_foreign_product_and_inactive_reason(){
        await using var s=new ProjectSqlTests.Scope();await using var other=new ProjectSqlTests.Scope();
        var id=await Accepted(s);var foreign=await Accepted(other);var f=await Form(s,id);
        var reason=new PrivateBrandsPortal.Web.Models.Entities.CustomerRejectionReason{Code="TEST_"+Guid.NewGuid().ToString("N"),Name=s.Login+" reason",IsActive=false};
        s.Db.CustomerRejectionReasons.Add(reason);await s.Db.SaveChangesAsync();f.Input.Confirm=true;f.Input.ReasonId=reason.Id;
        Assert.Null(await Service(other).FormAsync(id,f.Input.ProductId,default));
        await Assert.ThrowsAsync<ValidationException>(()=>Service(other).CloseAsync(f.Input,default));
        await Assert.ThrowsAsync<ValidationException>(()=>Service(s).CloseAsync(f.Input,default));
        f.Input.ReasonId=f.Reasons.First().Id;f.Input.ProductId=(await other.Projects.DetailsAsync(foreign))!.Products[0].Id;
        await Assert.ThrowsAsync<ValidationException>(()=>Service(s).CloseAsync(f.Input,default));
    }
    [Fact]
    public async Task Mixed_terminal_products_archive_without_requiring_all_sales(){
        await using var s=new ProjectSqlTests.Scope();var id=await Accepted(s);
        await CommercialWorkflowTests.Status(s,id,0,CommercialStatus.SalesAndDelivery);
        await Reject(s,id,1);
        var p=(await s.Projects.DetailsAsync(id))!;
        Assert.Equal(CommercialStatus.SalesAndDelivery,p.Products[0].CommercialStatus);
        Assert.Equal(CommercialStatus.CustomerNotApproved,p.Products[1].CommercialStatus);Assert.NotNull(p.ArchivedAtUtc);
        Assert.Equal("Completed — mixed outcome",(await CommercialWorkflowTests.Commercial(s).ArchiveAsync(default)).Single(x=>x.Id==id).Outcome);
    }
    [Fact]
    public async Task Three_products_rejection_changes_only_target_and_last_terminal_archives(){
        await using var s=new ProjectSqlTests.Scope();var id=await Accepted(s,true);
        await CommercialWorkflowTests.Status(s,id,0,CommercialStatus.SalesAndDelivery);
        await CommercialWorkflowTests.Status(s,id,2,CommercialStatus.OfferUnderNegotiation);
        var before=(await s.Projects.DetailsAsync(id))!;
        await Reject(s,id,1);
        var after=(await s.Projects.DetailsAsync(id))!;
        Assert.Null(after.ArchivedAtUtc);
        foreach(var i in new[]{0,2}){
            Assert.Equal(before.Products[i].CommercialStatus,after.Products[i].CommercialStatus);
            Assert.Equal(before.Products[i].UpdatedAtUtc,after.Products[i].UpdatedAtUtc);
            Assert.Equal(before.Products[i].Reviews,after.Products[i].Reviews);
        }
        await CommercialWorkflowTests.Status(s,id,2,CommercialStatus.SalesAndDelivery);
        Assert.NotNull((await s.Projects.DetailsAsync(id))!.ArchivedAtUtc);
    }
    [Fact]
    public async Task All_customer_rejected_products_archive(){
        await using var s=new ProjectSqlTests.Scope();var id=await Accepted(s);
        await Reject(s,id,0);Assert.Null((await s.Projects.DetailsAsync(id))!.ArchivedAtUtc);
        await Reject(s,id,1);Assert.NotNull((await s.Projects.DetailsAsync(id))!.ArchivedAtUtc);
    }
    [Fact]
    public async Task Rejected_product_cannot_reopen_or_repeat_rejection(){
        await using var s=new ProjectSqlTests.Scope();var id=await Accepted(s);await Reject(s,id);
        var p=(await s.Projects.DetailsAsync(id))!;
        await Assert.ThrowsAsync<ValidationException>(()=>CommercialWorkflowTests.Commercial(s).UpdateAsync(new(){ProjectId=id,ProductId=p.Products[0].Id,Version=p.UpdatedAtUtc,Status=CommercialStatus.OfferUnderNegotiation}));
        await Assert.ThrowsAsync<ValidationException>(()=>Service(s).CloseAsync(new(){ProjectId=id,ProductId=p.Products[0].Id,Version=p.UpdatedAtUtc,ReasonId=1,Confirm=true},default));
        Assert.Null((await s.Projects.DetailsAsync(id))!.ArchivedAtUtc);
    }
    [Theory]
    [InlineData("/Implementation/Review")][InlineData("/Implementation/Resubmit")]
    [InlineData("/CustomerClosure/Close")][InlineData("/Dictionaries/Delete")]
    public async Task Mutation_requires_csrf(string path){
        await using var factory=new AuthenticatedFactory();using var client=factory.CreateClient();client.DefaultRequestHeaders.Add("X-Test-User","reader");
        var response=await client.PostAsync(path,new FormUrlEncodedContent([]));
        Assert.Contains(response.StatusCode,new[]{System.Net.HttpStatusCode.BadRequest,System.Net.HttpStatusCode.Forbidden});
    }
}
