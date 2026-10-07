using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using PrivateBrandsPortal.Web.Data;
using PrivateBrandsPortal.Web.Interfaces;
using PrivateBrandsPortal.Web.Models.Entities;
using PrivateBrandsPortal.Web.Models.Enums;
using PrivateBrandsPortal.Web.ViewModels;
namespace PrivateBrandsPortal.Web.Services;
public sealed class CustomerClosureService(ApplicationDbContext db,IAppUserService users,TimeProvider clock)
{
    public async Task<CustomerClosureModel?> FormAsync(int id,CancellationToken ct)
    {
        var actor=await users.GetCurrentAsync(ct);if(!actor.IsActive)throw new PortalAccessException();
        var p=await db.Projects.AsNoTracking().Where(x=>x.Id==id && x.ProjectManagerId==actor.Id && x.ArchivedAtUtc==null &&
            x.Products.Any(p=>(p.ReviewStatus==ProductReviewStatus.Approved || p.ReviewStatus==ProductReviewStatus.EditedAndApproved)&&p.CommercialStatus!=CommercialStatus.SalesAndDelivery))
            .SingleOrDefaultAsync(ct);
        if(p is null)return null;
        return new(){ProjectNumber=p.ProjectNumber,Input=new(){ProjectId=id,Version=p.UpdatedAtUtc},Reasons=await db.CustomerRejectionReasons.AsNoTracking().Where(x=>x.IsActive).OrderBy(x=>x.DisplayOrder).ThenBy(x=>x.Name).Select(x=>new LookupItem(x.Id,x.Name)).ToListAsync(ct)};
    }
    public async Task<string> CloseAsync(CustomerClosureInput input,CancellationToken ct)
    {
        Validator.ValidateObject(input,new ValidationContext(input),true);var actor=await users.GetCurrentAsync(ct);if(!actor.IsActive)throw new PortalAccessException();
        await using var tx=await db.Database.BeginTransactionAsync(ct);var now=clock.GetUtcNow();if(now<=input.Version)now=input.Version!.Value.AddTicks(1);
        if(await db.Projects.Where(x=>x.Id==input.ProjectId && x.ProjectManagerId==actor.Id && x.ArchivedAtUtc==null && x.UpdatedAtUtc==input.Version)
            .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.UpdatedAtUtc,now),ct)!=1)throw new ValidationException("Project changed or is unavailable.");
        var reason=await db.CustomerRejectionReasons.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==input.ReasonId && x.IsActive,ct)??throw new ValidationException("Select an active customer rejection reason.");
        var project=await db.Projects.Include(x=>x.Products).SingleAsync(x=>x.Id==input.ProjectId,ct);await db.Entry(project).ReloadAsync(ct);
        foreach(var product in project.Products)await db.Entry(product).ReloadAsync(ct);
        var active=project.Products.Where(x=>x.ReviewStatus is ProductReviewStatus.Approved or ProductReviewStatus.EditedAndApproved && x.CommercialStatus!=CommercialStatus.SalesAndDelivery).ToList();
        if(active.Count==0)throw new ValidationException("No active commercial products are available to close.");
        foreach(var p in active){db.AuditLogs.Add(new(){EntityType=nameof(ProjectProduct),EntityId=p.Id,FieldName=nameof(p.CommercialStatus),OldValue=p.CommercialStatus?.ToString(),NewValue=CommercialStatus.CustomerNotApproved.ToString(),ChangedByUserId=actor.Id,ChangedAtUtc=now,ChangeType=AuditChangeType.Updated});p.CommercialStatus=CommercialStatus.CustomerNotApproved;p.UpdatedAtUtc=now;}
        project.CustomerRejectionReasonId=reason.Id;project.CustomerRejectionReasonName=reason.Name;project.CustomerRejectedByUserId=actor.Id;project.CustomerRejectedAtUtc=now;project.CustomerRejectionComment=input.Comment?.Trim();project.ArchivedAtUtc=now;project.Status=ProjectStatus.Cancelled;project.UpdatedAtUtc=now;
        db.AuditLogs.Add(new(){EntityType=nameof(Project),EntityId=project.Id,FieldName="ProjectCustomerRejected",NewValue=System.Text.Json.JsonSerializer.Serialize(new{reason.Code,reason.Name,Comment=project.CustomerRejectionComment}),ChangedByUserId=actor.Id,ChangedAtUtc=now,ChangeType=AuditChangeType.Updated});
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return reason.Name;
    }
}
