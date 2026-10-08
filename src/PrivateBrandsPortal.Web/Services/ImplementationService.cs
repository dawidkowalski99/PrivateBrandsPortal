using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using PrivateBrandsPortal.Web.Data;
using PrivateBrandsPortal.Web.Interfaces;
using PrivateBrandsPortal.Web.Models.Entities;
using PrivateBrandsPortal.Web.Models.Enums;
using PrivateBrandsPortal.Web.ViewModels;
namespace PrivateBrandsPortal.Web.Services;
public sealed class ImplementationService(ApplicationDbContext db,IAppUserService users,TimeProvider clock)
{
    private async Task<AppUser> Reviewer(CancellationToken ct)
    {var user=await users.GetCurrentAsync(ct);if(!user.IsActive || user.Role is not (AppRole.Manager or AppRole.SuperAdmin))throw new PortalAccessException();return user;}
    public static void Audit(ApplicationDbContext db,int productId,string eventName,int actor,DateTimeOffset now,string? comment=null)
    {db.AuditLogs.Add(new(){EntityType=nameof(ProjectProduct),EntityId=productId,FieldName=eventName,NewValue=eventName,Reason=comment,ChangedByUserId=actor,ChangedAtUtc=now,ChangeType=AuditChangeType.Updated});}
    public static void Request(ApplicationDbContext db,int productId,int actor,DateTimeOffset now,bool resubmit)
    {db.ImplementationApprovals.Add(new(){ProjectProductId=productId,RequestedByUserId=actor,RequestedAtUtc=now});Audit(db,productId,resubmit?"ImplementationApprovalResubmitted":"ImplementationApprovalRequested",actor,now);}
    public async Task<IReadOnlyList<ImplementationQueueItem>> QueueAsync(CancellationToken ct)
    {
        var user=await Reviewer(ct);
        return await db.ImplementationApprovals.AsNoTracking().Where(x=>x.Status==ImplementationApprovalStatus.Pending &&
            x.ProjectProduct.CommercialStatus==CommercialStatus.ImplementationIntoProduction && x.ProjectProduct.Project.ArchivedAtUtc==null && (user.Role==AppRole.SuperAdmin || x.ProjectProduct.Project.ProjectManagerId!=user.Id))
            .OrderBy(x=>x.RequestedAtUtc).Select(x=>new ImplementationQueueItem(x.Id,x.ProjectProduct.ProjectId,x.ProjectProduct.Project.ProjectNumber,
                x.ProjectProduct.Project.Customer,x.ProjectProduct.Project.ProjectManager.DisplayName,
                x.ProjectProduct.ProductCategory==null?null:x.ProjectProduct.ProductCategory.Name,x.ProjectProduct.Subcategory,x.ProjectProduct.SKU,x.RequestedAtUtc,
                db.ProjectAttachments.Any(a=>a.ProjectId==x.ProjectProduct.ProjectId && a.AttachmentType==AttachmentType.Offer && a.ProjectProductId==null && a.DeletedAtUtc==null),
                db.ProjectAttachments.Any(a=>a.ProjectId==x.ProjectProduct.ProjectId && a.AttachmentType==AttachmentType.Calculation && (a.ProjectProductId==null||a.ProjectProductId==x.ProjectProductId) && a.DeletedAtUtc==null))).ToListAsync(ct);
    }
    public async Task<ImplementationReviewModel?> ReviewAsync(int id,CancellationToken ct)
    {
        var user=await Reviewer(ct);
        var approval=await db.ImplementationApprovals.AsNoTracking().Include(x=>x.ProjectProduct).SingleOrDefaultAsync(x=>x.Id==id,ct);
        if(approval is null)return null;
        var project=await ProjectDetailsReader.ReadAsync(db,db.Projects.Where(x=>x.Id==approval.ProjectProduct.ProjectId),ct);
        if(project is null)return null;
        var docs=await db.ProjectAttachments.AsNoTracking().Where(x=>x.ProjectId==project.Id && x.DeletedAtUtc==null &&
            ((x.AttachmentType==AttachmentType.Offer && x.ProjectProductId==null) || (x.AttachmentType==AttachmentType.Calculation && (x.ProjectProductId==null||x.ProjectProductId==approval.ProjectProductId))))
            .Select(x=>new AttachmentItem(x.Id,x.AttachmentType,x.OriginalFileName,x.FileSize,x.UploadedByUser.DisplayName,x.UploadedAtUtc,x.ProjectProduct==null?null:x.ProjectProduct.SKU,x.Description,null,null,null)).ToListAsync(ct);
        return new(){Project=project,Product=project.Products.Single(x=>x.Id==approval.ProjectProductId),Documents=docs,
            Input=new(){Id=id,Version=project.UpdatedAtUtc},CanDecide=(user.Role==AppRole.SuperAdmin || project.ProjectManagerId!=user.Id) && project.ArchivedAtUtc==null && approval.Status==ImplementationApprovalStatus.Pending && approval.ProjectProduct.CommercialStatus==CommercialStatus.ImplementationIntoProduction};
    }
    public async Task DecideAsync(ImplementationDecisionInput input,CancellationToken ct)
    {
        Validator.ValidateObject(input,new ValidationContext(input),true);var user=await Reviewer(ct);
        if(input.Decision is not (ImplementationApprovalStatus.Approved or ImplementationApprovalStatus.Rejected))throw new ValidationException("Choose Approve or Reject.");
        var comment=input.Comment?.Trim();if(input.Decision==ImplementationApprovalStatus.Rejected && string.IsNullOrWhiteSpace(comment))throw new ValidationException("A rejection comment is required.");
        var projectId=await db.ImplementationApprovals.Where(x=>x.Id==input.Id).Select(x=>(int?)x.ProjectProduct.ProjectId).SingleOrDefaultAsync(ct);
        if(projectId is null)throw new PortalAccessException();
        await using var tx=await db.Database.BeginTransactionAsync(ct);var now=clock.GetUtcNow();if(now<=input.Version)now=input.Version!.Value.AddTicks(1);
        if(await db.Projects.Where(x=>x.Id==projectId && (user.Role==AppRole.SuperAdmin || x.ProjectManagerId!=user.Id) && x.ArchivedAtUtc==null && x.UpdatedAtUtc==input.Version)
            .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.UpdatedAtUtc,now),ct)!=1)throw new ValidationException("Project changed, closed, or belongs to you. Reopen review.");
        var approval=await db.ImplementationApprovals.Include(x=>x.ProjectProduct).SingleAsync(x=>x.Id==input.Id,ct);await db.Entry(approval).ReloadAsync(ct);await db.Entry(approval.ProjectProduct).ReloadAsync(ct);
        if(approval.Status!=ImplementationApprovalStatus.Pending || approval.ProjectProduct.CommercialStatus!=CommercialStatus.ImplementationIntoProduction)throw new ValidationException("This request is no longer pending.");
        if(input.Decision==ImplementationApprovalStatus.Approved)await AttachmentService.RequireSalesDocumentsAsync(db,projectId.Value,approval.ProjectProductId,ct);
        approval.Status=input.Decision.Value;approval.ReviewerId=user.Id;approval.ReviewedAtUtc=now;approval.Comment=comment;
        Audit(db,approval.ProjectProductId,input.Decision==ImplementationApprovalStatus.Approved?"ImplementationApproved":"ImplementationRejected",user.Id,now,comment);
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
    public async Task ResubmitAsync(int projectId,int productId,DateTimeOffset version,CancellationToken ct)
    {
        var user=await users.GetCurrentAsync(ct);if(!user.IsActive)throw new PortalAccessException();
        await using var tx=await db.Database.BeginTransactionAsync(ct);var now=clock.GetUtcNow();if(now<=version)now=version.AddTicks(1);
        if(await db.Projects.Where(x=>x.Id==projectId && x.ProjectManagerId==user.Id && x.ArchivedAtUtc==null && x.UpdatedAtUtc==version)
            .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.UpdatedAtUtc,now),ct)!=1)throw new ValidationException("Project changed or is unavailable.");
        var product=await db.ProjectProducts.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==productId && x.ProjectId==projectId,ct);
        if(product?.CommercialStatus!=CommercialStatus.ImplementationIntoProduction)throw new ValidationException("Product is not in Implementation.");
        var last=await db.ImplementationApprovals.Where(x=>x.ProjectProductId==productId).OrderByDescending(x=>x.Id).Select(x=>(ImplementationApprovalStatus?)x.Status).FirstOrDefaultAsync(ct);
        if(last is not (null or ImplementationApprovalStatus.Rejected))throw new ValidationException("Only rejected or historical requests can be submitted.");
        await AttachmentService.RequireSalesDocumentsAsync(db,projectId,productId,ct);Request(db,productId,user.Id,now,last is not null);
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
}
