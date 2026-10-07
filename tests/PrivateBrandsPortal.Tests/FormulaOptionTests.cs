using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using PrivateBrandsPortal.Web.Models.Entities;
using PrivateBrandsPortal.Web.Services;
using PrivateBrandsPortal.Web.ViewModels;
namespace PrivateBrandsPortal.Tests;

[Collection("SQL integration")]
public sealed class FormulaOptionTests
{
    [Fact] public async Task Seed_codes_names_and_order_are_correct()
    {
        await using var s=new ProjectSqlTests.Scope();
        var rows=await s.Db.FormulaOptions.AsNoTracking().Where(x=>new[]{"NEW_DEVELOPMENT","NEW_FORMULA","READY_TO_GO"}.Contains(x.Code)).OrderBy(x=>x.DisplayOrder).ToListAsync();
        Assert.Equal(new[]{"NEW_DEVELOPMENT","NEW_FORMULA","READY_TO_GO"},rows.Select(x=>x.Code));
        Assert.Equal(new[]{"New development (extended timeline)","New formula (standard timeline)","Ready to go"},rows.Select(x=>x.Name));
        Assert.All(rows,x=>Assert.True(x.IsActive));
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task New_products_require_existing_active_formula(bool inactive)
    {
        await using var s=new ProjectSqlTests.Scope();var input=WizardTests.ValidDraft();
        if(inactive){var option=new FormulaOption{Code="TEST_"+Guid.NewGuid().ToString("N"),Name=s.Login+" inactive",IsActive=false};s.Db.Add(option);await s.Db.SaveChangesAsync();input.Products[0].FormulaOptionId=option.Id;}
        else input.Products[0].FormulaOptionId=null;
        await Assert.ThrowsAsync<ValidationException>(()=>s.Projects.SaveDraftAsync(input));
        Assert.Empty(await s.Projects.ListAsync());
    }
    [Fact] public async Task New_development_persists_displays_and_copies_without_SKU()
    {
        await using var s=new ProjectSqlTests.Scope();var draft=WizardTests.ValidDraft();draft.Products[0].FormulaOptionId=DictionaryFixture.DevelopmentFormulaId;
        var id=await s.Projects.SaveDraftAsync(draft);var product=(await s.Projects.DetailsAsync(id))!.Products[0];
        Assert.Equal("New development (extended timeline)",product.FormulaName);
        var copy=await new ProductCopyService(s.Db,s.Users).CopyAsync(product.Id,default);
        Assert.NotNull(copy);Assert.Equal(DictionaryFixture.DevelopmentFormulaId,copy.FormulaOptionId);Assert.Empty(copy.SKU);
        Assert.Equal(copy.FormulaOptionId,ProductCopyService.Duplicate(copy).FormulaOptionId);
    }
    [Fact] public async Task Used_formula_code_is_stable_name_can_change_and_inactive_existing_reference_is_preserved()
    {
        await using var s=new ProjectSqlTests.Scope();var service=new DictionaryService(s.Db,new PermissionService(new PolicyAppUser("SuperAdmin")),TimeProvider.System);
        var code="TEST_"+Guid.NewGuid().ToString("N");await service.SaveAsync(new(){Kind=DictionaryKind.FormulaOptions,Code=code,Name=s.Login+" formula"},default);
        var option=await s.Db.FormulaOptions.SingleAsync(x=>x.Code==code);var draft=WizardTests.ValidDraft();draft.Products[0].FormulaOptionId=option.Id;
        var id=await s.Projects.SaveDraftAsync(draft);
        var edit=(await service.GetAsync(DictionaryKind.FormulaOptions,option.Id,default))!;edit.Code="OTHER_"+Guid.NewGuid().ToString("N");
        await Assert.ThrowsAsync<ValidationException>(()=>service.SaveAsync(edit,default));
        edit.Code=code;edit.Name=s.Login+" renamed";edit.IsActive=false;await service.SaveAsync(edit,default);
        var saved=(await s.Projects.LoadDraftAsync(id))!;await s.Projects.SaveDraftAsync(saved);
        Assert.Equal(edit.Name,(await s.Projects.DetailsAsync(id))!.Products[0].FormulaName);
        Assert.DoesNotContain(await new ProjectDictionaryService(s.Db).FormulasAsync(),x=>x.Id==option.Id);
    }
    [Fact] public async Task Legacy_null_dictionary_reference_still_renders_and_survives_edit()
    {
        await using var s=new ProjectSqlTests.Scope();var id=await s.Projects.SaveDraftAsync(WizardTests.ValidDraft());
        await s.Db.ProjectProducts.Where(x=>x.ProjectId==id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.FormulaOptionId,(int?)null));s.Db.ChangeTracker.Clear();
        var edit=(await s.Projects.LoadDraftAsync(id))!;await s.Projects.SaveDraftAsync(edit);
        Assert.All((await s.Projects.DetailsAsync(id))!.Products,x=>Assert.Null(x.FormulaName));
    }
}
