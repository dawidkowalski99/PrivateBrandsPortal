using Microsoft.EntityFrameworkCore;
using PrivateBrandsPortal.Web.Models.Entities;
namespace PrivateBrandsPortal.Web.Data;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<Country> Countries => Set<Country>();
    public DbSet<ProductType> ProductTypes => Set<ProductType>();
    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();
    public DbSet<RejectionReason> RejectionReasons => Set<RejectionReason>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectProduct> ProjectProducts => Set<ProjectProduct>();
    public DbSet<ProductReview> ProductReviews => Set<ProductReview>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcDateTimeOffsetConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasSequence<long>("ProjectNumberSequence", "dbo")
            .StartsAt(1).IncrementsBy(1).IsCyclic(false);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}
