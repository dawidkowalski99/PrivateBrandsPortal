using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrivateBrandsPortal.Web.Models.Entities;
using PrivateBrandsPortal.Web.Models.Enums;

namespace PrivateBrandsPortal.Web.Data.Configurations;

public sealed class ProductTypeConfiguration : IEntityTypeConfiguration<ProductType>
{
    public void Configure(EntityTypeBuilder<ProductType> b)
    {
        b.ToTable("ProductTypes");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).IsRequired().HasMaxLength(100).UseCollation("Latin1_General_100_CI_AS");
        b.HasIndex(x => x.Name).IsUnique();
        b.Property(x => x.Description).HasMaxLength(1000);
        var seedTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        b.HasData(
            new ProductType { Id = 1, Name = "Shampoo", DisplayOrder = 10, CreatedAtUtc = seedTime, UpdatedAtUtc = seedTime },
            new ProductType { Id = 2, Name = "Shower Gel", DisplayOrder = 20, CreatedAtUtc = seedTime, UpdatedAtUtc = seedTime },
            new ProductType { Id = 3, Name = "Body Lotion", DisplayOrder = 30, CreatedAtUtc = seedTime, UpdatedAtUtc = seedTime },
            new ProductType { Id = 4, Name = "Conditioner", DisplayOrder = 40, CreatedAtUtc = seedTime, UpdatedAtUtc = seedTime });
    }
}

