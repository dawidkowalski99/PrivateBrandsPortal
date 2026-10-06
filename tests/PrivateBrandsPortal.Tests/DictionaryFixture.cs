using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using PrivateBrandsPortal.Web.Data;
using PrivateBrandsPortal.Web.Models.Entities;
namespace PrivateBrandsPortal.Tests;

// Disposable reference data for SQL tests only. Never part of application seed data.
public sealed class DictionaryFixture : IAsyncLifetime
{
    public static int CustomerId { get; private set; } = 1;
    public static int ShampooId { get; private set; } = 1;
    public static int LotionId { get; private set; } = 2;
    private ApplicationDbContext? db;
    public async Task InitializeAsync()
    {
        var connection = new ConfigurationBuilder().AddUserSecrets<Program>().Build().GetConnectionString("DefaultConnection");
        if (new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connection).InitialCatalog != "PrivateBrandsPortal_DEV")
            throw new InvalidOperationException("Tests require PrivateBrandsPortal_DEV.");
        db = new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options);
        var customer = new Customer { Name = "PORTALTEST " + Guid.NewGuid().ToString("N"), CreatedAtUtc=DateTimeOffset.UtcNow, UpdatedAtUtc=DateTimeOffset.UtcNow };
        var shampoo = new ProductSubcategory { ProductCategoryId=1, Name="PORTALTEST Shampoo " + Guid.NewGuid().ToString("N") };
        var lotion = new ProductSubcategory { ProductCategoryId=2, Name="PORTALTEST Body Lotion " + Guid.NewGuid().ToString("N") };
        db.AddRange(customer,shampoo,lotion); await db.SaveChangesAsync();
        CustomerId=customer.Id; ShampooId=shampoo.Id; LotionId=lotion.Id;
    }
    public async Task DisposeAsync()
    {
        if(db is null)return;
        await db.ProductSubcategories.Where(x=>x.Id==ShampooId || x.Id==LotionId).ExecuteDeleteAsync();
        await db.Customers.Where(x=>x.Id==CustomerId).ExecuteDeleteAsync();
        await db.DisposeAsync();
    }
}
