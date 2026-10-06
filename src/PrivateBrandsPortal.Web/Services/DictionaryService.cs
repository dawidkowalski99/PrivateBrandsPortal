using System.ComponentModel.DataAnnotations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PrivateBrandsPortal.Web.Data;
using PrivateBrandsPortal.Web.Interfaces;
using PrivateBrandsPortal.Web.Models.Entities;
using PrivateBrandsPortal.Web.Models.Enums;
using PrivateBrandsPortal.Web.ViewModels;
namespace PrivateBrandsPortal.Web.Services;
public sealed class DictionaryService(ApplicationDbContext db,IPermissionService permissions,TimeProvider clock)
{
    private Task Authorize(CancellationToken ct) => permissions.RequireAsync(PermissionCodes.ManageDictionaries, ct);
    private IQueryable<DictionaryInput> Query(DictionaryKind kind)=>kind switch{
        DictionaryKind.Customers=>db.Customers.AsNoTracking().Select(x=>new DictionaryInput{Id=x.Id,Kind=kind,Name=x.Name,Code=x.Code,DefaultCountryId=x.DefaultCountryId,DefaultCountryName=x.DefaultCountry==null?null:x.DefaultCountry.Name,IsActive=x.IsActive,DisplayOrder=x.DisplayOrder,Version=x.UpdatedAtUtc}),
        DictionaryKind.ProductSubcategories=>db.ProductSubcategories.AsNoTracking().Select(x=>new DictionaryInput{Id=x.Id,Kind=kind,Name=x.Name,ProductCategoryId=x.ProductCategoryId,CategoryName=x.ProductCategory.Name,IsActive=x.IsActive,DisplayOrder=x.DisplayOrder,Version=x.UpdatedAtUtc}),
        DictionaryKind.ProductCategories=>db.ProductCategories.AsNoTracking().Select(x=>new DictionaryInput{Id=x.Id,Kind=kind,Name=x.Name,Description=x.Description,IsActive=x.IsActive,DisplayOrder=x.DisplayOrder,Version=x.UpdatedAtUtc}),
        DictionaryKind.RejectionReasons=>db.RejectionReasons.AsNoTracking().Select(x=>new DictionaryInput{Id=x.Id,Kind=kind,Name=x.Name,Description=x.Description,IsActive=x.IsActive,DisplayOrder=x.DisplayOrder,Version=x.UpdatedAtUtc,RequiresComment=x.RequiresComment}),
        DictionaryKind.Countries=>db.Countries.AsNoTracking().Select(x=>new DictionaryInput{Id=x.Id,Kind=kind,Name=x.Name,Code=x.Code,IsActive=x.IsActive,DisplayOrder=x.DisplayOrder,Version=x.UpdatedAtUtc}),
        _=>throw new ValidationException("Unknown dictionary.")};
    public async Task<IReadOnlyList<LookupItem>> CountriesAsync(CancellationToken ct) => await db.Countries.AsNoTracking().Where(x=>x.IsActive).OrderBy(x=>x.DisplayOrder).ThenBy(x=>x.Name).Select(x=>new LookupItem(x.Id,x.Name)).ToListAsync(ct);
    public async Task<IReadOnlyList<LookupItem>> CategoriesAsync(CancellationToken ct) => await db.ProductCategories.AsNoTracking().OrderBy(x=>x.DisplayOrder).ThenBy(x=>x.Name).Select(x=>new LookupItem(x.Id,x.Name)).ToListAsync(ct);
    public async Task<DictionaryPage> ListAsync(DictionaryKind kind,CancellationToken ct,string? search=null,int? categoryId=null){
        await Authorize(ct);var q=Query(kind);search=search?.Trim();
        if(search?.Length>200)throw new ValidationException("Search supports up to 200 characters.");
        if(!string.IsNullOrEmpty(search))q=kind is DictionaryKind.Customers or DictionaryKind.Countries ? q.Where(x=>x.Name.Contains(search) || (x.Code!=null && x.Code.Contains(search))) : q.Where(x=>x.Name.Contains(search));
        if(kind==DictionaryKind.ProductSubcategories && categoryId.HasValue)q=q.Where(x=>x.ProductCategoryId==categoryId);
        return new(kind,await q.OrderBy(x=>x.DisplayOrder).ThenBy(x=>x.Name).ToListAsync(ct)){Search=search,CategoryId=categoryId,Categories=await CategoriesAsync(ct)};
    }
    public async Task<DictionaryInput?> GetAsync(DictionaryKind kind,int id,CancellationToken ct){await Authorize(ct);var model=id==0?new DictionaryInput{Kind=kind}:await Query(kind).SingleOrDefaultAsync(x=>x.Id==id,ct);if(model is not null){model.Categories=await CategoriesAsync(ct);model.Countries=await CountriesAsync(ct);}return model;}
    public async Task SaveAsync(DictionaryInput input,CancellationToken ct)
    {
        await Authorize(ct); input.Name=string.Join(' ',(input.Name??"").Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries));input.Description=input.Description?.Trim();input.Code=string.IsNullOrWhiteSpace(input.Code)?null:input.Code.Trim().ToUpperInvariant();Validator.ValidateObject(input,new ValidationContext(input),true);
        if(input.Id<0)throw new ValidationException("Invalid entry.");
        if(await Query(input.Kind).AnyAsync(x=>x.Id!=input.Id && x.Name==input.Name && (input.Kind!=DictionaryKind.ProductSubcategories || x.ProductCategoryId==input.ProductCategoryId),ct))throw new ValidationException("This name already exists.");
        var now=clock.GetUtcNow(); if(now<=input.Version)now=input.Version!.Value.AddTicks(1);
        try {
            if(input.Kind==DictionaryKind.Customers){
                if(input.DefaultCountryId.HasValue && !await db.Countries.AnyAsync(x=>x.Id==input.DefaultCountryId && x.IsActive,ct))throw new ValidationException("Select an active default country or leave it empty.");
                if(input.Id==0)db.Customers.Add(new Customer{Name=input.Name,Code=input.Code,DefaultCountryId=input.DefaultCountryId,IsActive=input.IsActive,DisplayOrder=input.DisplayOrder,CreatedAtUtc=now,UpdatedAtUtc=now});
                else if(await db.Customers.Where(x=>x.Id==input.Id && x.UpdatedAtUtc==input.Version).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.DefaultCountryId,input.DefaultCountryId).SetProperty(x=>x.Name,input.Name).SetProperty(x=>x.Code,input.Code).SetProperty(x=>x.IsActive,input.IsActive).SetProperty(x=>x.DisplayOrder,input.DisplayOrder).SetProperty(x=>x.UpdatedAtUtc,now),ct)!=1)throw new ValidationException("Entry changed. Reopen it.");
            }else if(input.Kind==DictionaryKind.ProductSubcategories){
                if(input.Id==0 && !await db.ProductCategories.AnyAsync(x=>x.Id==input.ProductCategoryId && x.IsActive,ct))throw new ValidationException("Select an active category.");
                if(input.Id!=0 && await db.ProductSubcategories.AnyAsync(x=>x.Id==input.Id && x.ProductCategoryId!=input.ProductCategoryId,ct))
                    throw new ValidationException("A subcategory cannot move to another category. Add a new entry instead.");
                if(input.Id==0)db.ProductSubcategories.Add(new ProductSubcategory{Name=input.Name,ProductCategoryId=input.ProductCategoryId!.Value,IsActive=input.IsActive,DisplayOrder=input.DisplayOrder,CreatedAtUtc=now,UpdatedAtUtc=now});
                else if(await db.ProductSubcategories.Where(x=>x.Id==input.Id && x.UpdatedAtUtc==input.Version).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Name,input.Name).SetProperty(x=>x.ProductCategoryId,input.ProductCategoryId!.Value).SetProperty(x=>x.IsActive,input.IsActive).SetProperty(x=>x.DisplayOrder,input.DisplayOrder).SetProperty(x=>x.UpdatedAtUtc,now),ct)!=1)throw new ValidationException("Entry changed. Reopen it.");
            }else if(input.Kind==DictionaryKind.Countries){
                if(await db.Countries.AnyAsync(x=>x.Id!=input.Id && x.Code==input.Code,ct))throw new ValidationException("This country code already exists.");
                if(input.Id==0)db.Countries.Add(new Country{Name=input.Name,Code=input.Code!,IsActive=input.IsActive,DisplayOrder=input.DisplayOrder,UpdatedAtUtc=now});
                else if(await db.Countries.Where(x=>x.Id==input.Id && x.UpdatedAtUtc==input.Version).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Name,input.Name).SetProperty(x=>x.Code,input.Code!).SetProperty(x=>x.IsActive,input.IsActive).SetProperty(x=>x.DisplayOrder,input.DisplayOrder).SetProperty(x=>x.UpdatedAtUtc,now),ct)!=1)throw new ValidationException("Entry changed. Reopen it.");
            }else if(input.Kind==DictionaryKind.ProductCategories){
                if(input.Id==0)db.ProductCategories.Add(new ProductCategory{Name=input.Name,Description=input.Description,IsActive=input.IsActive,DisplayOrder=input.DisplayOrder,CreatedAtUtc=now,UpdatedAtUtc=now});
                else if(await db.ProductCategories.Where(x=>x.Id==input.Id && x.UpdatedAtUtc==input.Version).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Name,input.Name).SetProperty(x=>x.Description,input.Description).SetProperty(x=>x.IsActive,input.IsActive).SetProperty(x=>x.DisplayOrder,input.DisplayOrder).SetProperty(x=>x.UpdatedAtUtc,now),ct)!=1)throw new ValidationException("Entry changed. Reopen it.");
            }else{
                var requires=input.RequiresComment || input.Name.Equals("Other",StringComparison.OrdinalIgnoreCase);
                if(input.Id==0)db.RejectionReasons.Add(new RejectionReason{Name=input.Name,Description=input.Description,IsActive=input.IsActive,DisplayOrder=input.DisplayOrder,RequiresComment=requires,CreatedAtUtc=now,UpdatedAtUtc=now});
                else if(await db.RejectionReasons.Where(x=>x.Id==input.Id && x.UpdatedAtUtc==input.Version).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Name,input.Name).SetProperty(x=>x.Description,input.Description).SetProperty(x=>x.IsActive,input.IsActive).SetProperty(x=>x.DisplayOrder,input.DisplayOrder).SetProperty(x=>x.RequiresComment,x=>x.RequiresComment || requires).SetProperty(x=>x.UpdatedAtUtc,now),ct)!=1)throw new ValidationException("Entry changed. Reopen it.");
            }
            await db.SaveChangesAsync(ct);
        }catch(DbUpdateException ex)when(ex.InnerException is SqlException{Number:2601 or 2627}){throw new ValidationException("This name already exists.");}
        catch(SqlException ex)when(ex.Number is 2601 or 2627){throw new ValidationException("This name already exists.");}
    }
}
