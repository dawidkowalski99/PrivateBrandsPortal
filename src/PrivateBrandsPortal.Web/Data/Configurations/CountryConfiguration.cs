using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrivateBrandsPortal.Web.Models.Entities;
using PrivateBrandsPortal.Web.Models.Enums;

namespace PrivateBrandsPortal.Web.Data.Configurations;

public sealed class CountryConfiguration : IEntityTypeConfiguration<Country>
{
    public void Configure(EntityTypeBuilder<Country> b)
    {
        b.ToTable("Countries");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).IsRequired().HasMaxLength(100);
        b.Property(x => x.Code).IsRequired().HasMaxLength(2).IsUnicode(false).UseCollation("Latin1_General_100_CI_AS");
        b.HasIndex(x => x.Code).IsUnique();
        b.HasData(
            new Country { Id = 1, Name = "Poland", Code = "PL", DisplayOrder = 10 },
            new Country { Id = 2, Name = "Germany", Code = "DE", DisplayOrder = 20 },
            new Country { Id = 3, Name = "France", Code = "FR", DisplayOrder = 30 },
            new Country { Id = 4, Name = "Sweden", Code = "SE", DisplayOrder = 40 },
            new Country { Id = 5, Name = "United Kingdom", Code = "GB", DisplayOrder = 50 });
    }
}

