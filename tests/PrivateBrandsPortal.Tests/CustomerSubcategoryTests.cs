using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using PrivateBrandsPortal.Web.Models.Entities;
using PrivateBrandsPortal.Web.Models.Enums;
using PrivateBrandsPortal.Web.Services;
using PrivateBrandsPortal.Web.ViewModels;
namespace PrivateBrandsPortal.Tests;

[Collection("SQL integration")]
public sealed class CustomerSubcategoryTests
{
    internal static DictionaryService Dictionaries(ProjectSqlTests.Scope s) => new(s.Db,new PermissionService(new PolicyAppUser("SuperAdmin")),TimeProvider.System);
    internal static async Task<Customer> Customer(ProjectSqlTests.Scope s, bool active=true)
    {
        var row=new Customer{Name=s.Login+" customer "+Guid.NewGuid().ToString("N"),IsActive=active,CreatedAtUtc=DateTimeOffset.UtcNow,UpdatedAtUtc=DateTimeOffset.UtcNow};
        s.Db.Customers.Add(row);await s.Db.SaveChangesAsync();return row;
    }
    internal static async Task<ProductSubcategory> Subcategory(ProjectSqlTests.Scope s,int category=1,bool active=true)
    {
        var row=new ProductSubcategory{Name=s.Login+" sub "+Guid.NewGuid().ToString("N"),ProductCategoryId=category,IsActive=active,CreatedAtUtc=DateTimeOffset.UtcNow,UpdatedAtUtc=DateTimeOffset.UtcNow};
        s.Db.ProductSubcategories.Add(row);await s.Db.SaveChangesAsync();return row;
    }
    [Fact]
    public async Task Active_lookups_are_sorted_scoped_and_inactive_entries_are_excluded()
    {
        await using var s=new ProjectSqlTests.Scope();var customer=await Customer(s);var inactive=await Customer(s,false);
        var a=await Subcategory(s);var b=await Subcategory(s,2);var hidden=await Subcategory(s,1,false);
        var lookups=new ProjectDictionaryService(s.Db);
        var customers=await lookups.CustomersAsync();Assert.Contains(customers,x=>x.Id==customer.Id);Assert.DoesNotContain(customers,x=>x.Id==inactive.Id);
        var subcategories=await lookups.SubcategoriesAsync();Assert.Contains(subcategories,x=>x.Id==a.Id && x.CategoryId==1);Assert.Contains(subcategories,x=>x.Id==b.Id && x.CategoryId==2);Assert.DoesNotContain(subcategories,x=>x.Id==hidden.Id);
        var page=await Dictionaries(s).ListAsync(DictionaryKind.ProductSubcategories,default,s.Login,1);
        Assert.All(page.Items,x=>Assert.Equal(1,x.ProductCategoryId));Assert.DoesNotContain(page.Items,x=>x.Id==b.Id);
    }
    [Fact]
    public async Task Save_records_ids_and_server_snapshots_and_renames_do_not_rewrite_history()
    {
        await using var s=new ProjectSqlTests.Scope();var customer=await Customer(s);var sub=await Subcategory(s);
        var draft=WizardTests.ValidDraft();draft.Brief.CustomerId=customer.Id;draft.Brief.Customer="forged";
        draft.Products[0].ProductSubcategoryId=sub.Id;draft.Products[0].Subcategory="forged";
        var id=await s.Projects.SaveDraftAsync(draft);s.Db.ChangeTracker.Clear();
        var saved=await s.Db.Projects.AsNoTracking().Include(x=>x.Products).SingleAsync(x=>x.Id==id);
        Assert.Equal(customer.Id,saved.CustomerId);Assert.Equal(customer.Name,saved.Customer);
        Assert.Equal(sub.Id,saved.Products.OrderBy(x=>x.Id).First().ProductSubcategoryId);Assert.Equal(sub.Name,saved.Products.OrderBy(x=>x.Id).First().Subcategory);
        await s.Db.Customers.Where(x=>x.Id==customer.Id).ExecuteUpdateAsync(x=>x.SetProperty(p=>p.Name,customer.Name+" renamed").SetProperty(p=>p.IsActive,false));
        await s.Db.ProductSubcategories.Where(x=>x.Id==sub.Id).ExecuteUpdateAsync(x=>x.SetProperty(p=>p.Name,sub.Name+" renamed").SetProperty(p=>p.IsActive,false));
        var edit=(await s.Projects.LoadDraftAsync(id))!;edit.Brief.Customer="tampered";edit.Products[0].Subcategory="tampered";edit.Products[0].Quantity++;
        await s.Projects.SaveDraftAsync(edit);
        var details=(await s.Projects.DetailsAsync(id))!;Assert.Equal(customer.Name,details.Customer);Assert.Equal(sub.Name,details.Products[0].ProductType);
    }
    [Theory]
    [InlineData("inactive customer")]
    [InlineData("missing customer")]
    [InlineData("inactive subcategory")]
    [InlineData("missing subcategory")]
    [InlineData("wrong category")]
    public async Task New_project_rejects_forged_dictionary_selection(string kind)
    {
        await using var s=new ProjectSqlTests.Scope();var customer=await Customer(s,kind!="inactive customer");var sub=await Subcategory(s,1,kind!="inactive subcategory");
        var draft=WizardTests.ValidDraft();draft.Brief.CustomerId=kind=="missing customer"?null:customer.Id;
        draft.Products[0].ProductSubcategoryId=kind=="missing subcategory"?null:sub.Id;
        if(kind=="wrong category")draft.Products[0].ProductCategoryId=2;
        await Assert.ThrowsAsync<ValidationException>(()=>s.Projects.SaveDraftAsync(draft));
        Assert.Empty(await s.Projects.ListAsync());
    }
    [Fact]
    public async Task Legacy_null_foreign_keys_and_product_type_survive_draft_edit()
    {
        await using var s=new ProjectSqlTests.Scope();var id=await s.Projects.SaveDraftAsync(WizardTests.ValidDraft());
        await s.Db.Projects.Where(x=>x.Id==id).ExecuteUpdateAsync(x=>x.SetProperty(p=>p.CustomerId,(int?)null).SetProperty(p=>p.Customer,"Legacy customer"));
        await s.Db.ProjectProducts.Where(x=>x.ProjectId==id).ExecuteUpdateAsync(x=>x.SetProperty(p=>p.ProductSubcategoryId,(int?)null).SetProperty(p=>p.ProductCategoryId,(int?)null).SetProperty(p=>p.Subcategory,(string?)null).SetProperty(p=>p.ProductTypeId,1));
        s.Db.ChangeTracker.Clear();var edit=(await s.Projects.LoadDraftAsync(id))!;edit.Products[0].Quantity++;
        await s.Projects.SaveDraftAsync(edit);var details=(await s.Projects.DetailsAsync(id))!;
        Assert.Equal("Legacy customer",details.Customer);Assert.All(details.Products,p=>Assert.Equal("Shampoo",p.ProductType));
        Assert.Null(await s.Db.Projects.Where(x=>x.Id==id).Select(x=>x.CustomerId).SingleAsync());
    }
    [Fact]
    public async Task Copy_and_duplicate_keep_dictionary_identity_but_clear_sku_and_reject_inactive_new_copy()
    {
        await using var s=new ProjectSqlTests.Scope();var sub=await Subcategory(s);var draft=WizardTests.ValidDraft();draft.Products[0].ProductSubcategoryId=sub.Id;
        var id=await s.Projects.SaveDraftAsync(draft);var source=(await s.Projects.LoadDraftAsync(id))!.Products[0];
        var duplicate=ProductCopyService.Duplicate(source);var copy=(await new ProductCopyService(s.Db,s.Users).CopyAsync(source.PersistedId!.Value,default))!;
        foreach(var input in new[]{duplicate,copy}){Assert.Equal(sub.Id,input.ProductSubcategoryId);Assert.Equal(1,input.ProductCategoryId);Assert.Equal(sub.Name,input.Subcategory);Assert.Empty(input.SKU);Assert.Null(input.PersistedId);}
        await s.Db.ProductSubcategories.Where(x=>x.Id==sub.Id).ExecuteUpdateAsync(x=>x.SetProperty(p=>p.IsActive,false));
        var next=WizardTests.ValidDraft();copy.SKU="NEW-SKU";next.Products=[copy];
        await Assert.ThrowsAsync<ValidationException>(()=>s.Projects.SaveDraftAsync(next));
        Assert.Equal(sub.Name,(await s.Projects.DetailsAsync(id))!.Products[0].ProductType);
    }
    [Fact]
    public async Task Manager_numeric_edit_preserves_legacy_product_type_fallback()
    {
        await using var s=new ProjectSqlTests.Scope();var id=await CommercialWorkflowTests.Submitted(s);
        var product=(await s.Projects.DetailsAsync(id))!.Products[0];
        await s.Db.ProjectProducts.Where(x=>x.Id==product.Id).ExecuteUpdateAsync(x=>x
            .SetProperty(p=>p.ProductSubcategoryId,(int?)null).SetProperty(p=>p.Subcategory,(string?)null).SetProperty(p=>p.ProductTypeId,1));
        s.Db.ChangeTracker.Clear();var review=CommercialWorkflowTests.Review(s);
        var form=(await review.FormAsync(id,product.Id,ReviewDecision.EditedAndApproved))!;
        form.Input.Product!.Quantity++;await review.DecideAsync(form.Input);
        Assert.Null(await s.Db.ProjectProducts.Where(x=>x.Id==product.Id).Select(x=>x.Subcategory).SingleAsync());
        Assert.Equal("Shampoo",(await s.Projects.DetailsAsync(id))!.Products[0].ProductType);
    }
    [Fact]
    public async Task Dictionary_normalizes_whitespace_checks_uniqueness_and_preserves_used_parent()
    {
        await using var s=new ProjectSqlTests.Scope();var service=Dictionaries(s);
        await service.SaveAsync(new(){Kind=DictionaryKind.Customers,Name="  "+s.Login+"   Acme  "},default);
        Assert.Equal(s.Login+" Acme",(await s.Db.Customers.SingleAsync(x=>x.Name.StartsWith(s.Login))).Name);
        await Assert.ThrowsAsync<ValidationException>(()=>service.SaveAsync(new(){Kind=DictionaryKind.Customers,Name=s.Login+"\tAcme"},default));
        var sub=await Subcategory(s);var edit=(await service.GetAsync(DictionaryKind.ProductSubcategories,sub.Id,default))!;edit.ProductCategoryId=2;
        await Assert.ThrowsAsync<ValidationException>(()=>service.SaveAsync(edit,default));
        var customer=(await service.ListAsync(DictionaryKind.Customers,default,"Acme")).Items.Single(x=>x.Name.StartsWith(s.Login));
        customer.IsActive=false;await service.SaveAsync(customer,default);Assert.DoesNotContain(await new ProjectDictionaryService(s.Db).CustomersAsync(),x=>x.Id==customer.Id);
    }
}
