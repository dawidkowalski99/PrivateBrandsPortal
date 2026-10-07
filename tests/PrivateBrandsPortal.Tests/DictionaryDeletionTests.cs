using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using PrivateBrandsPortal.Web.Models.Entities;
using PrivateBrandsPortal.Web.Models.Enums;
using PrivateBrandsPortal.Web.Services;
using PrivateBrandsPortal.Web.ViewModels;
namespace PrivateBrandsPortal.Tests;
[Collection("SQL integration")]
public sealed class DictionaryDeletionTests
{
    [Theory]
    [InlineData(AppRole.Manager)]
    [InlineData(AppRole.SuperAdmin)]
    public async Task Unused_value_can_be_deleted_with_audit(AppRole role){
        await using var s=new ProjectSqlTests.Scope();var actor=await s.Users.GetCurrentAsync();actor.Role=role;
        var value=new CustomerRejectionReason{Code="TEST_"+Guid.NewGuid().ToString("N"),Name=s.Login+" unused"};
        s.Db.CustomerRejectionReasons.Add(value);await s.Db.SaveChangesAsync();
        var service=new DictionaryDeletionService(s.Db,new PermissionTests.FixedUser(actor),TimeProvider.System);
        var form=(await service.FormAsync(DictionaryKind.CustomerRejectionReasons,value.Id,default))!;
        await service.DeleteAsync(form.Kind,form.Id,form.Version,default);
        Assert.False(await s.Db.CustomerRejectionReasons.AnyAsync(x=>x.Id==value.Id));
        Assert.True(await s.Db.AuditLogs.AnyAsync(x=>x.EntityType==nameof(CustomerRejectionReason) && x.EntityId==value.Id && x.FieldName=="DictionaryValueDeleted"));
    }
    [Fact]
    public async Task Project_manager_permission_does_not_authorize_delete(){
        await using var s=new ProjectSqlTests.Scope();var actor=await s.Users.GetCurrentAsync();
        var service=new DictionaryDeletionService(s.Db,new PermissionTests.FixedUser(actor),TimeProvider.System);
        await Assert.ThrowsAsync<PortalAccessException>(()=>service.FormAsync(DictionaryKind.Countries,1,default));
        await Assert.ThrowsAsync<PortalAccessException>(()=>service.DeleteAsync(DictionaryKind.Countries,1,DateTimeOffset.UtcNow,default));
    }
    [Theory]
    [InlineData(DictionaryKind.Countries)]
    [InlineData(DictionaryKind.Customers)]
    [InlineData(DictionaryKind.ProductCategories)]
    [InlineData(DictionaryKind.ProductSubcategories)]
    [InlineData(DictionaryKind.FormulaOptions)]
    public async Task Referenced_values_are_protected(DictionaryKind kind){
        await using var s=new ProjectSqlTests.Scope();var draft=WizardTests.ValidDraft();var id=await s.Projects.SaveDraftAsync(draft);
        var p=await s.Db.Projects.AsNoTracking().Include(x=>x.Products).SingleAsync(x=>x.Id==id);
        var key=kind switch{
            DictionaryKind.Countries=>p.CountryId,DictionaryKind.Customers=>p.CustomerId!.Value,
            DictionaryKind.ProductCategories=>p.Products.First().ProductCategoryId!.Value,
            DictionaryKind.ProductSubcategories=>p.Products.First().ProductSubcategoryId!.Value,
            _=>p.Products.First().FormulaOptionId!.Value};
        var actor=await s.Users.GetCurrentAsync();actor.Role=AppRole.Manager;
        var service=new DictionaryDeletionService(s.Db,new PermissionTests.FixedUser(actor),TimeProvider.System);
        var f=(await service.FormAsync(kind,key,default))!;
        var error=await Assert.ThrowsAsync<ValidationException>(()=>service.DeleteAsync(kind,key,f.Version,default));
        Assert.Equal(DictionaryDeletionService.UsedMessage,error.Message);
        Assert.NotNull(await service.FormAsync(kind,key,default));
    }
    [Fact]
    public async Task Initial_review_reason_is_protected(){
        await using var s=new ProjectSqlTests.Scope();var id=await CommercialWorkflowTests.Submitted(s);
        await CommercialWorkflowTests.Decide(s,id,0,ReviewDecision.Rejected,7);
        var actor=await s.Users.GetCurrentAsync();actor.Role=AppRole.Manager;
        var service=new DictionaryDeletionService(s.Db,new PermissionTests.FixedUser(actor),TimeProvider.System);
        var f=(await service.FormAsync(DictionaryKind.RejectionReasons,7,default))!;
        await Assert.ThrowsAsync<ValidationException>(()=>service.DeleteAsync(f.Kind,f.Id,f.Version,default));
    }
}
