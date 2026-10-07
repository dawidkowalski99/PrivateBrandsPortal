using Microsoft.EntityFrameworkCore;
using PrivateBrandsPortal.Web.Models.Enums;
using PrivateBrandsPortal.Web.Services;
using PrivateBrandsPortal.Web.ViewModels;
namespace PrivateBrandsPortal.Tests;

[Collection("SQL integration")]
public sealed class ManagerOwnedProjectTests
{
    private static async Task Role(ProjectSqlTests.Scope s,AppRole role)
    {var user=await s.Users.GetCurrentAsync();await s.Db.AppUsers.Where(x=>x.Id==user.Id).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.Role,role));s.Db.ChangeTracker.Clear();}
    [Theory][InlineData(AppRole.Manager)][InlineData(AppRole.SuperAdmin)]
    public async Task Authorized_creator_finalizes_without_review_and_can_run_commercial_workflow(AppRole role)
    {
        await using var s=new ProjectSqlTests.Scope();await Role(s,role);var actor=await s.Users.GetCurrentAsync();
        var id=await s.Projects.SaveDraftAsync(WizardTests.ValidDraft());var project=await s.Db.Projects.AsNoTracking().SingleAsync(x=>x.Id==id);
        Assert.Equal(actor.Id,project.CreatedByUserId);Assert.False(project.RequiresManagerApproval);Assert.Equal(ProjectStatus.Draft,project.Status);
        var edit=(await s.Projects.LoadDraftAsync(id))!;edit.Products[0].Quantity=23456;await s.Projects.SaveDraftAsync(edit);
        var review=CommercialWorkflowTests.Review(s);await review.SubmitAsync(id,(await s.Projects.DetailsAsync(id))!.UpdatedAtUtc);
        var details=(await s.Projects.DetailsAsync(id))!;Assert.Equal(ProjectStatus.Approved,details.Status);
        Assert.All(details.Products,x=>{Assert.Equal(ProductReviewStatus.Approved,x.ReviewStatus);Assert.Null(x.CommercialStatus);Assert.Empty(x.Reviews);});
        Assert.DoesNotContain(await review.QueueAsync(),x=>x.Id==id);Assert.Null(await review.FormAsync(id,details.Products[0].Id,ReviewDecision.Approved));
        var audit=await s.Db.AuditLogs.SingleAsync(x=>x.EntityType=="Project" && x.EntityId==id && x.ChangeType==AuditChangeType.ManagerApprovalBypassed);
        Assert.Equal("Project created by authorized Manager; separate manager approval not required.",audit.Reason);
        await CommercialWorkflowTests.Status(s,id,0,CommercialStatus.PriceOfferSubmitted);
        Assert.Equal(CommercialStatus.PriceOfferSubmitted,(await s.Projects.DetailsAsync(id))!.Products[0].CommercialStatus);
    }
    [Theory][InlineData(AppRole.ProjectManager,AppRole.Manager,true)][InlineData(AppRole.Manager,AppRole.ProjectManager,false)]
    public async Task Creation_flag_does_not_change_after_role_change(AppRole creator,AppRole later,bool required)
    {
        await using var s=new ProjectSqlTests.Scope();await Role(s,creator);var id=await s.Projects.SaveDraftAsync(WizardTests.ValidDraft());
        await Role(s,later);await CommercialWorkflowTests.Review(s).SubmitAsync(id,(await s.Projects.DetailsAsync(id))!.UpdatedAtUtc);
        var details=(await s.Projects.DetailsAsync(id))!;Assert.Equal(required,details.RequiresManagerApproval);
        Assert.Equal(required?ProjectStatus.AwaitingManagerReview:ProjectStatus.Approved,details.Status);
    }
    [Theory][InlineData(AppRole.ProjectManager,AppRole.Manager,true)][InlineData(AppRole.Manager,AppRole.ProjectManager,false)]
    public async Task Transfer_preserves_creator_mode_and_files_but_revokes_old_owner(AppRole creator,AppRole targetRole,bool required)
    {
        await using var owner=new ProjectSqlTests.Scope();await using var target=new ProjectSqlTests.Scope();await using var admin=new ProjectSqlTests.Scope();
        using var files=new AttachmentTests.Files();await Role(owner,creator);await Role(target,targetRole);await Role(admin,AppRole.SuperAdmin);
        var original=await owner.Users.GetCurrentAsync();var next=await target.Users.GetCurrentAsync();
        var id=await owner.Projects.SaveDraftAsync(WizardTests.ValidDraft());
        await files.Service(owner).UploadAsync(new(){ProjectId=id,Type=AttachmentType.Offer,File=AttachmentTests.File()},default);
        var attachment=await owner.Db.ProjectAttachments.AsNoTracking().SingleAsync(x=>x.ProjectId==id);
        var transfer=new ProjectTransferService(admin.Db,admin.Users,new PermissionService(admin.Users),TimeProvider.System);
        var form=(await transfer.FormAsync(id,default))!;Assert.Contains(form.ProjectManagers,x=>x.Id==next.Id);
        form.Input.NewProjectManagerId=next.Id;form.Input.Reason="Test ownership handover";await transfer.TransferAsync(form.Input);
        var project=await target.Db.Projects.AsNoTracking().SingleAsync(x=>x.Id==id);
        Assert.Equal(original.Id,project.CreatedByUserId);Assert.Equal(next.Id,project.ProjectManagerId);Assert.Equal(required,project.RequiresManagerApproval);
        var after=await target.Db.ProjectAttachments.AsNoTracking().SingleAsync(x=>x.Id==attachment.Id);
        Assert.Equal(attachment.StorageKey,after.StorageKey);Assert.Equal(original.Id,after.UploadedByUserId);
        await Assert.ThrowsAsync<PortalAccessException>(()=>files.Service(owner).DownloadAsync(attachment.Id,default));
        var download=await files.Service(target).DownloadAsync(attachment.Id,default);await download.Stream.DisposeAsync();
        Assert.Null(await owner.Projects.LoadDraftAsync(id));Assert.NotNull(await target.Projects.LoadDraftAsync(id));
        await CommercialWorkflowTests.Review(target).SubmitAsync(id,(await target.Projects.DetailsAsync(id))!.UpdatedAtUtc);
        Assert.Equal(required?ProjectStatus.AwaitingManagerReview:ProjectStatus.Approved,(await target.Projects.DetailsAsync(id))!.Status);
    }
}
