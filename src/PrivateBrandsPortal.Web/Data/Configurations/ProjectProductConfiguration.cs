using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrivateBrandsPortal.Web.Models.Entities;
using PrivateBrandsPortal.Web.Models.Enums;

namespace PrivateBrandsPortal.Web.Data.Configurations;

public sealed class ProjectProductConfiguration : IEntityTypeConfiguration<ProjectProduct>
{
    public void Configure(EntityTypeBuilder<ProjectProduct> b)
    {
        b.ToTable("ProjectProducts", t =>
        {
            t.HasCheckConstraint("CK_ProjectProducts_Quantity", "[Quantity] > 0");
            t.HasCheckConstraint("CK_ProjectProducts_Value", "[EstimatedValue] >= 0");
            t.HasCheckConstraint("CK_ProjectProducts_Margin", "[EstimatedMargin] >= 0 AND [EstimatedMargin] <= 100");
            t.HasCheckConstraint("CK_ProjectProducts_Formula", "[FormulaStatus] IN ('ReadyToGo','NewFormula')");
            t.HasCheckConstraint("CK_ProjectProducts_Review", "[ReviewStatus] IN ('Pending','Approved','Rejected','EditedAndApproved')");
            t.HasCheckConstraint("CK_ProjectProducts_Commercial", "[CommercialStatus] IS NULL OR ([ReviewStatus] IN ('Approved','EditedAndApproved') AND [CommercialStatus] IN ('PriceOfferSubmitted','OfferUnderNegotiation','CustomerApprovedOrder','CustomerNotApproved','ImplementationIntoProduction','SalesAndDelivery'))");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Subcategory).HasMaxLength(100);
        b.Property(x => x.CommercialStatus).HasConversion<string>().HasMaxLength(40);
        b.HasOne(x => x.ProductCategory).WithMany().HasForeignKey(x => x.ProductCategoryId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(x => new { x.ReviewStatus, x.CommercialStatus });
        b.Property(x => x.SKU).IsRequired().HasMaxLength(100);
        b.Property(x => x.EstimatedValue).HasPrecision(18, 2);
        b.Property(x => x.EstimatedMargin).HasPrecision(5, 2);
        b.Property(x => x.FormulaStatus).HasConversion<string>().HasMaxLength(32);
        b.Property(x => x.ReviewStatus).HasConversion<string>().HasMaxLength(32).HasDefaultValue(ProductReviewStatus.Pending);
        b.Property(x => x.ReviewComment).HasMaxLength(2000);
        b.HasOne(x => x.Project).WithMany(x => x.Products).HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.ProductType).WithMany(x => x.Products).HasForeignKey(x => x.ProductTypeId).OnDelete(DeleteBehavior.NoAction);
    }
}
