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
    public async Task<CustomerClosureModel?> FormAsync(int id,int productId,CancellationToken ct)
    {
        var actor=await users.GetCurrentAsync(ct);if(!actor.IsActive)throw new PortalAccessException();
        var p=await db.ProjectProducts.AsNoTracking().Where(p=>p.Id==productId && p.ProjectId==id && p.Project.ProjectManagerId==actor.Id &&
            p.Project.ArchivedAtUtc==null && (p.ReviewStatus==ProductReviewStatus.Approved || p.ReviewStatus==ProductReviewStatus.EditedAndApproved) &&
            p.CommercialStatus!=CommercialStatus.SalesAndDelivery && p.CommercialStatus!=CommercialStatus.CustomerNotApproved)
            .Select(p=>new {p.SKU,p.Project.ProjectNumber,p.Project.UpdatedAtUtc}).SingleOrDefaultAsync(ct);
        if(p is null)return null;
        return new(){ProjectNumber=p.ProjectNumber,SKU=p.SKU,Input=new(){ProjectId=id,ProductId=productId,Version=p.UpdatedAtUtc},
            Reasons=await db.CustomerRejectionReasons.AsNoTracking().Where(x=>x.IsActive).OrderBy(x=>x.DisplayOrder).ThenBy(x=>x.Name).Select(x=>new LookupItem(x.Id,x.Name)).ToListAsync(ct)};
    }
    public async Task<CustomerClosureResult> CloseAsync(CustomerClosureInput input,CancellationToken ct)
    {
        Validator.ValidateObject(input,new ValidationContext(input),true);
        var actor=await users.GetCurrentAsync(ct);if(!actor.IsActive)throw new PortalAccessException();
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        var now=clock.GetUtcNow();if(now<=input.Version)now=input.Version!.Value.AddTicks(1);
        if(await db.Projects.Where(x=>x.Id==input.ProjectId && x.ProjectManagerId==actor.Id && x.ArchivedAtUtc==null && x.UpdatedAtUtc==input.Version)
            .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.UpdatedAtUtc,now),ct)!=1)throw new ValidationException("Project changed or is unavailable.");
        var reason=await db.CustomerRejectionReasons.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==input.ReasonId && x.IsActive,ct)
            ??throw new ValidationException("Select an active customer rejection reason.");
        var product=await db.ProjectProducts.SingleOrDefaultAsync(x=>x.Id==input.ProductId && x.ProjectId==input.ProjectId,ct)
            ??throw new ValidationException("Product is unavailable in this project.");
        await db.Entry(product).ReloadAsync(ct);
        if(product.ReviewStatus is not (ProductReviewStatus.Approved or ProductReviewStatus.EditedAndApproved) ||
            product.CommercialStatus is CommercialStatus.SalesAndDelivery or CommercialStatus.CustomerNotApproved)
            throw new ValidationException("Only accepted, unfinished products can be closed.");
        db.AuditLogs.Add(new(){EntityType=nameof(ProjectProduct),EntityId=product.Id,FieldName=nameof(product.CommercialStatus),
            OldValue=product.CommercialStatus?.ToString(),NewValue=CommercialStatus.CustomerNotApproved.ToString(),
            ChangedByUserId=actor.Id,ChangedAtUtc=now,ChangeType=AuditChangeType.Updated});
        product.CommercialStatus=CommercialStatus.CustomerNotApproved;
        product.CustomerRejectionReasonId=reason.Id;product.CustomerRejectionReasonName=reason.Name;
        product.CustomerRejectionComment=input.Comment?.Trim();product.CustomerRejectedByUserId=actor.Id;
        product.CustomerRejectedAtUtc=now;product.UpdatedAtUtc=now;
        db.AuditLogs.Add(new(){EntityType=nameof(ProjectProduct),EntityId=product.Id,FieldName="ProductCustomerRejected",
            NewValue=System.Text.Json.JsonSerializer.Serialize(new{reason.Code,reason.Name,Comment=product.CustomerRejectionComment}),
            ChangedByUserId=actor.Id,ChangedAtUtc=now,ChangeType=AuditChangeType.Updated});
        await db.SaveChangesAsync(ct);
        var archived=await CommercialService.ArchiveIfCompleteAsync(db,input.ProjectId,now,ct)>0;
        await tx.CommitAsync(ct);return new(reason.Name,archived);
    }
}
