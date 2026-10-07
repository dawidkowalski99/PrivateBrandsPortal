using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrivateBrandsPortal.Web.Models.Entities;
namespace PrivateBrandsPortal.Web.Data.Configurations;
public sealed class ImplementationConfiguration : IEntityTypeConfiguration<ImplementationApproval>, IEntityTypeConfiguration<CustomerRejectionReason>, IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<ImplementationApproval> b)
    {
        b.ToTable("ImplementationApprovals",t=>{
            t.HasCheckConstraint("CK_ImplementationApprovals_Status","[Status] IN ('Pending','Approved','Rejected')");
            t.HasCheckConstraint("CK_ImplementationApprovals_Decision","([Status]='Pending' AND [ReviewerId] IS NULL AND [ReviewedAtUtc] IS NULL) OR ([Status]<>'Pending' AND [ReviewerId] IS NOT NULL AND [ReviewedAtUtc] IS NOT NULL)");
        });
        b.Property(x=>x.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(x=>x.Comment).HasMaxLength(1000);
        b.HasOne(x=>x.ProjectProduct).WithMany(x=>x.ImplementationApprovals).HasForeignKey(x=>x.ProjectProductId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x=>x.RequestedByUser).WithMany().HasForeignKey(x=>x.RequestedByUserId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x=>x.Reviewer).WithMany().HasForeignKey(x=>x.ReviewerId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(x=>new{x.ProjectProductId,x.Id});
        b.HasIndex(x=>x.ProjectProductId).IsUnique().HasFilter("[Status]='Pending'");
    }
    public void Configure(EntityTypeBuilder<CustomerRejectionReason> b)
    {
        b.Property(x=>x.Code).HasMaxLength(64).UseCollation("Latin1_General_100_CI_AS");b.HasIndex(x=>x.Code).IsUnique();
        b.Property(x=>x.Name).HasMaxLength(200).UseCollation("Latin1_General_100_CI_AS");b.Property(x=>x.Description).HasMaxLength(1000);
    }
    public void Configure(EntityTypeBuilder<Project> b)
    {
        b.Property(x=>x.CustomerRejectionReasonName).HasMaxLength(200);b.Property(x=>x.CustomerRejectionComment).HasMaxLength(1000);
        b.HasOne(x=>x.CustomerRejectionReason).WithMany().HasForeignKey(x=>x.CustomerRejectionReasonId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x=>x.CustomerRejectedByUser).WithMany().HasForeignKey(x=>x.CustomerRejectedByUserId).OnDelete(DeleteBehavior.NoAction);
    }
}
