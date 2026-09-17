using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrivateBrandsPortal.Web.Models.Entities;
using PrivateBrandsPortal.Web.Models.Enums;

namespace PrivateBrandsPortal.Web.Data.Configurations;

public sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> b)
    {
        b.ToTable("AppUsers", t => t.HasCheckConstraint("CK_AppUsers_Role", "[Role] IN ('ProjectManager','Manager','Admin')"));
        b.HasKey(x => x.Id);
        b.Property(x => x.DomainLogin).IsRequired().HasMaxLength(256).UseCollation("Latin1_General_100_CI_AS");
        b.HasIndex(x => x.DomainLogin).IsUnique();
        b.Property(x => x.DisplayName).IsRequired().HasMaxLength(200);
        b.Property(x => x.Email).HasMaxLength(254);
        b.Property(x => x.Department).HasMaxLength(100);
        b.Property(x => x.Role).HasConversion<string>().HasMaxLength(32);
    }
}

