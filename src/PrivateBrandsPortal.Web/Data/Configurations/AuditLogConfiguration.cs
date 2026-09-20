using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrivateBrandsPortal.Web.Models.Entities;
using PrivateBrandsPortal.Web.Models.Enums;

namespace PrivateBrandsPortal.Web.Data.Configurations;

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.ToTable("AuditLogs", t => t.HasCheckConstraint("CK_AuditLogs_ChangeType", "[ChangeType] IN ('Created','Updated','Deleted','ManagerEdit','ProjectSubmitted')"));
        b.HasKey(x => x.Id);
        b.Property(x => x.EntityType).IsRequired().HasMaxLength(100);
        b.Property(x => x.FieldName).IsRequired().HasMaxLength(100);
        // Unbounded text preserves complete historical values rather than truncating them.
        b.Property(x => x.OldValue).HasColumnType("nvarchar(max)");
        b.Property(x => x.NewValue).HasColumnType("nvarchar(max)");
        b.Property(x => x.ChangeType).HasConversion<string>().HasMaxLength(32);
        b.HasOne(x => x.ChangedByUser).WithMany(x => x.AuditLogs).HasForeignKey(x => x.ChangedByUserId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(x => new { x.EntityType, x.EntityId, x.ChangedAtUtc });
    }
}
