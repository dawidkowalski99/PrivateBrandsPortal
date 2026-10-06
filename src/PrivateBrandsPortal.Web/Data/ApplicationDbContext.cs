using Microsoft.EntityFrameworkCore;
using PrivateBrandsPortal.Web.Models.Entities;
namespace PrivateBrandsPortal.Web.Data;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<ProductSubcategory> ProductSubcategories => Set<ProductSubcategory>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<AppUserPermission> AppUserPermissions => Set<AppUserPermission>();
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
        modelBuilder.Entity<Project>().HasOne(x => x.CustomerEntry).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<ProjectProduct>().HasOne(x => x.ProductSubcategory).WithMany().HasForeignKey(x => x.ProductSubcategoryId).OnDelete(DeleteBehavior.NoAction);
    }
}
