using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using PrivateBrandsPortal.Web.Models.Entities;
using PrivateBrandsPortal.Web.Services;
using PrivateBrandsPortal.Web.ViewModels;
namespace PrivateBrandsPortal.Tests;

[Collection("SQL integration")]
public sealed class CustomerCountryTests
{
    [Fact]
    public async Task Customer_options_expose_active_default_or_null_and_hide_inactive_customers()
    {
        await using var s=new ProjectSqlTests.Scope();
        var customer=await CustomerSubcategoryTests.Customer(s);var empty=await CustomerSubcategoryTests.Customer(s);
        var country=await s.Db.Countries.SingleAsync(x=>x.Code=="PL");
        customer.DefaultCountryId=country.Id;await s.Db.SaveChangesAsync();
        var options=await new ProjectDictionaryService(s.Db).CustomersAsync();
        Assert.Equal(country.Id,options.Single(x=>x.Id==customer.Id).DefaultCountryId);
        Assert.Null(options.Single(x=>x.Id==empty.Id).DefaultCountryId);
        customer.IsActive=false;await s.Db.SaveChangesAsync();
        Assert.DoesNotContain(await new ProjectDictionaryService(s.Db).CustomersAsync(),x=>x.Id==customer.Id);
    }
    [Fact]
    public async Task Manual_country_override_and_customer_snapshot_survive_default_edit()
    {
        await using var s=new ProjectSqlTests.Scope();var customer=await CustomerSubcategoryTests.Customer(s);
        var pl=await s.Db.Countries.SingleAsync(x=>x.Code=="PL");var de=await s.Db.Countries.SingleAsync(x=>x.Code=="DE");
        customer.DefaultCountryId=pl.Id;await s.Db.SaveChangesAsync();
        var draft=WizardTests.ValidDraft();draft.Brief.CustomerId=customer.Id;draft.Brief.CountryId=de.Id;
        var id=await s.Projects.SaveDraftAsync(draft);
        var admin=CustomerSubcategoryTests.Dictionaries(s);var input=(await admin.GetAsync(DictionaryKind.Customers,customer.Id,default))!;
        input.DefaultCountryId=null;input.Name+=" renamed";await admin.SaveAsync(input,default);
        var details=(await s.Projects.DetailsAsync(id))!;Assert.Equal(customer.Name,details.Customer);Assert.Equal(de.Name,details.Country);
        var edit=(await s.Projects.LoadDraftAsync(id))!;Assert.Equal(de.Id,edit.Brief.CountryId);
        await s.Projects.SaveDraftAsync(edit);Assert.Equal(de.Name,(await s.Projects.DetailsAsync(id))!.Country);
    }
    [Fact]
    public async Task Customer_without_default_can_create_project_with_manual_country()
    {
        await using var s=new ProjectSqlTests.Scope();var customer=await CustomerSubcategoryTests.Customer(s);
        var draft=WizardTests.ValidDraft();draft.Brief.CustomerId=customer.Id;
        var id=await s.Projects.SaveDraftAsync(draft);Assert.NotNull(await s.Projects.DetailsAsync(id));
    }
    [Fact]
    public async Task Inactive_country_is_not_suggested_or_accepted_for_project_or_customer()
    {
        await using var s=new ProjectSqlTests.Scope();
        // A transaction protects the shared country; rollback restores it even when an assertion fails.
        await using var tx=await s.Db.Database.BeginTransactionAsync();
        var country=await s.Db.Countries.SingleAsync(x=>x.Code=="BE");country.IsActive=false;
        var customer=await CustomerSubcategoryTests.Customer(s);customer.DefaultCountryId=country.Id;await s.Db.SaveChangesAsync();
        Assert.Null((await new ProjectDictionaryService(s.Db).CustomersAsync()).Single(x=>x.Id==customer.Id).DefaultCountryId);
        Assert.DoesNotContain(await CustomerSubcategoryTests.Dictionaries(s).CountriesAsync(default),x=>x.Id==country.Id);
        var input=(await CustomerSubcategoryTests.Dictionaries(s).GetAsync(DictionaryKind.Customers,customer.Id,default))!;
        await Assert.ThrowsAsync<ValidationException>(()=>CustomerSubcategoryTests.Dictionaries(s).SaveAsync(input,default));
        var draft=WizardTests.ValidDraft();draft.Brief.CustomerId=customer.Id;draft.Brief.CountryId=country.Id;
        await Assert.ThrowsAsync<ValidationException>(()=>s.Projects.SaveDraftAsync(draft));
        await tx.RollbackAsync();s.Db.ChangeTracker.Clear();
    }
    [Fact]
    public async Task Administration_trims_nbsp_without_changing_business_name()
    {
        await using var s=new ProjectSqlTests.Scope();var service=CustomerSubcategoryTests.Dictionaries(s);
        await service.SaveAsync(new(){Kind=DictionaryKind.Customers,Name="\u00a0"+s.Login+"  \u00a0",DefaultCountryId=(await s.Db.Countries.SingleAsync(x=>x.Code=="PL")).Id},default);
        Assert.Equal(s.Login,(await s.Db.Customers.SingleAsync(x=>x.Name==s.Login)).Name);
    }
}
