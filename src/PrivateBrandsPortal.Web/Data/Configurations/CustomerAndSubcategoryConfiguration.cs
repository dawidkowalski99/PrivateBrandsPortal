using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrivateBrandsPortal.Web.Models.Entities;
namespace PrivateBrandsPortal.Web.Data.Configurations;
public sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> b)
    {
        b.ToTable("Customers"); b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(200).IsRequired().UseCollation("Latin1_General_100_CI_AS");
        b.Property(x => x.Code).HasMaxLength(50);
        b.HasIndex(x => x.Name).IsUnique();
        b.HasOne(x => x.DefaultCountry).WithMany().HasForeignKey(x => x.DefaultCountryId).OnDelete(DeleteBehavior.NoAction);
    }
}
public sealed class ProductSubcategoryConfiguration : IEntityTypeConfiguration<ProductSubcategory>
{
    public void Configure(EntityTypeBuilder<ProductSubcategory> b)
    {
        b.ToTable("ProductSubcategories"); b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(100).IsRequired().UseCollation("Latin1_General_100_CI_AS");
        b.HasIndex(x => new { x.ProductCategoryId, x.Name }).IsUnique();
        b.HasOne(x => x.ProductCategory).WithMany().HasForeignKey(x => x.ProductCategoryId).OnDelete(DeleteBehavior.NoAction);
    }
}
