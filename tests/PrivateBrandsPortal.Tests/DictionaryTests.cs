using System.ComponentModel.DataAnnotations;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using PrivateBrandsPortal.Web.Interfaces;
using PrivateBrandsPortal.Web.Services;
using PrivateBrandsPortal.Web.ViewModels;
namespace PrivateBrandsPortal.Tests;
[Collection("SQL integration")]
public sealed class DictionaryTests
{
    [Theory]
    [InlineData(DictionaryKind.ProductCategories)]
    [InlineData(DictionaryKind.RejectionReasons)]
    public async Task Admin_can_create_edit_deactivate_and_conflicting_edits_are_rejected(DictionaryKind kind)
    {
        await using var s=new ProjectSqlTests.Scope();var service=new DictionaryService(s.Db,new PolicyAppUser("Admin"),TimeProvider.System);
        var input=new DictionaryInput{Kind=kind,Name=s.Login+" entry",DisplayOrder=11,RequiresComment=kind==DictionaryKind.RejectionReasons};
        await service.SaveAsync(input,default);var saved=(await service.ListAsync(kind,default)).Items.Single(x=>x.Name==input.Name);
        var stale=await service.GetAsync(kind,saved.Id,default);saved.IsActive=false;saved.DisplayOrder=22;saved.Description="Test description";saved.RequiresComment=false;
        await service.SaveAsync(saved,default);var updated=(await service.GetAsync(kind,saved.Id,default))!;
        Assert.False(updated.IsActive);Assert.Equal(22,updated.DisplayOrder);Assert.Equal("Test description",updated.Description);
        if(kind==DictionaryKind.RejectionReasons)Assert.True(updated.RequiresComment);
        await Assert.ThrowsAsync<ValidationException>(()=>service.SaveAsync(stale!,default));
        await Assert.ThrowsAsync<ValidationException>(()=>service.SaveAsync(input,default));
        var denied=new DictionaryService(s.Db,s.Users,TimeProvider.System);await Assert.ThrowsAsync<PortalAccessException>(()=>denied.SaveAsync(updated,default));
    }
    [Theory]
    [InlineData("/Dictionaries?kind=ProductCategories","Product Categories")]
    [InlineData("/Dictionaries?kind=RejectionReasons","Rejection Reasons")]
    [InlineData("/Dictionaries/Edit?kind=ProductCategories","Display Order")]
    [InlineData("/Dictionaries/Edit?kind=RejectionReasons","Comment required")]
    public async Task Admin_pages_render_and_normal_pm_is_denied(string path,string expected)
    {
        await using var baseline=new AuthenticatedFactory();using var denied=baseline.CreateClient();denied.DefaultRequestHeaders.Add("X-Test-User","reader");
        Assert.Equal(HttpStatusCode.Forbidden,(await denied.GetAsync(path)).StatusCode);
        await using var admin=baseline.WithWebHostBuilder(b=>b.ConfigureServices(s=>s.AddScoped<IAppUserService>(_=>new PolicyAppUser("Admin"))));
        using var client=admin.CreateClient();client.DefaultRequestHeaders.Add("X-Test-User","reader");var response=await client.GetAsync(path);Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Contains(expected,await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsync("/Dictionaries/Edit",new FormUrlEncodedContent([]))).StatusCode);
    }
}
