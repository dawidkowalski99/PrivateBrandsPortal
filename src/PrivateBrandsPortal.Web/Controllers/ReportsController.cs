using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PrivateBrandsPortal.Web.Services;
using PrivateBrandsPortal.Web.ViewModels;
namespace PrivateBrandsPortal.Web.Controllers;
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class ReportsController(ReportService reports):Controller
{
    [Authorize(Policy=PermissionCodes.ViewReports)]
    public async Task<IActionResult> Index(ReportFilter filter,CancellationToken ct){if(!ModelState.IsValid){var page=await reports.GetAsync(new(),ct);page.Filter=filter;page.Rows=[];page.Managers=[];page.Totals=new();return View(page);}return View(await reports.GetAsync(filter,ct));}
    [Authorize(Policy=PermissionCodes.ExportReports)]
    public async Task<IActionResult> Export(ReportFilter filter,CancellationToken ct){if(!ModelState.IsValid)return BadRequest(ModelState);var rows=await reports.ExportAsync(filter,ct);Response.ContentType="text/csv; charset=utf-8";Response.Headers.ContentDisposition="attachment; filename=private-brands-report.csv";await ReportCsv.WriteAsync(Response.Body,rows,ct);return new EmptyResult();}
}
