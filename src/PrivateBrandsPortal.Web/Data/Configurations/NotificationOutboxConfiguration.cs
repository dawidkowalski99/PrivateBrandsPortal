using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrivateBrandsPortal.Web.Models.Entities;
namespace PrivateBrandsPortal.Web.Data.Configurations;
public sealed class NotificationOutboxConfiguration : IEntityTypeConfiguration<NotificationOutbox>
{
    public void Configure(EntityTypeBuilder<NotificationOutbox> b)
    {
        b.ToTable("NotificationOutbox"); b.HasKey(x => x.Id);
        b.Property(x => x.EventKey).HasMaxLength(64).IsRequired();
        b.HasIndex(x => x.EventKey).IsUnique();
        b.HasIndex(x => new { x.Status, x.NextAttemptAtUtc, x.Id });
        b.Property(x => x.EventType).HasMaxLength(100).IsRequired();
        b.Property(x => x.RecipientEmail).HasMaxLength(256);
        b.Property(x => x.Subject).HasMaxLength(400).IsRequired();
        b.Property(x => x.Body).IsRequired();
        b.Property(x => x.RelativePath).HasMaxLength(200).IsRequired();
        b.Property(x => x.LastError).HasMaxLength(500);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Audience).HasConversion<string>().HasMaxLength(30);
    }
}
