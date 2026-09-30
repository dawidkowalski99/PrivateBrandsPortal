using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrivateBrandsPortal.Web.Models.Entities;
namespace PrivateBrandsPortal.Web.Data.Configurations;

public sealed class ProductCategoryConfiguration : IEntityTypeConfiguration<ProductCategory>
{
    public void Configure(EntityTypeBuilder<ProductCategory> b)
    {
        b.Property(x => x.Name).HasMaxLength(100).IsRequired().UseCollation("Latin1_General_100_CI_AS");
        b.HasIndex(x => x.Name).IsUnique();
        b.Property(x => x.Description).HasMaxLength(1000);
        var time = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        b.HasData(new[] { "Hair Care", "Body Care", "Face Cream" }.Select((name, i) => new ProductCategory {
            Id = i + 1, Name = name, DisplayOrder = (i + 1) * 10, CreatedAtUtc = time, UpdatedAtUtc = time }));
    }
}
public sealed class RejectionReasonConfiguration : IEntityTypeConfiguration<RejectionReason>
{
    public void Configure(EntityTypeBuilder<RejectionReason> b)
    {
        b.Property(x => x.Name).HasMaxLength(100).IsRequired().UseCollation("Latin1_General_100_CI_AS");
        b.HasIndex(x => x.Name).IsUnique();
        b.Property(x => x.Description).HasMaxLength(1000);
        var time = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        b.HasData(new[] { "Price barrier", "Lack of technology", "Inability to meet quality requirements", "Not meeting the NPD",
            "Not meeting the MOQ", "Project with low potential", "Price too high", "Formulation quality below expectations", "Lack of information", "Other" }
            .Select((name, i) => new RejectionReason { Id = i + 1, Name = name, DisplayOrder = (i + 1) * 10,
                RequiresComment = name == "Other", CreatedAtUtc = time, UpdatedAtUtc = time }));
    }
}
