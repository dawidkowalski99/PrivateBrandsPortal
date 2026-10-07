using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using PrivateBrandsPortal.Web.Models.Entities;
using PrivateBrandsPortal.Web.Models.Enums;
using PrivateBrandsPortal.Web.Services;
using PrivateBrandsPortal.Web.ViewModels;
namespace PrivateBrandsPortal.Tests;

[Collection("SQL integration")]
public sealed class ImplementationWorkflowTests
{
    private static ImplementationService Service(ProjectSqlTests.Scope s)=>new(s.Db,s.Users,TimeProvider.System);
    private static async Task<int> Accepted(ProjectSqlTests.Scope s){
        var id=await CommercialWorkflowTests.Submitted(s);
        await CommercialWorkflowTests.Decide(s,id,0,ReviewDecision.Approved);return id;
    }
    private static async Task<ImplementationDecisionInput> Request(ProjectSqlTests.Scope s,int id){
        await CommercialWorkflowTests.Status(s,id,0,CommercialStatus.ImplementationIntoProduction);
        var p=(await s.Projects.DetailsAsync(id))!;
        return new(){Id=await s.Db.ImplementationApprovals.Where(a=>a.ProjectProductId==p.Products[0].Id).Select(a=>a.Id).SingleAsync(),Version=p.UpdatedAtUtc,Decision=ImplementationApprovalStatus.Approved};
    }
    private static async Task SetRole(ProjectSqlTests.Scope s,AppRole role){
        var actor=await s.Users.GetCurrentAsync();await s.Db.AppUsers.Where(x=>x.Id==actor.Id).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.Role,role));
    }
    [Theory]
    [InlineData(false,false)]
    [InlineData(true,false)]
    [InlineData(false,true)]
    public async Task Implementation_requires_both_documents_atomically(bool offer,bool calculation){
        await using var s=new ProjectSqlTests.Scope();var id=await Accepted(s);
        await CommercialWorkflowTests.SupplyDocuments(s,id);
        await s.Db.ProjectAttachments.Where(x=>x.ProjectId==id && ((x.AttachmentType==AttachmentType.Offer && !offer)||(x.AttachmentType==AttachmentType.Calculation && !calculation))).ExecuteDeleteAsync();
        var p=(await s.Projects.DetailsAsync(id))!;
        await Assert.ThrowsAsync<ValidationException>(()=>CommercialWorkflowTests.Commercial(s).UpdateAsync(new(){ProjectId=id,ProductId=p.Products[0].Id,Version=p.UpdatedAtUtc,Status=CommercialStatus.ImplementationIntoProduction}));
        Assert.Empty(await s.Db.ImplementationApprovals.Where(x=>x.ProjectProduct.ProjectId==id).ToListAsync());
        Assert.Equal(p.UpdatedAtUtc,(await s.Projects.DetailsAsync(id))!.UpdatedAtUtc);
    }
    [Theory]
    [InlineData(AppRole.Manager)]
    [InlineData(AppRole.SuperAdmin)]
    public async Task Reviewer_approves_and_sales_requires_latest_approval(AppRole role){
        await using var s=new ProjectSqlTests.Scope();await using var reviewer=new ProjectSqlTests.Scope();
        var id=await Accepted(s);var input=await Request(s,id);await SetRole(reviewer,role);
        var p=(await s.Projects.DetailsAsync(id))!;
        var sales=new CommercialInput{ProjectId=id,ProductId=p.Products[0].Id,Version=p.UpdatedAtUtc,Status=CommercialStatus.SalesAndDelivery};
        await Assert.ThrowsAsync<ValidationException>(()=>CommercialWorkflowTests.Commercial(s).UpdateAsync(sales));
        Assert.Contains(await Service(reviewer).QueueAsync(default),x=>x.Id==input.Id);
        await Service(reviewer).DecideAsync(input,default);
        sales.Version=(await s.Projects.DetailsAsync(id))!.UpdatedAtUtc;
        await CommercialWorkflowTests.Commercial(s).UpdateAsync(sales);
        Assert.Equal(CommercialStatus.SalesAndDelivery,(await s.Projects.DetailsAsync(id))!.Products[0].CommercialStatus);
        Assert.True(await s.Db.AuditLogs.AnyAsync(x=>x.EntityId==sales.ProductId && x.FieldName=="ImplementationApproved"));
    }
    [Theory]
    [InlineData(AppRole.Manager)]
    [InlineData(AppRole.SuperAdmin)]
    public async Task Own_project_cannot_be_approved_even_by_superadmin(AppRole role){
        await using var s=new ProjectSqlTests.Scope();var id=await Accepted(s);var input=await Request(s,id);await SetRole(s,role);
        await Assert.ThrowsAsync<ValidationException>(()=>Service(s).DecideAsync(input,default));
        Assert.DoesNotContain(await Service(s).QueueAsync(default),x=>x.Id==input.Id);
        Assert.False((await Service(s).ReviewAsync(input.Id,default))!.CanDecide);
    }
    [Fact]
    public async Task Project_manager_cannot_review_and_foreign_owner_cannot_resubmit(){
        await using var s=new ProjectSqlTests.Scope();await using var other=new ProjectSqlTests.Scope();
        var id=await Accepted(s);var input=await Request(s,id);
        await Assert.ThrowsAsync<PortalAccessException>(()=>Service(other).DecideAsync(input,default));
        await Assert.ThrowsAsync<PortalAccessException>(()=>Service(other).QueueAsync(default));
        var p=(await s.Projects.DetailsAsync(id))!;
        await Assert.ThrowsAsync<ValidationException>(()=>Service(other).ResubmitAsync(id,p.Products[0].Id,p.UpdatedAtUtc,default));
    }
    [Fact]
    public async Task Rejection_requires_comment_and_resubmission_preserves_history(){
        await using var s=new ProjectSqlTests.Scope();await using var reviewer=new ProjectSqlTests.Scope();
        var id=await Accepted(s);var input=await Request(s,id);await SetRole(reviewer,AppRole.Manager);
        input.Decision=ImplementationApprovalStatus.Rejected;input.Comment=" ";
        await Assert.ThrowsAsync<ValidationException>(()=>Service(reviewer).DecideAsync(input,default));
        input.Comment="Correct calculation";await Service(reviewer).DecideAsync(input,default);
        var p=(await s.Projects.DetailsAsync(id))!;var product=p.Products[0];
        Assert.Equal(CommercialStatus.ImplementationIntoProduction,product.CommercialStatus);
        await Assert.ThrowsAsync<ValidationException>(()=>CommercialWorkflowTests.Commercial(s).UpdateAsync(new(){ProjectId=id,ProductId=product.Id,Version=p.UpdatedAtUtc,Status=CommercialStatus.SalesAndDelivery}));
        await Service(s).ResubmitAsync(id,product.Id,p.UpdatedAtUtc,default);
        var history=await s.Db.ImplementationApprovals.AsNoTracking().Where(x=>x.ProjectProductId==product.Id).OrderBy(x=>x.Id).ToListAsync();
        Assert.Equal(2,history.Count);Assert.Equal("Correct calculation",history[0].Comment);Assert.Equal(ImplementationApprovalStatus.Pending,history[1].Status);
        var latest=(await s.Projects.DetailsAsync(id))!;await Assert.ThrowsAsync<ValidationException>(()=>Service(s).ResubmitAsync(id,product.Id,latest.UpdatedAtUtc,default));
    }
    [Fact]
    public async Task Removed_document_after_approval_blocks_delivery(){
        await using var s=new ProjectSqlTests.Scope();var id=await Accepted(s);await Request(s,id);
        var p=(await s.Projects.DetailsAsync(id))!;await CommercialWorkflowTests.ApproveImplementation(s,id,p.Products[0].Id);
        var actor=(await s.Users.GetCurrentAsync()).Id;await s.Db.ProjectAttachments.Where(x=>x.ProjectId==id && x.AttachmentType==AttachmentType.Offer).ExecuteUpdateAsync(x=>x.SetProperty(a=>a.DeletedAtUtc,DateTimeOffset.UtcNow).SetProperty(a=>a.DeletedByUserId,actor));
        var latest=(await s.Projects.DetailsAsync(id))!;await Assert.ThrowsAsync<ValidationException>(()=>CommercialWorkflowTests.Commercial(s).UpdateAsync(new(){ProjectId=id,ProductId=p.Products[0].Id,Version=latest.UpdatedAtUtc,Status=CommercialStatus.SalesAndDelivery}));
    }
    [Fact]
    public async Task Stale_decision_does_not_overwrite_accepted_review(){
        await using var s=new ProjectSqlTests.Scope();await using var reviewer=new ProjectSqlTests.Scope();
        var id=await Accepted(s);var input=await Request(s,id);await SetRole(reviewer,AppRole.Manager);await Service(reviewer).DecideAsync(input,default);
        input.Decision=ImplementationApprovalStatus.Rejected;input.Comment="Stale rejection";
        await Assert.ThrowsAsync<ValidationException>(()=>Service(reviewer).DecideAsync(input,default));
        Assert.Equal(ImplementationApprovalStatus.Approved,await s.Db.ImplementationApprovals.Where(x=>x.Id==input.Id).Select(x=>x.Status).SingleAsync());
    }
    private sealed class AuditFailure : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData data,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,CancellationToken ct=default)
        {
            if(data.Context!.ChangeTracker.Entries<AuditLog>().Any(x=>x.State==EntityState.Added))
                throw new InvalidOperationException("Deliberate implementation audit failure.");
            return base.SavingChangesAsync(data,result,ct);
        }
    }
    [Fact]
    public async Task Approval_audit_failure_rolls_back_decision_and_project_version(){
        await using var s=new ProjectSqlTests.Scope();await using var reviewer=new ProjectSqlTests.Scope();
        var id=await Accepted(s);var input=await Request(s,id);await SetRole(reviewer,AppRole.Manager);
        await using var failing=reviewer.NewContext(new AuditFailure());
        var service=new ImplementationService(failing,reviewer.MakeUsers(failing),TimeProvider.System);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.DecideAsync(input,default));
        Assert.Equal(ImplementationApprovalStatus.Pending,await s.Db.ImplementationApprovals.Where(x=>x.Id==input.Id).Select(x=>x.Status).SingleAsync());
        Assert.Equal(input.Version,(await s.Projects.DetailsAsync(id))!.UpdatedAtUtc);
    }
    [Fact]
    public async Task Implementation_reviewer_can_download_only_applicable_documents(){
        await using var s=new ProjectSqlTests.Scope();await using var reviewer=new ProjectSqlTests.Scope();using var files=new AttachmentTests.Files();
        var id=await Accepted(s);await SetRole(reviewer,AppRole.Manager);
        var p=(await s.Projects.DetailsAsync(id))!;
        await files.Service(s).UploadAsync(new(){ProjectId=id,Type=AttachmentType.Offer,File=AttachmentTests.File()},default);
        await files.Service(s).UploadAsync(new(){ProjectId=id,Type=AttachmentType.Calculation,ProjectProductId=p.Products[0].Id,File=AttachmentTests.File()},default);
        await files.Service(s).UploadAsync(new(){ProjectId=id,Type=AttachmentType.Calculation,ProjectProductId=p.Products[1].Id,File=AttachmentTests.File()},default);
        var documents=await s.Db.ProjectAttachments.AsNoTracking().Where(x=>x.ProjectId==id).ToListAsync();
        foreach(var document in documents)await Assert.ThrowsAsync<PortalAccessException>(()=>files.Service(reviewer).DownloadAsync(document.Id,default));
        await CommercialWorkflowTests.Commercial(s).UpdateAsync(new(){ProjectId=id,ProductId=p.Products[0].Id,Version=(await s.Projects.DetailsAsync(id))!.UpdatedAtUtc,Status=CommercialStatus.ImplementationIntoProduction});
        foreach(var document in documents){
            if(document.ProjectProductId==p.Products[1].Id)await Assert.ThrowsAsync<PortalAccessException>(()=>files.Service(reviewer).DownloadAsync(document.Id,default));
            else {var result=await files.Service(reviewer).DownloadAsync(document.Id,default);await result.Stream.DisposeAsync();}
        }
    }
}
