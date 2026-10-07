using System.Text;
using Microsoft.EntityFrameworkCore;
using PrivateBrandsPortal.Web.Models.Enums;
using PrivateBrandsPortal.Web.Services;
using PrivateBrandsPortal.Web.ViewModels;
namespace PrivateBrandsPortal.Tests;
[Collection("SQL integration")]
public sealed class ReportTests
{
    private static ReportService Reports(ProjectSqlTests.Scope s)=>new(s.Db,new PermissionService(new PolicyAppUser("SuperAdmin")),new PolicyAppUser("SuperAdmin"));
    private static async Task<int> Fixture(ProjectSqlTests.Scope s)
    {
        var first=WizardTests.ValidDraft();first.Brief.Customer=s.Login+" Żółć; \"Demo\"";first.Brief.CountryId=2;
        var id=await CommercialWorkflowTests.Submitted(s,first);await s.Db.Projects.Where(x=>x.Id==id).ExecuteUpdateAsync(x=>x.SetProperty(p=>p.Customer,s.Login+" Żółć; \"Demo\""));await CommercialWorkflowTests.Decide(s,id,0,ReviewDecision.Approved);await CommercialWorkflowTests.Decide(s,id,1,ReviewDecision.Rejected,7);await CommercialWorkflowTests.Status(s,id,0,CommercialStatus.SalesAndDelivery);
        var second=WizardTests.ValidDraft();second.Brief.Customer=s.Login+" active";second.Brief.CountryId=3;var secondId=await s.Projects.SaveDraftAsync(second);await s.Db.Projects.Where(x=>x.Id==secondId).ExecuteUpdateAsync(x=>x.SetProperty(p=>p.Customer,s.Login+" active"));
        await s.Db.Projects.Where(p=>p.Id==secondId).ExecuteUpdateAsync(x=>x.SetProperty(p=>p.CreatedAtUtc,new DateTimeOffset(2025,1,1,0,0,0,TimeSpan.Zero)));
        await s.Db.Projects.Where(p=>p.Id==id).ExecuteUpdateAsync(x=>x.SetProperty(p=>p.CreatedAtUtc,new DateTimeOffset(2026,9,30,23,59,59,TimeSpan.Zero)));
        return id;
    }
    [Fact]
    public async Task Distinct_projects_skus_and_pm_aggregation_agree()
    {
        await using var s=new ProjectSqlTests.Scope();await using var other=new ProjectSqlTests.Scope();await Fixture(s);
        var draft=WizardTests.ValidDraft();draft.Brief.Customer=s.Login;var otherId=await other.Projects.SaveDraftAsync(draft);await s.Db.Projects.Where(x=>x.Id==otherId).ExecuteUpdateAsync(x=>x.SetProperty(p=>p.Customer,s.Login));
        var page=await Reports(s).GetAsync(new(){Customer=s.Login});
        Assert.Equal(3,page.Totals.Projects);Assert.Equal(6,page.Totals.SKUs);Assert.Equal(2,page.Managers.Count);
        Assert.Equal(6,page.Managers.Sum(x=>x.Totals.SKUs));Assert.Equal(3,page.Managers.Sum(x=>x.Totals.Projects));
        Assert.Equal(1,page.Totals.Approved);Assert.Equal(1,page.Totals.Rejected);Assert.Equal(1,page.Totals.Delivered);
    }
    [Theory]
    [InlineData("PM",4)]
    [InlineData("Country",2)]
    [InlineData("Category",2)]
    [InlineData("Commercial",1)]
    [InlineData("Review",1)]
    [InlineData("From",2)]
    [InlineData("To",2)]
    [InlineData("SameDay",2)]
    [InlineData("Archived",2)]
    [InlineData("Active",2)]
    [InlineData("Subcategory",2)]
    [InlineData("Customer",2)]
    [InlineData("ProjectStatus",2)]
    [InlineData("Combined",1)]
    public async Task Sql_filters_apply_to_details_kpis_and_csv(string filter,int expected)
    {
        await using var s=new ProjectSqlTests.Scope();await Fixture(s);var user=await s.Users.GetCurrentAsync();var f=new ReportFilter{ProjectManagerId=user.Id};
        switch(filter){
            case "Country":f.CountryId=2;break;case "Category":f.ProductCategoryId=1;break;
            case "Commercial":f.CommercialStatus=CommercialStatus.SalesAndDelivery;break;case "Review":f.ReviewStatus=ProductReviewStatus.Rejected;break;
            case "From":f.DateFrom=new(2026,1,1);break;case "To":f.DateTo=new(2025,1,1);break;
            case "SameDay":f.DateFrom=new(2026,9,30);f.DateTo=new(2026,9,30);break;
            case "Archived":f.Archived=ArchiveFilter.Archived;break;case "Active":f.Archived=ArchiveFilter.Active;break;
            case "Subcategory":f.Subcategory="Shampoo";break;case "Customer":f.Customer="Żółć";break;
            case "ProjectStatus":f.ProjectStatus=ProjectStatus.PartiallyApproved;break;
            case "Combined":f.CountryId=2;f.ProductCategoryId=1;f.ReviewStatus=ProductReviewStatus.Approved;f.CommercialStatus=CommercialStatus.SalesAndDelivery;f.Archived=ArchiveFilter.Archived;break;
        }
        var page=await Reports(s).GetAsync(f);Assert.Equal(expected,page.Totals.SKUs);Assert.Equal(expected,page.Rows.Count);Assert.Single(page.Managers);
        using var stream=new MemoryStream();await ReportCsv.WriteAsync(stream,await Reports(s).ExportAsync(f),default);
        var bytes=stream.ToArray();Assert.Equal(new byte[]{239,187,191},bytes[..3]);var csv=Encoding.UTF8.GetString(bytes);
        Assert.Equal(expected+1,csv.Split('\n',StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.StartsWith("\uFEFFProject Number;Project Manager",csv);
        if(page.Rows.Any(x=>x.Customer.Contains("Żółć")))Assert.Contains("Żółć; \"\"Demo\"\"",csv);
        if(page.Rows.Any(x=>x.SKU=="SKU-1")) Assert.Contains("150000,00",csv);
    }
    [Fact]
    public async Task Pagination_keeps_global_totals_and_export_includes_all_rows()
    {
        await using var s=new ProjectSqlTests.Scope();var draft=WizardTests.ValidDraft();var template=draft.Products[0];draft.Products=Enumerable.Range(1,55).Select(i=>new ProductInput{ProductCategoryId=1,ProductSubcategoryId=DictionaryFixture.ShampooId,Subcategory="Shampoo",SKU="PAGE-"+i,Quantity=1,EstimatedValue=12.34m,EstimatedMargin=10,FormulaOptionId=DictionaryFixture.ReadyFormulaId,FormulaStatus=FormulaStatus.ReadyToGo}).ToList();await s.Projects.SaveDraftAsync(draft);
        var f=new ReportFilter{ProjectManagerId=(await s.Users.GetCurrentAsync()).Id};var first=await Reports(s).GetAsync(f);f.Page=2;var second=await Reports(s).GetAsync(f);
        Assert.Equal(50,first.Rows.Count);Assert.Equal(5,second.Rows.Count);Assert.Equal(55,second.Totals.SKUs);Assert.Equal(1,second.Totals.Projects);
        using var stream=new MemoryStream();await ReportCsv.WriteAsync(stream,await Reports(s).ExportAsync(f),default);Assert.Equal(56,Encoding.UTF8.GetString(stream.ToArray()).Split('\n',StringSplitOptions.RemoveEmptyEntries).Length);
    }
    [Theory]
    [InlineData("=HYPERLINK(\"x\")","\"'=HYPERLINK(\"\"x\"\")\"")]
    [InlineData(" a;\"b\"\nc","\" a;\"\"b\"\"\nc\"")]
    [InlineData("Żółć","\"Żółć\"")]
    [InlineData("  @SUM(A1)","\"'  @SUM(A1)\"")]
    public void Csv_escapes_unicode_quotes_newlines_and_formulas(string raw,string expected)=>Assert.Equal(expected,ReportCsv.Cell(raw));
}
