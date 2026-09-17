using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using PrivateBrandsPortal.Web.Data;
using PrivateBrandsPortal.Web.Models.Entities;
using PrivateBrandsPortal.Web.Models.Enums;
using PrivateBrandsPortal.Web.Services;

namespace PrivateBrandsPortal.Tests;

public sealed class BusinessModelTests
{
    private static ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer().Options);

    [Fact]
    public void Model_contains_seven_entities_and_no_cascade_deletes()
    {
        using var db = CreateContext();
        Assert.Equal(7, db.Model.GetEntityTypes().Count());
        var foreignKeys = db.Model.GetEntityTypes().SelectMany(x => x.GetForeignKeys()).ToList();
        Assert.Equal(7, foreignKeys.Count);
        Assert.All(foreignKeys, fk => Assert.Equal(DeleteBehavior.NoAction, fk.DeleteBehavior));
    }

    [Fact]
    public void Project_products_have_required_foreign_key_and_navigation_fixup()
    {
        using var db = CreateContext();
        var project = new Project { Id = 10, ProjectNumber = "PB-2026-0001", Customer = "Test" };
        var product = new ProjectProduct { SKU = "SKU-1", Quantity = 1 };
        project.Products.Add(product);
        db.Add(project); // Tracking only; no server or database connection.
        Assert.Same(project, product.Project);
        Assert.Equal(project.Id, product.ProjectId);
        var relation = db.Model.FindEntityType(typeof(ProjectProduct))!.GetForeignKeys()
            .Single(fk => fk.PrincipalEntityType.ClrType == typeof(Project));
        Assert.True(relation.IsRequired);
        Assert.False(relation.IsUnique);
        Assert.Equal(nameof(Project.Products), relation.PrincipalToDependent!.Name);
    }

    [Fact]
    public void New_product_is_pending_and_new_project_is_draft()
    {
        Assert.Equal(ProductReviewStatus.Pending, new ProjectProduct { SKU = "Test" }.ReviewStatus);
        Assert.Equal(ProjectStatus.Draft, new Project { ProjectNumber = "Test", Customer = "Test" }.Status);
        using var db = CreateContext();
        Assert.Equal(ProductReviewStatus.Pending,
            db.Model.FindEntityType(typeof(ProjectProduct))!.FindProperty(nameof(ProjectProduct.ReviewStatus))!.GetDefaultValue());
    }

    [Theory]
    [InlineData(typeof(AppUser), "DomainLogin", 256)]
    [InlineData(typeof(Project), "ProjectNumber", 32)]
    [InlineData(typeof(Country), "Code", 2)]
    [InlineData(typeof(ProductType), "Name", 100)]
    public void Business_keys_are_required_bounded_and_unique(Type entity, string property, int maxLength)
    {
        using var db = CreateContext();
        var type = db.Model.FindEntityType(entity)!;
        Assert.False(type.FindProperty(property)!.IsNullable);
        Assert.Equal(maxLength, type.FindProperty(property)!.GetMaxLength());
        Assert.Contains(type.GetIndexes(), i => i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual([property]));
    }

    [Fact]
    public void Money_and_margin_have_explicit_precision()
    {
        using var db = CreateContext();
        var entity = db.Model.FindEntityType(typeof(ProjectProduct))!;
        var value = entity.FindProperty(nameof(ProjectProduct.EstimatedValue))!;
        var margin = entity.FindProperty(nameof(ProjectProduct.EstimatedMargin))!;
        Assert.Equal(typeof(decimal), value.ClrType);
        Assert.Equal(18, value.GetPrecision());
        Assert.Equal(2, value.GetScale());
        Assert.Equal(5, margin.GetPrecision());
        Assert.Equal(2, margin.GetScale());
        Assert.Equal(typeof(int), entity.FindProperty(nameof(ProjectProduct.Quantity))!.ClrType);
    }

    [Fact]
    public void Seed_is_deterministic_and_does_not_grant_user_access()
    {
        using var db = CreateContext();
        var model = db.GetService<IDesignTimeModel>().Model;
        var countries = model.FindEntityType(typeof(Country))!.GetSeedData().ToArray();
        var types = model.FindEntityType(typeof(ProductType))!.GetSeedData().ToArray();
        Assert.Equal(new[] { "PL", "DE", "FR", "SE", "GB" }, countries.Select(x => x["Code"]));
        Assert.Equal(new[] { "Shampoo", "Shower Gel", "Body Lotion", "Conditioner" }, types.Select(x => x["Name"]));
        Assert.Equal(5, countries.Select(x => x["Id"]).Distinct().Count());
        Assert.Equal(4, types.Select(x => x["Id"]).Distinct().Count());
        Assert.All(types, x => Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), x["CreatedAtUtc"]));
        Assert.Empty(model.FindEntityType(typeof(AppUser))!.GetSeedData());
    }

    [Fact]
    public void Sequence_is_non_cycling_bigint()
    {
        using var db = CreateContext();
        var sequence = db.Model.FindSequence("ProjectNumberSequence", "dbo")!;
        Assert.Equal(typeof(long), sequence.Type);
        Assert.Equal(1, sequence.StartValue);
        Assert.Equal(1, sequence.IncrementBy);
        Assert.False(sequence.IsCyclic);
    }

    [Theory]
    [InlineData(2026, 1, "PB-2026-0001")]
    [InlineData(2026, 9999, "PB-2026-9999")]
    [InlineData(2027, 10000, "PB-2027-10000")]
    [InlineData(2027, long.MaxValue, "PB-2027-9223372036854775807")]
    public void Project_number_has_stable_format_and_does_not_truncate(int year, long value, string expected)
        => Assert.Equal(expected, ProjectNumberGenerator.Format(year, value));

    [Theory]
    [InlineData(0, 1)]
    [InlineData(10000, 1)]
    [InlineData(2026, 0)]
    [InlineData(2026, -1)]
    public void Project_number_rejects_invalid_values(int year, long value)
        => Assert.Throws<ArgumentOutOfRangeException>(() => ProjectNumberGenerator.Format(year, value));

    [Fact]
    public void Timestamp_converter_preserves_instant_and_normalizes_utc()
    {
        var converter = new UtcDateTimeOffsetConverter();
        var local = new DateTimeOffset(2026, 9, 16, 12, 15, 0, TimeSpan.FromHours(2));
        var stored = (DateTimeOffset)converter.ConvertToProvider(local)!;
        Assert.Equal(TimeSpan.Zero, stored.Offset);
        Assert.Equal(local.UtcDateTime, stored.UtcDateTime);
        Assert.Equal(TimeSpan.Zero, ((DateTimeOffset)converter.ConvertFromProvider(local)!).Offset);
        using var db = CreateContext();
        Assert.All(db.Model.GetEntityTypes().SelectMany(e => e.GetProperties())
            .Where(p => p.ClrType == typeof(DateTimeOffset) || p.ClrType == typeof(DateTimeOffset?)),
            p => Assert.IsType<UtcDateTimeOffsetConverter>(p.GetValueConverter()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Current_user_does_not_trust_unauthenticated_name(bool authenticated)
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "EXAMPLE\\reader")], authenticated ? "Windows" : null);
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        var service = new CurrentUserService(new HttpContextAccessor { HttpContext = context });
        Assert.Equal(authenticated, service.IsAuthenticated);
        Assert.Equal(authenticated ? "EXAMPLE\\reader" : null, service.DomainLogin);
        Assert.Equal(authenticated ? "EXAMPLE\\reader" : "Unknown user", service.DisplayName);
        Assert.Equal("Role not assigned", service.RoleLabel);
    }

    [Fact]
    public void Current_user_handles_missing_http_context()
    {
        var service = new CurrentUserService(new HttpContextAccessor());
        Assert.False(service.IsAuthenticated);
        Assert.Null(service.DomainLogin);
    }

    [Theory]
    [InlineData(FormulaStatus.ReadyToGo, "Ready to go")]
    [InlineData(FormulaStatus.NewFormula, "New formula")]
    public void Formula_status_has_readable_display_name(FormulaStatus value, string expected)
        => Assert.Equal(expected, typeof(FormulaStatus).GetMember(value.ToString())[0].GetCustomAttribute<DisplayAttribute>()!.Name);
}
