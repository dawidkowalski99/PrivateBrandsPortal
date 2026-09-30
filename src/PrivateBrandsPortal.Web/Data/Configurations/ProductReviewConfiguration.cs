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

        });
        b.HasKey(x => x.Id);
        b.Property(x => x.RejectionReasonName).HasMaxLength(100);
        b.HasOne(x => x.RejectionReason).WithMany().HasForeignKey(x => x.RejectionReasonId).OnDelete(DeleteBehavior.NoAction);
        b.Property(x => x.Decision).HasConversion<string>().HasMaxLength(32);
        b.Property(x => x.Comment).HasMaxLength(2000);
        b.HasOne(x => x.ProjectProduct).WithMany(x => x.Reviews).HasForeignKey(x => x.ProjectProductId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.Reviewer).WithMany(x => x.ProductReviews).HasForeignKey(x => x.ReviewerId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(x => new { x.ProjectProductId, x.ReviewedAtUtc });
    }
}
