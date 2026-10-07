using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using PrivateBrandsPortal.Web.Data;
using PrivateBrandsPortal.Web.Models.Entities;
using PrivateBrandsPortal.Web.Models.Enums;
using PrivateBrandsPortal.Web.ViewModels;
namespace PrivateBrandsPortal.Web.Services;

public sealed class SuperAdminBootstrap(ApplicationDbContext db,TimeProvider clock)
{
    public async Task<int> RunAsync(string login,string name,CancellationToken ct=default)
    {
        var input=new UserCreateInput {DomainLogin=login.Trim(),DisplayName=name.Trim(),Role=AppRole.SuperAdmin};
        Validator.ValidateObject(input,new ValidationContext(input),true);
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync(UserAdministrationService.AdministrationLock,ct);
        // This command only initializes an empty allow-list; it never elevates an existing account.
        if(await db.AppUsers.AnyAsync(ct))throw new ValidationException("Bootstrap requires an empty AppUsers table. Use Administration to manage existing accounts.");
        var now=clock.GetUtcNow();
        var user=new AppUser {DomainLogin=input.DomainLogin,DisplayName=input.DisplayName,Role=AppRole.SuperAdmin,IsActive=true,CreatedAtUtc=now,UpdatedAtUtc=now};
        db.AppUsers.Add(user);await db.SaveChangesAsync(ct);
        db.AuditLogs.Add(new AuditLog {EntityType=nameof(AppUser),EntityId=user.Id,FieldName="UserCreated",
            NewValue=System.Text.Json.JsonSerializer.Serialize(new{user.DomainLogin,user.Role}),ChangedByUserId=user.Id,ChangedAtUtc=now,
            ChangeType=AuditChangeType.Created,Reason="Explicit first SuperAdmin bootstrap command; executed by server operator."});
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return user.Id;
    }
}
