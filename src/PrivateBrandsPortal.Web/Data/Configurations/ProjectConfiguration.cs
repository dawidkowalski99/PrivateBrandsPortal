using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrivateBrandsPortal.Web.Models.Entities;
using PrivateBrandsPortal.Web.Models.Enums;

namespace PrivateBrandsPortal.Web.Data.Configurations;

public sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> b)
    {
        b.ToTable("Projects", t => t.HasCheckConstraint("CK_Projects_Status",
            "[Status] IN ('Draft','AwaitingManagerReview','PartiallyReviewed','Approved','Rejected','InProgress','Completed','Cancelled','PartiallyApproved')"));
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.ArchivedAtUtc, x.CreatedAtUtc });
        b.Property(x => x.ProjectNumber).IsRequired().HasMaxLength(32).IsUnicode(false);
        b.HasIndex(x => x.ProjectNumber).IsUnique();
        b.Property(x => x.Customer).IsRequired().HasMaxLength(200);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).HasDefaultValue(ProjectStatus.Draft);
        b.HasOne(x => x.Country).WithMany(x => x.Projects).HasForeignKey(x => x.CountryId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.ProjectManager).WithMany(x => x.Projects).HasForeignKey(x => x.ProjectManagerId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(x => new { x.ProjectManagerId, x.Status });
        b.HasIndex(x => new { x.Status, x.UpdatedAtUtc });
    }
}
