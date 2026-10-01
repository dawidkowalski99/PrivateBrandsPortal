using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrivateBrandsPortal.Web.Models.Entities;
using PrivateBrandsPortal.Web.Services;

namespace PrivateBrandsPortal.Web.Data.Configurations;

public sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> b)
    {
        b.Property(x => x.Code).HasMaxLength(64).IsRequired().UseCollation("Latin1_General_100_CI_AS");
        b.HasIndex(x => x.Code).IsUnique();
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.Description).HasMaxLength(1000);
        b.HasData(
            new Permission { Id = 1, Code = PermissionCodes.ManageUsers, Name = "Manage users", DisplayOrder = 10 },
            new Permission { Id = 2, Code = PermissionCodes.ManageDictionaries, Name = "Manage dictionaries", DisplayOrder = 20 },
            new Permission { Id = 3, Code = PermissionCodes.ViewReports, Name = "View reports", DisplayOrder = 30 },
            new Permission { Id = 4, Code = PermissionCodes.ExportReports, Name = "Export reports", DisplayOrder = 40 });
    }
}

public sealed class AppUserPermissionConfiguration : IEntityTypeConfiguration<AppUserPermission>
{
    public void Configure(EntityTypeBuilder<AppUserPermission> b)
    {
        b.HasKey(x => new { x.AppUserId, x.PermissionId });
        b.HasOne(x => x.AppUser).WithMany(x => x.Permissions).HasForeignKey(x => x.AppUserId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.Permission).WithMany().HasForeignKey(x => x.PermissionId).OnDelete(DeleteBehavior.NoAction);
    }
}
