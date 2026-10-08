using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrivateBrandsPortal.Web.Models.Entities;
namespace PrivateBrandsPortal.Web.Data.Configurations;
public sealed class ProductCustomerRejectionConfiguration : IEntityTypeConfiguration<ProjectProduct>
{
    public void Configure(EntityTypeBuilder<ProjectProduct> b)
    {
        b.Property(x=>x.CustomerRejectionReasonName).HasMaxLength(200);
        b.Property(x=>x.CustomerRejectionComment).HasMaxLength(1000);
        b.HasOne(x=>x.CustomerRejectionReason).WithMany().HasForeignKey(x=>x.CustomerRejectionReasonId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x=>x.CustomerRejectedByUser).WithMany().HasForeignKey(x=>x.CustomerRejectedByUserId).OnDelete(DeleteBehavior.NoAction);
    }
}
