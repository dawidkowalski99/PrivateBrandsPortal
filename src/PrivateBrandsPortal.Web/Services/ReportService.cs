using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using PrivateBrandsPortal.Web.Data;
using PrivateBrandsPortal.Web.Interfaces;
using PrivateBrandsPortal.Web.Models.Entities;
using PrivateBrandsPortal.Web.Models.Enums;
using PrivateBrandsPortal.Web.ViewModels;
namespace PrivateBrandsPortal.Web.Services;
public sealed class ReportService(ApplicationDbContext db,IPermissionService permissions,IAppUserService users)
{
    private Task Authorize(CancellationToken ct) => permissions.RequireAsync(PermissionCodes.ViewReports, ct);
    private IQueryable<ProjectProduct> Filter(ReportFilter f, AppUser user)
    {
        Validator.ValidateObject(f,new ValidationContext(f),true);
        var q=db.ProjectProducts.AsNoTracking();
        if (!user.IsActive) throw new PortalAccessException();
        if (user.Role == AppRole.ProjectManager) q=q.Where(x=>x.Project.ProjectManagerId==user.Id);
        if(f.ProjectManagerId.HasValue)q=q.Where(x=>x.Project.ProjectManagerId==f.ProjectManagerId);
        if(f.CountryId.HasValue)q=q.Where(x=>x.Project.CountryId==f.CountryId);
        if(f.ProjectStatus.HasValue)q=q.Where(x=>x.Project.Status==f.ProjectStatus);
        if(f.ReviewStatus.HasValue)q=q.Where(x=>x.ReviewStatus==f.ReviewStatus);
        if(f.CommercialStatus.HasValue)q=q.Where(x=>x.CommercialStatus==f.CommercialStatus);
        if(f.ProductCategoryId.HasValue)q=q.Where(x=>x.ProductCategoryId==f.ProductCategoryId);
        if(!string.IsNullOrWhiteSpace(f.Customer)){var term=f.Customer.Trim();q=q.Where(x=>x.Project.Customer.Contains(term));}
        if(!string.IsNullOrWhiteSpace(f.Subcategory)){var term=f.Subcategory.Trim();q=q.Where(x=>(x.Subcategory ?? EF.Functions.Collate(x.ProductType!.Name,"Latin1_General_100_CI_AS")).Contains(term));}
        if(f.DateFrom.HasValue){var from=new DateTimeOffset(f.DateFrom.Value.ToDateTime(TimeOnly.MinValue,DateTimeKind.Utc));q=q.Where(x=>x.Project.CreatedAtUtc>=from);}
        if(f.DateTo.HasValue){var to=new DateTimeOffset(f.DateTo.Value.AddDays(1).ToDateTime(TimeOnly.MinValue,DateTimeKind.Utc));q=q.Where(x=>x.Project.CreatedAtUtc<to);}
        if(f.Archived==ArchiveFilter.Active)q=q.Where(x=>x.Project.ArchivedAtUtc==null);
        if(f.Archived==ArchiveFilter.Archived)q=q.Where(x=>x.Project.ArchivedAtUtc!=null);
        return q;
    }
    private static IQueryable<ReportRow> Rows(IQueryable<ProjectProduct> q)=>q.OrderByDescending(x=>x.Project.CreatedAtUtc).ThenBy(x=>x.ProjectId).ThenBy(x=>x.Id).Select(x=>new ReportRow{
        ProjectNumber=x.Project.ProjectNumber,ProjectManager=x.Project.ProjectManager.DisplayName,Customer=x.Project.Customer,Country=x.Project.Country.Name,ProjectStatus=x.Project.Status,
        Category=x.ProductCategory==null?null:x.ProductCategory.Name,Subcategory=x.Subcategory ?? EF.Functions.Collate(x.ProductType!.Name,"Latin1_General_100_CI_AS"),SKU=x.SKU,Quantity=x.Quantity,EstimatedValue=x.EstimatedValue,EstimatedMargin=x.EstimatedMargin,
        Formula=x.FormulaOption!=null?x.FormulaOption.Name:x.FormulaStatus==FormulaStatus.ReadyToGo?"Ready to go":"New formula", ReviewStatus=x.ReviewStatus,RejectionReason=x.Reviews.Where(r=>r.Decision==ReviewDecision.Rejected).OrderByDescending(r=>r.ReviewedAtUtc).ThenByDescending(r=>r.Id).Select(r=>r.RejectionReasonName ?? (r.RejectionReason==null?r.Comment:EF.Functions.Collate(r.RejectionReason.Name,"Latin1_General_100_CI_AS"))).FirstOrDefault(),
        ImplementationStatus=x.ImplementationApprovals.OrderByDescending(a=>a.Id).Select(a=>(ImplementationApprovalStatus?)a.Status).FirstOrDefault(),CustomerRejectionReason=x.Project.CustomerRejectionReasonName,CommercialStatus=x.CommercialStatus,CreatedAtUtc=x.Project.CreatedAtUtc,ArchivedAtUtc=x.Project.ArchivedAtUtc});
    public async Task<ReportPage> GetAsync(ReportFilter f,CancellationToken ct=default)
    {
        await Authorize(ct);var user=await users.GetCurrentAsync(ct);var q=Filter(f,user);
        var totals=await q.GroupBy(x=>1).Select(g=>new ReportTotals{Projects=g.Select(x=>x.ProjectId).Distinct().Count(),SKUs=g.Count(),Approved=g.Count(x=>x.ReviewStatus==ProductReviewStatus.Approved),EditedApproved=g.Count(x=>x.ReviewStatus==ProductReviewStatus.EditedAndApproved),Rejected=g.Count(x=>x.ReviewStatus==ProductReviewStatus.Rejected),Delivered=g.Count(x=>x.CommercialStatus==CommercialStatus.SalesAndDelivery)}).SingleOrDefaultAsync(ct)??new();
        var managers=await q.GroupBy(x=>new{x.Project.ProjectManagerId,x.Project.ProjectManager.DisplayName}).Select(g=>new ManagerReport{Id=g.Key.ProjectManagerId,Name=g.Key.DisplayName,Totals=new ReportTotals{Projects=g.Select(x=>x.ProjectId).Distinct().Count(),SKUs=g.Count(),Approved=g.Count(x=>x.ReviewStatus==ProductReviewStatus.Approved),EditedApproved=g.Count(x=>x.ReviewStatus==ProductReviewStatus.EditedAndApproved),Rejected=g.Count(x=>x.ReviewStatus==ProductReviewStatus.Rejected),Delivered=g.Count(x=>x.CommercialStatus==CommercialStatus.SalesAndDelivery)}}).OrderBy(x=>x.Name).ToListAsync(ct);
        var pages=Math.Max(1,(int)Math.Ceiling(totals.SKUs/50m));f.Page=Math.Min(f.Page,pages);
        return new ReportPage{Filter=f,Totals=totals,Managers=managers,Rows=await Rows(q).Skip((f.Page-1)*50).Take(50).ToListAsync(ct),
            Users=await db.AppUsers.AsNoTracking().Where(x=>x.Projects.Any() && (user.Role!=AppRole.ProjectManager || x.Id==user.Id)).OrderBy(x=>x.DisplayName).Select(x=>new LookupItem(x.Id,x.DisplayName)).ToListAsync(ct),
            Countries=await db.Countries.AsNoTracking().OrderBy(x=>x.DisplayOrder).ThenBy(x=>x.Name).Select(x=>new LookupItem(x.Id,x.Name)).ToListAsync(ct),
            Categories=await db.ProductCategories.AsNoTracking().OrderBy(x=>x.DisplayOrder).ThenBy(x=>x.Name).Select(x=>new LookupItem(x.Id,x.Name)).ToListAsync(ct)};
    }
    public async Task<IAsyncEnumerable<ReportRow>> ExportAsync(ReportFilter filter,CancellationToken ct=default){await permissions.RequireAsync(PermissionCodes.ExportReports,ct);return Rows(Filter(filter,await users.GetCurrentAsync(ct))).AsAsyncEnumerable();}
}
