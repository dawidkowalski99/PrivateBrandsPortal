using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrivateBrandsPortal.Web.Models.Entities;
namespace PrivateBrandsPortal.Web.Data.Configurations;
public sealed class FormulaOptionConfiguration : IEntityTypeConfiguration<FormulaOption>
{
    public void Configure(EntityTypeBuilder<FormulaOption> b)
    {
        b.ToTable("FormulaOptions");b.HasKey(x=>x.Id);
        b.Property(x=>x.Code).HasMaxLength(64).IsRequired().UseCollation("Latin1_General_100_CI_AS");b.HasIndex(x=>x.Code).IsUnique();
        b.Property(x=>x.Name).HasMaxLength(200).IsRequired();b.Property(x=>x.Description).HasMaxLength(1000);
    }
}
public sealed class ProjectAttachmentConfiguration : IEntityTypeConfiguration<ProjectAttachment>
{
    public void Configure(EntityTypeBuilder<ProjectAttachment> b)
    {
        b.ToTable("ProjectAttachments",t=>{
            t.HasCheckConstraint("CK_ProjectAttachments_Type","[AttachmentType] IN ('Brief','Offer','Calculation')");
            t.HasCheckConstraint("CK_ProjectAttachments_Product","[ProjectProductId] IS NULL OR [AttachmentType]='Calculation'");
            t.HasCheckConstraint("CK_ProjectAttachments_Size","[FileSize]>0");
            t.HasCheckConstraint("CK_ProjectAttachments_Deleted","([DeletedAtUtc] IS NULL AND [DeletedByUserId] IS NULL) OR ([DeletedAtUtc] IS NOT NULL AND [DeletedByUserId] IS NOT NULL)");
        });b.HasKey(x=>x.Id);
        b.Property(x=>x.AttachmentType).HasConversion<string>().HasMaxLength(20);
        b.Property(x=>x.OriginalFileName).HasMaxLength(255).IsRequired();b.Property(x=>x.StorageKey).HasMaxLength(200).IsRequired();b.HasIndex(x=>x.StorageKey).IsUnique();
        b.Property(x=>x.ContentType).HasMaxLength(150).IsRequired();b.Property(x=>x.Description).HasMaxLength(1000);b.Property(x=>x.Sha256).HasMaxLength(64);
        b.HasOne(x=>x.Project).WithMany().HasForeignKey(x=>x.ProjectId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x=>x.ProjectProduct).WithMany().HasForeignKey(x=>x.ProjectProductId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x=>x.UploadedByUser).WithMany().HasForeignKey(x=>x.UploadedByUserId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x=>x.DeletedByUser).WithMany().HasForeignKey(x=>x.DeletedByUserId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(x=>new{x.ProjectId,x.AttachmentType,x.DeletedAtUtc});
    }
}
