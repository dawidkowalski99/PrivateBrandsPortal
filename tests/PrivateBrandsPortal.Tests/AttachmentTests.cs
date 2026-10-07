using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PrivateBrandsPortal.Web.Configuration;
using PrivateBrandsPortal.Web.Data;
using PrivateBrandsPortal.Web.Interfaces;
using PrivateBrandsPortal.Web.Models.Enums;
using PrivateBrandsPortal.Web.Services;
using PrivateBrandsPortal.Web.ViewModels;
namespace PrivateBrandsPortal.Tests;

[Collection("SQL integration")]
public sealed class AttachmentTests
{
    [Fact] public async Task Removal_checks_owner_preserves_file_and_audits_soft_delete()
    {
        await using var s=new ProjectSqlTests.Scope();await using var other=new ProjectSqlTests.Scope();using var files=new Files();
        var projectId=await s.Projects.SaveDraftAsync(WizardTests.ValidDraft());
        await files.Service(s).UploadAsync(new(){ProjectId=projectId,Type=AttachmentType.Brief,File=File()},default);
        var row=await s.Db.ProjectAttachments.AsNoTracking().SingleAsync(x=>x.ProjectId==projectId);
        await Assert.ThrowsAsync<PortalAccessException>(()=>files.Service(other).RemoveAsync(row.Id,default));
        Assert.Equal(projectId,await files.Service(s).RemoveAsync(row.Id,default));
        var removed=await s.Db.ProjectAttachments.AsNoTracking().SingleAsync(x=>x.Id==row.Id);
        Assert.NotNull(removed.DeletedAtUtc);Assert.Equal((await s.Users.GetCurrentAsync()).Id,removed.DeletedByUserId);
        Assert.Empty((await files.Service(s).PanelAsync(projectId,default))!.Items);
        await Assert.ThrowsAsync<PortalAccessException>(()=>files.Service(s).DownloadAsync(row.Id,default));
        await using var stored=await files.Storage.OpenAsync(row.StorageKey,default);Assert.Equal(row.FileSize,stored.Length);
        Assert.Single(await s.Db.AuditLogs.Where(x=>x.EntityId==projectId && x.ChangeType==AuditChangeType.AttachmentRemoved).ToListAsync());
    }
    [Fact] public async Task Removal_cannot_invalidate_delivered_sku_documents_and_rolls_back()
    {
        await using var s=new ProjectSqlTests.Scope();using var files=new Files();
        var id=await CommercialWorkflowTests.Submitted(s);await CommercialWorkflowTests.Decide(s,id,0,ReviewDecision.Approved);
        await files.Service(s).UploadAsync(new(){ProjectId=id,Type=AttachmentType.Offer,File=File()},default);
        await files.Service(s).UploadAsync(new(){ProjectId=id,Type=AttachmentType.Calculation,File=File()},default);
        var details=(await s.Projects.DetailsAsync(id))!;
        await CommercialWorkflowTests.Commercial(s).UpdateAsync(new(){ProjectId=id,ProductId=details.Products[0].Id,Version=details.UpdatedAtUtc,Status=CommercialStatus.SalesAndDelivery});
        var offer=await s.Db.ProjectAttachments.AsNoTracking().SingleAsync(x=>x.ProjectId==id && x.AttachmentType==AttachmentType.Offer);
        await Assert.ThrowsAsync<ValidationException>(()=>files.Service(s).RemoveAsync(offer.Id,default));
        Assert.Null((await s.Db.ProjectAttachments.AsNoTracking().SingleAsync(x=>x.Id==offer.Id)).DeletedAtUtc);
        Assert.False(await s.Db.AuditLogs.AnyAsync(x=>x.EntityId==id && x.ChangeType==AuditChangeType.AttachmentRemoved));
    }
    [Fact] public async Task Upload_form_lists_category_and_subcategory_without_sql_collation_conflicts()
    {
        await using var s=new ProjectSqlTests.Scope();using var files=new Files();
        var id=await s.Projects.SaveDraftAsync(WizardTests.ValidDraft());
        var details=(await s.Projects.DetailsAsync(id))!;
        var form=await files.Service(s).UploadFormAsync(new(){ProjectId=id},default);
        Assert.Null(form.Input.ProjectProductId);
        Assert.Equal(details.Products.Count,form.Products.Count);
        foreach(var product in details.Products)
            Assert.Equal($"{product.Category ?? "Legacy category"} — {product.ProductType}",form.Products.Single(x=>x.Id==product.Id).Name);
    }
    [Fact] public async Task Same_original_filename_creates_two_files_and_missing_file_has_safe_error()
    {
        await using var s=new ProjectSqlTests.Scope();using var files=new Files();var id=await s.Projects.SaveDraftAsync(WizardTests.ValidDraft());
        for(var i=0;i<2;i++)await files.Service(s).UploadAsync(new(){ProjectId=id,Type=AttachmentType.Brief,File=File()},default);
        var rows=await s.Db.ProjectAttachments.Where(x=>x.ProjectId==id).ToListAsync();Assert.Equal(2,rows.Count);Assert.Equal(2,rows.Select(x=>x.StorageKey).Distinct().Count());
        await files.Storage.DeleteAsync(rows[0].StorageKey,default);
        var error=await Assert.ThrowsAsync<ValidationException>(()=>files.Service(s).DownloadAsync(rows[0].Id,default));Assert.Equal(AttachmentService.UnavailableMessage,error.Message);
        var remaining=await files.Service(s).DownloadAsync(rows[1].Id,default);await remaining.Stream.DisposeAsync();
    }
    [Theory][InlineData(AttachmentType.Offer)][InlineData(AttachmentType.Brief)]
    public async Task Only_calculation_accepts_product_scope(AttachmentType type)
    {
        await using var s=new ProjectSqlTests.Scope();using var files=new Files();var id=await s.Projects.SaveDraftAsync(WizardTests.ValidDraft());var product=(await s.Projects.DetailsAsync(id))!.Products[0].Id;
        await Assert.ThrowsAsync<ValidationException>(()=>files.Service(s).UploadAsync(new(){ProjectId=id,ProjectProductId=product,Type=type,File=File()},default));
    }
    [Fact] public async Task Superadmin_can_upload_and_download_foreign_project()
    {
        await using var owner=new ProjectSqlTests.Scope();await using var admin=new ProjectSqlTests.Scope();using var files=new Files();await SetRole(admin,AppRole.SuperAdmin);
        var id=await owner.Projects.SaveDraftAsync(WizardTests.ValidDraft());await files.Service(admin).UploadAsync(new(){ProjectId=id,Type=AttachmentType.Calculation,File=File()},default);
        var item=await admin.Db.ProjectAttachments.SingleAsync(x=>x.ProjectId==id);var download=await files.Service(admin).DownloadAsync(item.Id,default);await download.Stream.DisposeAsync();
        Assert.Equal((await admin.Users.GetCurrentAsync()).Id,item.UploadedByUserId);
    }
    internal sealed class Files : IDisposable
    {
        public string Root {get;}=Path.Combine(Path.GetTempPath(),"PortalAttachmentTests",Guid.NewGuid().ToString("N"));
        public IOptions<FileStorageOptions> Options {get;}
        public FileStorage Storage {get;}
        public Files(){Directory.CreateDirectory(Root);Options=Microsoft.Extensions.Options.Options.Create(new FileStorageOptions{RootPath=Root});Storage=new(Options,NullLogger<FileStorage>.Instance);}
        public AttachmentService Service(ProjectSqlTests.Scope s,ApplicationDbContext? db=null,IFileStorage? storage=null)=>
            new(db??s.Db,s.MakeUsers(db??s.Db),storage??Storage,Options,TimeProvider.System,NullLogger<AttachmentService>.Instance);
        public void Dispose()=>Directory.Delete(Root,true);
    }
    internal static IFormFile File(string name="workbook.xlsx",int size=16)=>new FormFile(new MemoryStream(new byte[size]),0,size,"File",name){Headers=new HeaderDictionary(),ContentType="text/html"};
    private static async Task SetRole(ProjectSqlTests.Scope s,AppRole role)
    {var user=await s.Users.GetCurrentAsync();await s.Db.AppUsers.Where(x=>x.Id==user.Id).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.Role,role));s.Db.ChangeTracker.Clear();}
    [Theory][InlineData(".xlsx")][InlineData(".xlsm")][InlineData(".xls")][InlineData(".pdf")][InlineData(".txt")][InlineData(".doc")][InlineData(".docx")]
    public async Task Upload_roundtrip_sanitizes_filename_audits_and_ignores_client_mime(string extension)
    {
        await using var s=new ProjectSqlTests.Scope();using var files=new Files();var service=files.Service(s);var id=await s.Projects.SaveDraftAsync(WizardTests.ValidDraft());
        await service.UploadAsync(new(){ProjectId=id,Type=AttachmentType.Brief,File=File("../../source"+extension)},default);
        var item=await s.Db.ProjectAttachments.SingleAsync(x=>x.ProjectId==id);
        Assert.Equal("source"+extension,item.OriginalFileName);Assert.DoesNotContain("source",item.StorageKey);Assert.NotEqual("text/html",item.ContentType);
        Assert.Equal(64,item.Sha256!.Length);var download=await service.DownloadAsync(item.Id,default);await using var stream=download.Stream;Assert.Equal(16,stream.Length);
        Assert.Single(await s.Db.AuditLogs.Where(x=>x.EntityId==id && x.ChangeType==AuditChangeType.AttachmentUploaded).ToListAsync());
    }
    [Theory][InlineData("file.exe",12)][InlineData("file.xlsx",0)][InlineData("file.xlsx",26*1024*1024)]
    public async Task Invalid_upload_leaves_no_metadata_or_files(string name,int size)
    {
        await using var s=new ProjectSqlTests.Scope();using var files=new Files();var id=await s.Projects.SaveDraftAsync(WizardTests.ValidDraft());
        await Assert.ThrowsAsync<ValidationException>(()=>files.Service(s).UploadAsync(new(){ProjectId=id,Type=AttachmentType.Offer,File=File(name,size)},default));
        Assert.Empty(Directory.GetFiles(files.Root,"*",SearchOption.AllDirectories));Assert.False(await s.Db.ProjectAttachments.AnyAsync(x=>x.ProjectId==id));
    }
    [Fact] public async Task Foreign_pm_cannot_upload_download_or_attach_foreign_sku()
    {
        await using var owner=new ProjectSqlTests.Scope();await using var other=new ProjectSqlTests.Scope();using var files=new Files();
        var id=await owner.Projects.SaveDraftAsync(WizardTests.ValidDraft());var foreign=await other.Projects.SaveDraftAsync(WizardTests.ValidDraft());
        await files.Service(owner).UploadAsync(new(){ProjectId=id,Type=AttachmentType.Brief,File=File()},default);
        var attachment=await owner.Db.ProjectAttachments.SingleAsync(x=>x.ProjectId==id);
        await Assert.ThrowsAsync<PortalAccessException>(()=>files.Service(other).DownloadAsync(attachment.Id,default));
        await Assert.ThrowsAsync<PortalAccessException>(()=>files.Service(other).UploadAsync(new(){ProjectId=id,Type=AttachmentType.Offer,File=File()},default));
        var product=(await other.Projects.DetailsAsync(foreign))!.Products[0].Id;
        await Assert.ThrowsAsync<ValidationException>(()=>files.Service(owner).UploadAsync(new(){ProjectId=id,ProjectProductId=product,Type=AttachmentType.Calculation,File=File()},default));
    }
    [Fact] public async Task Reviewing_manager_reads_brief_only_and_cannot_upload_foreign_project()
    {
        await using var owner=new ProjectSqlTests.Scope();await using var manager=new ProjectSqlTests.Scope();using var files=new Files();await SetRole(manager,AppRole.Manager);
        var id=await owner.Projects.SaveDraftAsync(WizardTests.ValidDraft());
        foreach(var type in Enum.GetValues<AttachmentType>())await files.Service(owner).UploadAsync(new(){ProjectId=id,Type=type,File=File()},default);
        var attachments=await owner.Db.ProjectAttachments.Where(x=>x.ProjectId==id).ToListAsync();
        await Assert.ThrowsAsync<PortalAccessException>(()=>files.Service(manager).DownloadAsync(attachments[0].Id,default));
        await CommercialWorkflowTests.Review(owner).SubmitAsync(id,(await owner.Projects.DetailsAsync(id))!.UpdatedAtUtc);
        var panel=(await files.Service(manager).PanelAsync(id,default))!;Assert.False(panel.CanUpload);Assert.Single(panel.Items);
        foreach(var item in attachments)
            if(item.AttachmentType==AttachmentType.Brief){var result=await files.Service(manager).DownloadAsync(item.Id,default);await result.Stream.DisposeAsync();}
            else await Assert.ThrowsAsync<PortalAccessException>(()=>files.Service(manager).DownloadAsync(item.Id,default));
        await Assert.ThrowsAsync<PortalAccessException>(()=>files.Service(manager).UploadAsync(new(){ProjectId=id,Type=AttachmentType.Brief,File=File()},default));
    }
    [Theory][InlineData(false,false,false,false)][InlineData(true,false,false,false)][InlineData(false,true,false,false)][InlineData(true,true,true,false)][InlineData(true,true,false,true)]
    public async Task Sales_guard_checks_project_offer_and_exact_sku_or_project_calculation(bool offer,bool calculation,bool wrongSku,bool deleted)
    {
        await using var s=new ProjectSqlTests.Scope();using var files=new Files();var id=await CommercialWorkflowTests.Submitted(s);await CommercialWorkflowTests.Decide(s,id,0,ReviewDecision.Approved);
        var details=(await s.Projects.DetailsAsync(id))!;var first=details.Products[0].Id;var second=details.Products[1].Id;
        if(offer)await files.Service(s).UploadAsync(new(){ProjectId=id,Type=AttachmentType.Offer,File=File()},default);
        if(calculation)await files.Service(s).UploadAsync(new(){ProjectId=id,ProjectProductId=wrongSku?second:first,Type=AttachmentType.Calculation,File=File()},default);
        if(deleted)await s.Db.ProjectAttachments.Where(x=>x.ProjectId==id).ExecuteUpdateAsync(x=>x.SetProperty(a=>a.DeletedAtUtc,DateTimeOffset.UtcNow).SetProperty(a=>a.DeletedByUserId,(int?)(s.Db.AppUsers.Where(u=>u.DomainLogin==s.Login).Select(u=>u.Id).First())));
        details=(await s.Projects.DetailsAsync(id))!;
        var error=await Assert.ThrowsAsync<ValidationException>(()=>CommercialWorkflowTests.Commercial(s).UpdateAsync(new(){ProjectId=id,ProductId=first,Version=details.UpdatedAtUtc,Status=CommercialStatus.SalesAndDelivery}));
        if(!offer || deleted)Assert.Contains("An offer attachment is required",error.Message);
        if(!calculation || wrongSku || deleted)Assert.Contains("A calculation attachment is required",error.Message);
        var after=(await s.Projects.DetailsAsync(id))!;Assert.Equal(details.UpdatedAtUtc,after.UpdatedAtUtc);Assert.Null(after.ArchivedAtUtc);Assert.Null(after.Products[0].CommercialStatus);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Matching_documents_allow_delivery_and_archive_preserves_download(bool perSku)
    {
        await using var s=new ProjectSqlTests.Scope();using var files=new Files();var id=await CommercialWorkflowTests.Submitted(s);await CommercialWorkflowTests.Decide(s,id,0,ReviewDecision.Approved);await CommercialWorkflowTests.Decide(s,id,1,ReviewDecision.Rejected,7);
        var p=(await s.Projects.DetailsAsync(id))!.Products[0];
        await files.Service(s).UploadAsync(new(){ProjectId=id,Type=AttachmentType.Offer,File=File()},default);
        await files.Service(s).UploadAsync(new(){ProjectId=id,ProjectProductId=perSku?p.Id:null,Type=AttachmentType.Calculation,File=File()},default);
        await CommercialWorkflowTests.Commercial(s).UpdateAsync(new(){ProjectId=id,ProductId=p.Id,Version=(await s.Projects.DetailsAsync(id))!.UpdatedAtUtc,Status=CommercialStatus.SalesAndDelivery});
        Assert.NotNull((await s.Projects.DetailsAsync(id))!.ArchivedAtUtc);
        var panel=(await files.Service(s).PanelAsync(id,default))!;Assert.False(panel.CanUpload);Assert.Equal(2,panel.Items.Count);
        var calculation=panel.Items.Single(x=>x.Type==AttachmentType.Calculation);
        Assert.Equal(perSku ? $"{p.Category ?? "Legacy category"} — {p.ProductType} · SKU: {p.SKU}" : "Whole project",calculation.ProductLabel);
        var download=await files.Service(s).DownloadAsync(panel.Items[0].Id,default);await download.Stream.DisposeAsync();
    }
    private sealed class AttachmentFailure : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,InterceptionResult<int> result,CancellationToken ct=default)
        {if(data.Context!.ChangeTracker.Entries<PrivateBrandsPortal.Web.Models.Entities.ProjectAttachment>().Any(x=>x.State==EntityState.Added))throw new InvalidOperationException("Deliberate attachment metadata failure.");return base.SavingChangesAsync(data,result,ct);}
    }
    [Fact] public async Task Sql_failure_compensates_permanent_file_and_leaves_project_unchanged()
    {
        await using var s=new ProjectSqlTests.Scope();using var files=new Files();var id=await s.Projects.SaveDraftAsync(WizardTests.ValidDraft());var before=(await s.Projects.DetailsAsync(id))!;
        await using var failing=s.NewContext(new AttachmentFailure());
        await Assert.ThrowsAsync<InvalidOperationException>(()=>files.Service(s,failing).UploadAsync(new(){ProjectId=id,Type=AttachmentType.Offer,File=File()},default));
        Assert.Empty(Directory.GetFiles(files.Root,"*",SearchOption.AllDirectories));Assert.Equal(before.UpdatedAtUtc,(await s.Projects.DetailsAsync(id))!.UpdatedAtUtc);
        Assert.False(await s.Db.ProjectAttachments.AnyAsync(x=>x.ProjectId==id));
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Temporary_brief_promotes_atomically_or_remains_retryable_on_rollback(bool fail)
    {
        await using var s=new ProjectSqlTests.Scope();using var files=new Files();var token=Guid.NewGuid();var input=WizardTests.ValidDraft();input.WizardToken=token;
        input.TemporaryBriefs.AddRange(await files.Service(s).UploadTemporaryAsync(token,[File()],default));
        Assert.Empty(await s.Projects.ListAsync());
        await using var db=s.NewContext(fail?new AttachmentFailure():null);
        var service=new ProjectService(db,s.MakeUsers(db),new ProjectNumberGenerator(db,TimeProvider.System),TimeProvider.System,files.Service(s,db));
        if(fail){await Assert.ThrowsAsync<InvalidOperationException>(()=>service.SaveDraftAsync(input));Assert.Empty(await s.Projects.ListAsync());Assert.Single(input.TemporaryBriefs);}
        else {var id=await service.SaveDraftAsync(input);Assert.Single(await s.Db.ProjectAttachments.Where(x=>x.ProjectId==id && x.AttachmentType==AttachmentType.Brief).ToListAsync());Assert.Empty(input.TemporaryBriefs);}
        Assert.Single(Directory.GetFiles(files.Root,"*",SearchOption.AllDirectories));
    }
    [Fact] public async Task Temporary_brief_cannot_be_promoted_by_other_owner_or_token()
    {
        await using var s=new ProjectSqlTests.Scope();await using var other=new ProjectSqlTests.Scope();using var files=new Files();var token=Guid.NewGuid();
        var temp=await files.Service(s).UploadTemporaryAsync(token,[File()],default);
        var input=WizardTests.ValidDraft();input.WizardToken=token;input.TemporaryBriefs.AddRange(temp);
        var service=new ProjectService(other.Db,other.Users,new ProjectNumberGenerator(other.Db,TimeProvider.System),TimeProvider.System,files.Service(other));
        await Assert.ThrowsAsync<ValidationException>(()=>service.SaveDraftAsync(input));Assert.Empty(await other.Projects.ListAsync());
    }
    [Fact] public async Task Unavailable_storage_returns_safe_error_without_metadata()
    {
        await using var s=new ProjectSqlTests.Scope();using var files=new Files();var id=await s.Projects.SaveDraftAsync(WizardTests.ValidDraft());
        Directory.Delete(files.Root);var error=await Assert.ThrowsAsync<ValidationException>(()=>files.Service(s).UploadAsync(new(){ProjectId=id,Type=AttachmentType.Offer,File=File()},default));
        Assert.Equal(AttachmentService.UnavailableMessage,error.Message);Assert.DoesNotContain(files.Root,error.Message);Directory.CreateDirectory(files.Root);
        Assert.False(await s.Db.ProjectAttachments.AnyAsync(x=>x.ProjectId==id));
    }
}
