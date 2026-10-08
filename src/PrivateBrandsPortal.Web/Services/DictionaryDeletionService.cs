using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using PrivateBrandsPortal.Web.Data;
using PrivateBrandsPortal.Web.Interfaces;
using PrivateBrandsPortal.Web.Models.Entities;
using PrivateBrandsPortal.Web.Models.Enums;
using PrivateBrandsPortal.Web.ViewModels;
namespace PrivateBrandsPortal.Web.Services;
public sealed class DictionaryDeletionService(ApplicationDbContext db,IAppUserService users,TimeProvider clock)
{
    public const string UsedMessage="This dictionary value is already used and cannot be deleted. Deactivate it instead.";
    private async Task<AppUser> Authorize(CancellationToken ct){var u=await users.GetCurrentAsync(ct);if(!u.IsActive || u.Role is not (AppRole.Manager or AppRole.SuperAdmin))throw new PortalAccessException();return u;}
    private static Type EntityType(DictionaryKind kind)=>kind switch {
        DictionaryKind.Countries=>typeof(Country),DictionaryKind.Customers=>typeof(Customer),DictionaryKind.ProductCategories=>typeof(ProductCategory),
        DictionaryKind.ProductSubcategories=>typeof(ProductSubcategory),DictionaryKind.RejectionReasons=>typeof(RejectionReason),DictionaryKind.FormulaOptions=>typeof(FormulaOption),
        DictionaryKind.CustomerRejectionReasons=>typeof(CustomerRejectionReason),_=>throw new ValidationException("Unknown dictionary.")};
    public async Task<DictionaryInput?> FormAsync(DictionaryKind kind,int id,CancellationToken ct)
    {
        await Authorize(ct);var entity=await db.FindAsync(EntityType(kind),[id],ct);if(entity is null)return null;await db.Entry(entity).ReloadAsync(ct);if(db.Entry(entity).State==EntityState.Detached)return null;
        return new(){Id=id,Kind=kind,Name=(string)entity.GetType().GetProperty("Name")!.GetValue(entity)!,Version=(DateTimeOffset?)entity.GetType().GetProperty("UpdatedAtUtc")!.GetValue(entity)};
    }
    public async Task DeleteAsync(DictionaryKind kind,int id,DateTimeOffset? version,CancellationToken ct)
    {
        var actor=await Authorize(ct);if(!version.HasValue)throw new ValidationException("Reopen the delete confirmation.");
        await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,ct);
        var model=await FormAsync(kind,id,ct)??throw new ValidationException("Entry is unavailable.");
        if(model.Version!=version)throw new ValidationException("Entry changed. Reopen it.");
        var used=kind switch {
            DictionaryKind.Countries=>await db.Projects.AnyAsync(x=>x.CountryId==id,ct)||await db.Customers.AnyAsync(x=>x.DefaultCountryId==id,ct),
            DictionaryKind.Customers=>await db.Projects.AnyAsync(x=>x.CustomerId==id || x.Customer==model.Name,ct),
            DictionaryKind.ProductCategories=>await db.ProjectProducts.AnyAsync(x=>x.ProductCategoryId==id,ct)||await db.ProductSubcategories.AnyAsync(x=>x.ProductCategoryId==id,ct)||await db.AuditLogs.AnyAsync(x=>x.FieldName=="ProductCategoryId" && (x.OldValue==id.ToString()||x.NewValue==id.ToString()),ct),
            DictionaryKind.ProductSubcategories=>await db.ProjectProducts.AnyAsync(x=>x.ProductSubcategoryId==id || x.Subcategory==model.Name,ct)||await db.AuditLogs.AnyAsync(x=>(x.FieldName=="ProductSubcategoryId" && (x.OldValue==id.ToString()||x.NewValue==id.ToString())) || (x.FieldName=="Subcategory" && (x.OldValue==model.Name||x.NewValue==model.Name)),ct),
            DictionaryKind.RejectionReasons=>await db.ProductReviews.AnyAsync(x=>x.RejectionReasonId==id || x.RejectionReasonName==model.Name,ct),
            DictionaryKind.FormulaOptions=>await db.ProjectProducts.AnyAsync(x=>x.FormulaOptionId==id,ct)||await db.AuditLogs.AnyAsync(x=>x.FieldName=="Formula" && (x.OldValue==model.Name||x.NewValue==model.Name),ct),
            DictionaryKind.CustomerRejectionReasons=>await db.Projects.AnyAsync(x=>x.CustomerRejectionReasonId==id,ct)||await db.ProjectProducts.AnyAsync(x=>x.CustomerRejectionReasonId==id,ct),_=>true};
        if(used)throw new ValidationException(UsedMessage);
        db.Remove((await db.FindAsync(EntityType(kind),[id],ct))!);
        db.AuditLogs.Add(new(){EntityType=EntityType(kind).Name,EntityId=id,FieldName="DictionaryValueDeleted",OldValue=System.Text.Json.JsonSerializer.Serialize(new{DictionaryType=kind.ToString(),model.Name}),ChangedByUserId=actor.Id,ChangedAtUtc=clock.GetUtcNow(),ChangeType=AuditChangeType.Deleted});
        try {await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);}
        catch(DbUpdateException ex)when(ex.InnerException is Microsoft.Data.SqlClient.SqlException{Number:547}){throw new ValidationException(UsedMessage);}
    }
}
