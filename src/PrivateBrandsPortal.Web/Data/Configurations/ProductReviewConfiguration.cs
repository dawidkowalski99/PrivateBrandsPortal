using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrivateBrandsPortal.Web.Models.Entities;
using PrivateBrandsPortal.Web.Models.Enums;

namespace PrivateBrandsPortal.Web.Data.Configurations;

public sealed class ProductReviewConfiguration : IEntityTypeConfiguration<ProductReview>
{
    public void Configure(EntityTypeBuilder<ProductReview> b)
    {
        b.ToTable("ProductReviews", t =>
        {
            t.HasCheckConstraint("CK_ProductReviews_Decision", "[Decision] IN ('Approved','Rejected','EditedAndApproved')");
            t.HasCheckConstraint("CK_ProductReviews_RejectionComment", "[Decision] <> 'Rejected' OR ([Comment] IS NOT NULL AND LEN(LTRIM(RTRIM([Comment]))) > 0)");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Decision).HasConversion<string>().HasMaxLength(32);
        b.Property(x => x.Comment).HasMaxLength(2000);
        b.HasOne(x => x.ProjectProduct).WithMany(x => x.Reviews).HasForeignKey(x => x.ProjectProductId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.Reviewer).WithMany(x => x.ProductReviews).HasForeignKey(x => x.ReviewerId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(x => new { x.ProjectProductId, x.ReviewedAtUtc });
    }
}

