using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PrivateBrandsPortal.Web.Configuration;
using PrivateBrandsPortal.Web.Data;
using PrivateBrandsPortal.Web.Interfaces;
using PrivateBrandsPortal.Web.Models.Entities;
using PrivateBrandsPortal.Web.Models.Enums;
using PrivateBrandsPortal.Web.ViewModels;

namespace PrivateBrandsPortal.Web.Services;

public sealed class AttachmentService(ApplicationDbContext db, IAppUserService users, IFileStorage storage,
    IOptions<FileStorageOptions> options, TimeProvider clock, ILogger<AttachmentService> logger)
{
    public const string UnavailableMessage = "Attachment storage is unavailable. Please try again later or contact IT.";
    private static bool CanWrite(Project project, AppUser actor) => actor.IsActive &&
        (actor.Role == AppRole.SuperAdmin || project.ProjectManagerId == actor.Id);
    private static bool CanReviewBrief(Project project, AppUser actor) => actor.IsActive && actor.Role == AppRole.Manager &&
        project.RequiresManagerApproval && project.Status is ProjectStatus.AwaitingManagerReview or ProjectStatus.PartiallyReviewed or
            ProjectStatus.Approved or ProjectStatus.Rejected or ProjectStatus.PartiallyApproved;

    private (string Name, string Extension, string ContentType) ValidateFile(IFormFile file)
    {
        var name = Path.GetFileName(file.FileName.Replace('\\', '/')).Trim();
        name = new string(name.Where(c => !char.IsControl(c)).ToArray());
        var extension = Path.GetExtension(name).ToLowerInvariant();
        if (name.Length is 0 or > 255 || !options.Value.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)
            || extension is not (".xlsx" or ".xlsm" or ".xls"))
            throw new ValidationException("Choose an Excel attachment (.xlsx, .xlsm or .xls).");
        if (options.Value.MaxBytes <= 0 || file.Length <= 0 || file.Length > options.Value.MaxBytes)
            throw new ValidationException($"Choose a non-empty attachment up to {options.Value.MaxFileSizeMb} MB.");
        var contentType = extension switch {
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".xlsm" => "application/vnd.ms-excel.sheet.macroEnabled.12", _ => "application/vnd.ms-excel" };
        return (name, extension, contentType);
    }
    private async Task<T> Storage<T>(Func<Task<T>> action)
    {
        try { return await action(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError("Attachment storage operation failed ({ExceptionType}).", ex.GetType().Name);
            throw new ValidationException(UnavailableMessage);
        }
    }
    public async Task CleanupFilesAsync(IEnumerable<string> keys)
    {
        foreach (var key in keys)
            try { await storage.DeleteAsync(key, CancellationToken.None); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { logger.LogError("Attachment compensation needs retry ({ExceptionType}); storage key {StorageKey}.", ex.GetType().Name, key); }
    }

    public async Task<IReadOnlyList<TemporaryBrief>> UploadTemporaryAsync(Guid token, IReadOnlyList<IFormFile> files, CancellationToken ct)
    {
        var actor = await users.GetCurrentAsync(ct);
        if (!WorkflowAccess.Allows(actor, AppRole.ProjectManager) || token == Guid.Empty) throw new PortalAccessException();
        if (files.Count > 10) throw new ValidationException("Upload at most 10 brief attachments at a time.");
        var result = new List<TemporaryBrief>();
        try
        {
            foreach (var file in files)
            {
                var info = ValidateFile(file);
                var key = $"temp/{actor.Id}/{token:N}/{Guid.NewGuid():N}{info.Extension}";
                await using var source = file.OpenReadStream();
                var stored = await Storage(() => storage.SaveAsync(key, source, options.Value.MaxBytes, ct));
                result.Add(new(key, info.Name, info.ContentType, stored.Size, stored.Sha256, actor.Id, token, clock.GetUtcNow()));
            }
            return result;
        }
        catch { await CleanupFilesAsync(result.Select(x => x.StorageKey)); throw; }
    }

    // Caller owns the SQL transaction; keys are returned through a ledger for compensation on rollback.
    public async Task PromoteBriefsAsync(Project project, IReadOnlyList<TemporaryBrief> files, Guid token,
        List<string> createdKeys, CancellationToken ct)
    {
        var actor = await users.GetCurrentAsync(ct);
        if (!CanWrite(project, actor)) throw new PortalAccessException();
        foreach (var file in files)
        {
            if (file.OwnerId != actor.Id || file.WizardToken != token || token == Guid.Empty ||
                !file.StorageKey.StartsWith($"temp/{actor.Id}/{token:N}/", StringComparison.Ordinal))
                throw new ValidationException("The temporary brief is unavailable in this workspace.");
            var key = $"projects/{project.Id}/{Guid.NewGuid():N}{Path.GetExtension(file.StorageKey)}";
            await using var source = await Storage(() => storage.OpenAsync(file.StorageKey, ct));
            var stored = await Storage(() => storage.SaveAsync(key, source, options.Value.MaxBytes, ct));
            createdKeys.Add(key);
            if (stored.Size != file.Size || stored.Sha256 != file.Sha256)
                throw new ValidationException("The temporary brief changed. Upload it again.");
            AddMetadata(project.Id, null, AttachmentType.Brief, file.FileName, file.ContentType, key,
                stored, actor.Id, null, file.UploadedAtUtc);
        }
    }
    private void AddMetadata(int projectId, int? productId, AttachmentType type, string name, string contentType,
        string key, StoredFile stored, int actorId, string? description, DateTimeOffset uploadedAt, string? sku = null)
    {
        db.ProjectAttachments.Add(new ProjectAttachment { ProjectId=projectId, ProjectProductId=productId,
            AttachmentType=type, OriginalFileName=name, ContentType=contentType, StorageKey=key, FileSize=stored.Size,
            Sha256=stored.Sha256, UploadedByUserId=actorId, UploadedAtUtc=uploadedAt, Description=description });
        db.AuditLogs.Add(new AuditLog { EntityType=nameof(Project), EntityId=projectId, FieldName="Attachment",
            NewValue=System.Text.Json.JsonSerializer.Serialize(new { Type=type.ToString(), FileName=name, ProjectProductId=productId, SKU=sku }),
            ChangedByUserId=actorId, ChangedAtUtc=clock.GetUtcNow(), ChangeType=AuditChangeType.AttachmentUploaded });
    }
    public async Task<AttachmentUploadModel> UploadFormAsync(AttachmentInput input, CancellationToken ct)
    {
        var actor = await users.GetCurrentAsync(ct);
        var project = await db.Projects.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==input.ProjectId,ct);
        if (project is null || !CanWrite(project,actor)) throw new PortalAccessException();
        if (project.ArchivedAtUtc.HasValue) throw new ValidationException("Archived project attachments are read-only.");
        var products = await db.ProjectProducts.AsNoTracking().Where(x=>x.ProjectId==input.ProjectId)
            .OrderBy(x=>x.SKU).Select(x=>new LookupItem(x.Id,x.SKU)).ToListAsync(ct);
        return new(input,products);
    }
    public async Task UploadAsync(AttachmentInput input, CancellationToken ct)
    {
        Validator.ValidateObject(input,new ValidationContext(input),true);
        var actor = await users.GetCurrentAsync(ct);
        if (!actor.IsActive) throw new PortalAccessException();
        var info = ValidateFile(input.File!);
        if (input.ProjectProductId.HasValue && input.Type != AttachmentType.Calculation)
            throw new ValidationException("Only Calculation may be attached to an individual product.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Same parent lock as commercial transitions and transfers: authorization stays valid until commit.
        var now = clock.GetUtcNow();
        var changed = await db.Projects.Where(x=>x.Id==input.ProjectId && x.ArchivedAtUtc==null &&
                (x.ProjectManagerId==actor.Id || actor.Role==AppRole.SuperAdmin))
            .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.UpdatedAtUtc,x=>x.UpdatedAtUtc < now ? now : x.UpdatedAtUtc.AddMilliseconds(1)),ct);
        if (changed!=1) throw new PortalAccessException();
        if (input.ProjectProductId.HasValue && !await db.ProjectProducts.AnyAsync(x=>x.Id==input.ProjectProductId && x.ProjectId==input.ProjectId,ct))
            throw new ValidationException("The selected product does not belong to this project.");
        var key = $"projects/{input.ProjectId}/{Guid.NewGuid():N}{info.Extension}";
        var saved = false;
        try
        {
            await using var source=input.File!.OpenReadStream();
            var stored=await Storage(()=>storage.SaveAsync(key,source,options.Value.MaxBytes,ct));
            saved=true;
            var sku=input.ProjectProductId.HasValue ? await db.ProjectProducts.Where(x=>x.Id==input.ProjectProductId).Select(x=>x.SKU).SingleAsync(ct) : null;
            AddMetadata(input.ProjectId,input.ProjectProductId,input.Type!.Value,info.Name,info.ContentType,key,stored,actor.Id,input.Description?.Trim(),clock.GetUtcNow(),sku);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch { if(saved) await CleanupFilesAsync([key]); throw; }
    }
    public async Task<(Stream Stream,string ContentType,string Name)> DownloadAsync(int id,CancellationToken ct)
    {
        var actor=await users.GetCurrentAsync(ct);
        var file=await db.ProjectAttachments.AsNoTracking().Include(x=>x.Project).SingleOrDefaultAsync(x=>x.Id==id && x.DeletedAtUtc==null,ct);
        if(file is null || !(CanWrite(file.Project,actor) || (file.AttachmentType==AttachmentType.Brief && CanReviewBrief(file.Project,actor))))
            throw new PortalAccessException();
        return (await Storage(()=>storage.OpenAsync(file.StorageKey,ct)),file.ContentType,file.OriginalFileName);
    }
    public async Task<AttachmentPanel?> PanelAsync(int id,CancellationToken ct)
    {
        var actor=await users.GetCurrentAsync(ct);
        var project=await db.Projects.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,ct);
        if(project is null)return null;
        var owner=CanWrite(project,actor);
        if(!owner && !CanReviewBrief(project,actor))return null;
        var query=db.ProjectAttachments.AsNoTracking().Where(x=>x.ProjectId==id && x.DeletedAtUtc==null);
        if(!owner)query=query.Where(x=>x.AttachmentType==AttachmentType.Brief);
        var items=await query.OrderBy(x=>x.AttachmentType).ThenByDescending(x=>x.UploadedAtUtc)
            .Select(x=>new AttachmentItem(x.Id,x.AttachmentType,x.OriginalFileName,x.FileSize,x.UploadedByUser.DisplayName,x.UploadedAtUtc,
                x.ProjectProduct==null?null:x.ProjectProduct.SKU,x.Description)).ToListAsync(ct);
        var requirements=owner ? await db.ProjectProducts.AsNoTracking().Where(x=>x.ProjectId==id && x.CommercialStatus==CommercialStatus.ImplementationIntoProduction)
            .Select(x=>new AttachmentRequirement(x.Id,x.SKU,
                db.ProjectAttachments.Any(a=>a.ProjectId==id && a.DeletedAtUtc==null && a.AttachmentType==AttachmentType.Offer && a.ProjectProductId==null),
                db.ProjectAttachments.Any(a=>a.ProjectId==id && a.DeletedAtUtc==null && a.AttachmentType==AttachmentType.Calculation && (a.ProjectProductId==null || a.ProjectProductId==x.Id))))
            .ToListAsync(ct) : [];
        return new(id,owner && project.ArchivedAtUtc==null,items,requirements);
    }
    public static async Task RequireSalesDocumentsAsync(ApplicationDbContext db,int projectId,int productId,CancellationToken ct)
    {
        var documents=await db.ProjectAttachments.AsNoTracking().Where(x=>x.ProjectId==projectId && x.DeletedAtUtc==null)
            .Select(x=>new{x.AttachmentType,x.ProjectProductId}).ToListAsync(ct);
        var errors=new List<string>();
        if(!documents.Any(x=>x.AttachmentType==AttachmentType.Offer && x.ProjectProductId==null))
            errors.Add("An offer attachment is required before Sales & Delivery.");
        if(!documents.Any(x=>x.AttachmentType==AttachmentType.Calculation && (x.ProjectProductId==null || x.ProjectProductId==productId)))
            errors.Add("A calculation attachment is required before Sales & Delivery.");
        if(errors.Count>0)throw new ValidationException(string.Join("\n",errors));
    }
}
