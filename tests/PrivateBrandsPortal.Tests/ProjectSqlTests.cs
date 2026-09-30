using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using PrivateBrandsPortal.Web.Data;
using PrivateBrandsPortal.Web.Interfaces;
using PrivateBrandsPortal.Web.Models.Entities;
using PrivateBrandsPortal.Web.Models.Enums;
using PrivateBrandsPortal.Web.Services;
namespace PrivateBrandsPortal.Tests;

// Real SQL Server tests. Each test uses a unique disposable profile and removes only its own rows.
// No SQLite, EnsureCreated, schema changes or modification of seeded dictionaries.
[Collection("SQL integration")]
public sealed class ProjectSqlTests
{
    private sealed class Current(string login) : ICurrentUserService
    {
        public string DomainLogin => login;
        public bool IsAuthenticated => true;
        public string DisplayName => login;
        public string RoleLabel => "ProjectManager";
    }
    private sealed class EnvironmentStub : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "PrivateBrandsPortal.Web";
        public string WebRootPath { get; set; } = "";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
    internal sealed class Scope : IAsyncDisposable
    {
        public string Login { get; } = $"PORTALTEST\\{Guid.NewGuid():N}";
        public string Connection { get; }
        public ApplicationDbContext Db { get; }
        public IAppUserService Users { get; }
        public ProjectService Projects { get; }
        public Scope()
        {
            var config = new ConfigurationBuilder().AddUserSecrets<Program>().AddEnvironmentVariables().Build();
            Connection = config.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Configure DEV User Secrets for SQL integration tests.");
            if (new SqlConnectionStringBuilder(Connection).InitialCatalog != "PrivateBrandsPortal_DEV")
                throw new InvalidOperationException("SQL tests are restricted to PrivateBrandsPortal_DEV.");
            Db = NewContext();
            Users = MakeUsers(Db);
            Projects = MakeProjects(Db);
        }
        public ApplicationDbContext NewContext(IInterceptor? interceptor = null)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(Connection);
            if (interceptor is not null) options.AddInterceptors(interceptor);
            return new(options.Options);
        }
        public AppUserService MakeUsers(ApplicationDbContext db, string environment = "Development") =>
            new(db, new Current(Login), new EnvironmentStub { EnvironmentName = environment }, TimeProvider.System,
                new HttpContextAccessor(), NullLogger<AppUserService>.Instance);
        public ProjectService MakeProjects(ApplicationDbContext db) =>
            new(db, MakeUsers(db), new ProjectNumberGenerator(db, TimeProvider.System), TimeProvider.System);
        public async ValueTask DisposeAsync()
        {
            await using var clean = NewContext();
            var ids = clean.AppUsers.Where(x => x.DomainLogin == Login).Select(x => x.Id);
            await clean.AuditLogs.Where(x => ids.Contains(x.ChangedByUserId)).ExecuteDeleteAsync();
            await clean.ProductReviews.Where(x => ids.Contains(x.ReviewerId) || ids.Contains(x.ProjectProduct.Project.ProjectManagerId)).ExecuteDeleteAsync();
            await clean.ProjectProducts.Where(x => ids.Contains(x.Project.ProjectManagerId)).ExecuteDeleteAsync();
            await clean.Projects.Where(x => ids.Contains(x.ProjectManagerId)).ExecuteDeleteAsync();
            await clean.AppUsers.Where(x => x.DomainLogin == Login).ExecuteDeleteAsync();
            await clean.RejectionReasons.Where(x => x.Name.StartsWith(Login)).ExecuteDeleteAsync();
            await clean.ProductCategories.Where(x => x.Name.StartsWith(Login)).ExecuteDeleteAsync();
            await Db.DisposeAsync();
        }
    }
    [Fact]
    public async Task Development_mapping_is_idempotent_and_never_grants_admin()
    {
        await using var scope = new Scope();
        var first = await scope.Users.GetCurrentAsync();
        var second = await scope.Users.GetCurrentAsync();
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(scope.Login, first.DomainLogin);
        Assert.Equal(AppRole.ProjectManager, first.Role);
        Assert.True(first.IsActive);
        Assert.Equal(1, await scope.Db.AppUsers.CountAsync(x => x.DomainLogin == scope.Login));
    }
    [Fact]
    public async Task Production_does_not_auto_provision_and_inactive_user_is_denied()
    {
        await using var scope = new Scope();
        await Assert.ThrowsAsync<PortalAccessException>(() => scope.MakeUsers(scope.Db, "Production").GetCurrentAsync());
        var user = await scope.Users.GetCurrentAsync();
        await scope.Db.AppUsers.Where(x => x.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        await Assert.ThrowsAsync<PortalAccessException>(() => scope.Users.GetCurrentAsync());
    }
    [Fact]
    public async Task Save_assigns_owner_number_draft_and_pending_products()
    {
        await using var scope = new Scope();
        var user = await scope.Users.GetCurrentAsync();
        var id = await scope.Projects.SaveDraftAsync(WizardTests.ValidDraft());
        scope.Db.ChangeTracker.Clear();
        var project = await scope.Db.Projects.AsNoTracking().Include(x => x.Products).SingleAsync(x => x.Id == id);
        Assert.Equal(user.Id, project.ProjectManagerId);
        Assert.Matches(@"^PB-\d{4}-\d{4,}$", project.ProjectNumber);
        Assert.Equal(ProjectStatus.Draft, project.Status);
        Assert.Equal(2, project.Products.Count);
        Assert.All(project.Products, p => Assert.Equal(ProductReviewStatus.Pending, p.ReviewStatus));
        Assert.Equal(245000m, project.Products.Sum(p => p.EstimatedValue));
        Assert.Equal(31.5m, project.Products.Single(p => p.SKU == "SKU-1").EstimatedMargin);
        Assert.Equal(TimeSpan.Zero, project.CreatedAtUtc.Offset);
        Assert.NotNull(await scope.Projects.DetailsAsync(id));
        Assert.Single(await scope.Projects.ListAsync());
    }
    [Fact]
    public async Task Other_owner_cannot_read_edit_or_save_project()
    {
        await using var owner = new Scope();
        await using var stranger = new Scope();
        var id = await owner.Projects.SaveDraftAsync(WizardTests.ValidDraft());
        var stolen = (await owner.Projects.LoadDraftAsync(id))!;
        Assert.Null(await stranger.Projects.DetailsAsync(id));
        Assert.Null(await stranger.Projects.LoadDraftAsync(id));
        Assert.Empty(await stranger.Projects.ListAsync());
        await Assert.ThrowsAsync<ValidationException>(() => stranger.Projects.SaveDraftAsync(stolen));
    }
    [Fact]
    public async Task Editing_preserves_number_and_rejects_stale_save()
    {
        await using var scope = new Scope();
        var id = await scope.Projects.SaveDraftAsync(WizardTests.ValidDraft());
        scope.Db.ChangeTracker.Clear();
        var edit = (await scope.Projects.LoadDraftAsync(id))!;
        var stale = WizardStore.Snapshot(edit);
        var number = (await scope.Projects.DetailsAsync(id))!.ProjectNumber;
        edit.Products[0].Quantity = 25000;
        edit.Products.RemoveAt(1);
        await scope.Projects.SaveDraftAsync(edit);
        scope.Db.ChangeTracker.Clear();
        var details = (await scope.Projects.DetailsAsync(id))!;
        Assert.Equal(number, details.ProjectNumber);
        Assert.Single(details.Products);
        Assert.Equal(25000, details.Products[0].Quantity);
        await Assert.ThrowsAsync<ValidationException>(() => scope.Projects.SaveDraftAsync(stale));
    }
    private sealed class ProductFailure : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            if (command.CommandText.Contains("INSERT INTO [ProjectProducts]") || command.CommandText.Contains("MERGE [ProjectProducts]"))
                throw new InvalidOperationException("Deliberate product write failure.");
            return base.ReaderExecutingAsync(command, data, result, ct);
        }
    }
    [Fact]
    public async Task Failed_product_insert_rolls_back_entire_draft()
    {
        await using var scope = new Scope();
        var user = await scope.Users.GetCurrentAsync();
        await using var failing = scope.NewContext(new ProductFailure());
        var error = await Assert.ThrowsAnyAsync<Exception>(() => scope.MakeProjects(failing).SaveDraftAsync(WizardTests.ValidDraft()));
        Assert.Contains("Deliberate product write failure.", error.ToString());
        Assert.False(await scope.Db.Projects.AnyAsync(x => x.ProjectManagerId == user.Id));
    }
}
