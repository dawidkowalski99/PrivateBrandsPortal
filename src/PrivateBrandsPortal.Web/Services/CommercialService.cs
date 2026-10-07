using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using PrivateBrandsPortal.Web.Data;
using PrivateBrandsPortal.Web.Interfaces;
using PrivateBrandsPortal.Web.Models.Entities;
using PrivateBrandsPortal.Web.Models.Enums;
using PrivateBrandsPortal.Web.ViewModels;
namespace PrivateBrandsPortal.Web.Services;

public sealed class CommercialService(ApplicationDbContext db, IAppUserService users, TimeProvider clock)
{
    private async Task<int> Owner(CancellationToken ct)
    {
        var user = await users.GetCurrentAsync(ct);
        if (!WorkflowAccess.Allows(user, AppRole.ProjectManager)) throw new PortalAccessException();
        return user.Id;
    }
    public async Task<CommercialForm?> FormAsync(int projectId, int productId, CancellationToken ct)
    {
        var owner = await Owner(ct);
        return await db.ProjectProducts.AsNoTracking().Where(p => p.Id == productId && p.ProjectId == projectId && p.Project.ProjectManagerId == owner &&
            (p.ReviewStatus == ProductReviewStatus.Approved || p.ReviewStatus == ProductReviewStatus.EditedAndApproved) && p.CommercialStatus != CommercialStatus.SalesAndDelivery)
            .Select(p => new CommercialForm { ProjectNumber=p.Project.ProjectNumber, SKU=p.SKU, Input=new CommercialInput {
                ProjectId=projectId, ProductId=productId, Version=p.Project.UpdatedAtUtc, Status=p.CommercialStatus } }).SingleOrDefaultAsync(ct);
    }
    public async Task UpdateAsync(CommercialInput input, CancellationToken ct = default)
    {
        Validator.ValidateObject(input,new ValidationContext(input),true);
        var owner = await Owner(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now=clock.GetUtcNow(); if(now<=input.Version) now=input.Version!.Value.AddTicks(1);
        var changed=await db.Projects.Where(p=>p.Id==input.ProjectId && p.ProjectManagerId==owner && p.UpdatedAtUtc==input.Version && p.ArchivedAtUtc==null)
            .ExecuteUpdateAsync(s=>s.SetProperty(p=>p.UpdatedAtUtc,now),ct);
        if(changed!=1) throw new ValidationException("Project changed or is unavailable. Reopen its details.");
        var product=await db.ProjectProducts.SingleOrDefaultAsync(p=>p.Id==input.ProductId && p.ProjectId==input.ProjectId,ct);
        if(product is null) throw new ValidationException("Product unavailable.");
        await db.Entry(product).ReloadAsync(ct);
        if(product.ReviewStatus is not (ProductReviewStatus.Approved or ProductReviewStatus.EditedAndApproved) || product.CommercialStatus==CommercialStatus.SalesAndDelivery)
            throw new ValidationException("Only accepted, unfinished products can be updated.");
        if(input.Status==CommercialStatus.SalesAndDelivery) await AttachmentService.RequireSalesDocumentsAsync(db,input.ProjectId,input.ProductId,ct);
        if(product.CommercialStatus==input.Status) { await tx.RollbackAsync(ct); return; }
        db.AuditLogs.Add(new AuditLog { EntityType=nameof(ProjectProduct),EntityId=product.Id,FieldName=nameof(ProjectProduct.CommercialStatus),
            OldValue=product.CommercialStatus?.ToString(),NewValue=input.Status!.Value.ToString(),ChangedByUserId=owner,ChangedAtUtc=now,ChangeType=AuditChangeType.Updated });
        product.CommercialStatus=input.Status; product.UpdatedAtUtc=now;
        await db.SaveChangesAsync(ct);
        await ArchiveIfCompleteAsync(db,input.ProjectId,now,ct);
        await tx.CommitAsync(ct);
    }
    public static Task<int> ArchiveIfCompleteAsync(ApplicationDbContext db,int id,DateTimeOffset now,CancellationToken ct) =>
        db.Projects.Where(p=>p.Id==id && p.ArchivedAtUtc==null &&
            p.Products.Any(x=>x.ReviewStatus==ProductReviewStatus.Approved || x.ReviewStatus==ProductReviewStatus.EditedAndApproved) &&
            p.Products.All(x=>x.ReviewStatus==ProductReviewStatus.Rejected ||
                ((x.ReviewStatus==ProductReviewStatus.Approved || x.ReviewStatus==ProductReviewStatus.EditedAndApproved) && x.CommercialStatus==CommercialStatus.SalesAndDelivery)))
            .ExecuteUpdateAsync(s=>s.SetProperty(p=>p.ArchivedAtUtc,now),ct);
    public async Task<IReadOnlyList<ArchiveItem>> ArchiveAsync(CancellationToken ct, string? search = null)
    {
        var owner=await Owner(ct);
        var global=(await users.GetCurrentAsync(ct)).Role==AppRole.SuperAdmin;
        return await db.Projects.AsNoTracking().Where(p=>(global || p.ProjectManagerId==owner) && p.ArchivedAtUtc!=null).Search(search).OrderByDescending(p=>p.ArchivedAtUtc)
            .Select(p=>new ArchiveItem(p.Id,p.ProjectNumber,p.Customer,p.Country.Name,p.ProjectManager.DisplayName,p.Products.Count,
                p.Products.Count(x=>x.CommercialStatus==CommercialStatus.SalesAndDelivery),p.ArchivedAtUtc)).ToListAsync(ct);
    }
}
