using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PrivateBrandsPortal.Web.Data;
using PrivateBrandsPortal.Web.Models.Enums;
using PrivateBrandsPortal.Web.Services;
using PrivateBrandsPortal.Web.ViewModels;
namespace PrivateBrandsPortal.Tests;

[Collection("SQL integration")]
public sealed class ApprovalTests
{
    private static ApprovalService Service(ProjectSqlTests.Scope s, ApplicationDbContext? db = null) => new(db ?? s.Db, s.MakeUsers(db ?? s.Db), TimeProvider.System, DemoAccessTests.Access(enabled: false));
    private static async Task Manager(ProjectSqlTests.Scope s)
    {
        var user = await s.Users.GetCurrentAsync();
        await s.Db.AppUsers.Where(x => x.Id == user.Id).ExecuteUpdateAsync(x => x.SetProperty(u => u.Role, AppRole.Manager));
        s.Db.ChangeTracker.Clear();
    }
    private static async Task<int> Submitted(ProjectSqlTests.Scope s)
    {
        var id = await s.Projects.SaveDraftAsync(WizardTests.ValidDraft());
        var p = (await s.Projects.DetailsAsync(id))!;
        await Service(s).SubmitAsync(id, p.UpdatedAtUtc);
        s.Db.ChangeTracker.Clear();
        return id;
    }
    private static async Task<ReviewInput> Input(ProjectSqlTests.Scope manager, int id, ReviewDecision decision, int index = 0)
    {
        var p = (await Service(manager).ReviewAsync(id))!;
        var input = (await Service(manager).FormAsync(id, p.Products[index].Id, decision))!.Input; if(decision == ReviewDecision.Rejected) input.RejectionReasonId = 10; return input;
    }
    [Fact]
    public async Task Incoming_manager_claim_does_not_override_app_user_role()
    {
        var requirement = new PrivateBrandsPortal.Web.Configuration.AppRoleRequirement(AppRole.Manager);
        var identity = new System.Security.Claims.ClaimsIdentity(new[] {
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "TEST\\reader"),
            new System.Security.Claims.Claim(PrivateBrandsPortal.Web.Configuration.PortalAuthorization.RoleClaim, "Manager") }, "Test");
        var context = new Microsoft.AspNetCore.Authorization.AuthorizationHandlerContext([requirement], new(identity), null);
        await new PrivateBrandsPortal.Web.Configuration.ManagerAuthorization(new PolicyAppUser("ProjectManager"), DemoAccessTests.Access(enabled: false)).HandleAsync(context);
        Assert.False(context.HasSucceeded);
    }
    [Fact]
    public void Audit_display_formats_values_without_changing_stored_history()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("pl-PL");
            var change = new ManagerChange("EstimatedMargin", "25.00", "27.5", "Manager", DateTimeOffset.UtcNow);
            Assert.Equal("25,00%", change.OldDisplay); Assert.Equal("27,50%", change.NewDisplay);
            Assert.Equal("25.00", change.OldValue); Assert.Equal("Estimated Margin", change.Label);
        } finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
    }
    private sealed class UnavailableProfile : PrivateBrandsPortal.Web.Interfaces.IAppUserService
    {
        public Task<PrivateBrandsPortal.Web.Models.Entities.AppUser> GetCurrentAsync(CancellationToken ct = default)
            => throw new InvalidOperationException("Profile database unavailable.");
    }
    [Fact]
    public async Task Authenticated_error_page_does_not_query_badge_database()
    {
        await using var baseline = new AuthenticatedFactory();
        await using var factory = baseline.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddScoped<PrivateBrandsPortal.Web.Interfaces.IAppUserService, UnavailableProfile>()));
        using var client = factory.CreateClient(); client.DefaultRequestHeaders.Add("X-Test-User", "reader");
        var response = await client.GetAsync("/Home/Error");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("Request ID:", await response.Content.ReadAsStringAsync());
    }
    [Theory]
    [InlineData("0,0", ProjectStatus.AwaitingManagerReview)]
    [InlineData("1,0", ProjectStatus.PartiallyReviewed)]
    [InlineData("2,0", ProjectStatus.PartiallyReviewed)]
    [InlineData("1,3", ProjectStatus.Approved)]
    [InlineData("2,2", ProjectStatus.Rejected)]
    [InlineData("1,2", ProjectStatus.PartiallyApproved)]
    [InlineData("3,2", ProjectStatus.PartiallyApproved)]
    public void Aggregate_status_is_explicit(string values, ProjectStatus expected) =>
        Assert.Equal(expected, ApprovalService.CalculateStatus(values.Split(',').Select(x => (ProductReviewStatus)int.Parse(x))));

    [Fact]
    public async Task Submit_sets_status_timestamp_audit_and_blocks_saved_or_open_draft()
    {
        await using var s = new ProjectSqlTests.Scope();
        var id = await s.Projects.SaveDraftAsync(WizardTests.ValidDraft());
        var draft = (await s.Projects.LoadDraftAsync(id))!;
        await Service(s).SubmitAsync(id, draft.OriginalUpdatedAtUtc!.Value);
        s.Db.ChangeTracker.Clear();
        var p = (await s.Projects.DetailsAsync(id))!;
        Assert.Equal(ProjectStatus.AwaitingManagerReview, p.Status);
        Assert.NotNull(p.SubmittedAtUtc);
        Assert.True(p.UpdatedAtUtc > draft.OriginalUpdatedAtUtc);
        Assert.All(p.Products, x => Assert.Equal(ProductReviewStatus.Pending, x.ReviewStatus));
        Assert.Null(await s.Projects.LoadDraftAsync(id));
        await Assert.ThrowsAsync<ValidationException>(() => s.Projects.SaveDraftAsync(draft));
        Assert.Single(await s.Db.AuditLogs.Where(x => x.EntityType == "Project" && x.EntityId == id && x.ChangeType == AuditChangeType.ProjectSubmitted).ToListAsync());
    }
    [Fact]
    public async Task Submit_rejects_foreign_owner_non_draft_and_stale_version()
    {
        await using var s = new ProjectSqlTests.Scope(); await using var stranger = new ProjectSqlTests.Scope();
        var id = await s.Projects.SaveDraftAsync(WizardTests.ValidDraft()); var p = (await s.Projects.DetailsAsync(id))!;
        await Assert.ThrowsAsync<ValidationException>(() => Service(stranger).SubmitAsync(id, p.UpdatedAtUtc));
        await Assert.ThrowsAsync<ValidationException>(() => Service(s).SubmitAsync(id, p.UpdatedAtUtc.AddSeconds(-1)));
        await Service(s).SubmitAsync(id, p.UpdatedAtUtc);
        await Assert.ThrowsAsync<ValidationException>(() => Service(s).SubmitAsync(id, p.UpdatedAtUtc));
    }
    [Fact]
    public async Task Submit_without_products_rolls_back_parent_version()
    {
        await using var s = new ProjectSqlTests.Scope();
        var id = await s.Projects.SaveDraftAsync(WizardTests.ValidDraft()); var before = (await s.Projects.DetailsAsync(id))!;
        await s.Db.ProjectProducts.Where(x => x.ProjectId == id).ExecuteDeleteAsync(); s.Db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<ValidationException>(() => Service(s).SubmitAsync(id, before.UpdatedAtUtc));
        var after = (await s.Projects.DetailsAsync(id))!;
        Assert.Equal(ProjectStatus.Draft, after.Status); Assert.Equal(before.UpdatedAtUtc, after.UpdatedAtUtc);
    }
    [Fact]
    public async Task Submit_validates_existing_product_data()
    {
        await using var s = new ProjectSqlTests.Scope();
        var id = await s.Projects.SaveDraftAsync(WizardTests.ValidDraft());
        await s.Db.ProjectProducts.Where(x => x.ProjectId == id).ExecuteUpdateAsync(x => x.SetProperty(p => p.SKU, " "));
        s.Db.ChangeTracker.Clear(); var p = (await s.Projects.DetailsAsync(id))!;
        await Assert.ThrowsAsync<ValidationException>(() => Service(s).SubmitAsync(id, p.UpdatedAtUtc));
    }
    [Fact]
    public async Task Queue_is_manager_only_and_approve_creates_independent_decision()
    {
        await using var owner = new ProjectSqlTests.Scope(); await using var manager = new ProjectSqlTests.Scope();
        var id = await Submitted(owner); await Manager(manager);
        await Assert.ThrowsAsync<PortalAccessException>(() => Service(owner).QueueAsync());
        await Assert.ThrowsAsync<PortalAccessException>(() => Service(owner).ReviewAsync(id));
        Assert.Contains(await Service(manager).QueueAsync(), x => x.Id == id && x.Reviewed == 0);
        var input = await Input(manager, id, ReviewDecision.Approved);
        await Assert.ThrowsAsync<PortalAccessException>(() => Service(owner).DecideAsync(input));
        await Service(manager).DecideAsync(input);
        var p = (await owner.Projects.DetailsAsync(id))!;
        Assert.Equal(ProjectStatus.PartiallyReviewed, p.Status);
        Assert.Equal(ProductReviewStatus.Approved, p.Products[0].ReviewStatus);
        Assert.Equal(ProductReviewStatus.Pending, p.Products[1].ReviewStatus);
        Assert.Equal(ReviewDecision.Approved, Assert.Single(p.Products[0].Reviews).Decision);
        await Assert.ThrowsAsync<ValidationException>(() => Service(manager).DecideAsync(input));
    }
    [Fact]
    public async Task Reject_requires_reason_and_mixed_decisions_finish_review()
    {
        await using var owner = new ProjectSqlTests.Scope(); await using var manager = new ProjectSqlTests.Scope();
        var id = await Submitted(owner); await Manager(manager);
        var input = await Input(manager, id, ReviewDecision.Rejected); input.Comment = "  ";
        await Assert.ThrowsAsync<ValidationException>(() => Service(manager).DecideAsync(input));
        input.Comment = "  Margin not accepted  "; await Service(manager).DecideAsync(input);
        await Service(manager).DecideAsync(await Input(manager, id, ReviewDecision.Approved, 1));
        var p = (await owner.Projects.DetailsAsync(id))!;
        Assert.Equal(ProjectStatus.PartiallyApproved, p.Status);
        Assert.Equal("Margin not accepted", Assert.Single(p.Products[0].Reviews).Comment);
        Assert.DoesNotContain(await Service(manager).QueueAsync(), x => x.Id == id);
    }
    [Fact]
    public async Task Edit_and_approve_audits_only_actual_fields_and_preserves_owner_history()
    {
        await using var owner = new ProjectSqlTests.Scope(); await using var manager = new ProjectSqlTests.Scope();
        var id = await Submitted(owner); await Manager(manager);
        var input = await Input(manager, id, ReviewDecision.EditedAndApproved, 1);
        var old = input.Product!.Quantity; input.Product.Quantity = 6000; input.Product.EstimatedMargin = 27.5m;
        await Service(manager).DecideAsync(input);
        var p = (await owner.Projects.DetailsAsync(id))!; var product = p.Products[1];
        Assert.Equal(6000, product.Quantity); Assert.Equal(27.5m, product.EstimatedMargin);
        Assert.Equal(ProductReviewStatus.EditedAndApproved, product.ReviewStatus);
        Assert.Equal(ReviewDecision.EditedAndApproved, Assert.Single(product.Reviews).Decision);
        Assert.Equal(2, product.Changes.Count);
        Assert.Contains(product.Changes, x => x.FieldName == "Quantity" && x.OldValue == old.ToString() && x.NewValue == "6000");
        Assert.Contains(product.Changes, x => x.FieldName == "EstimatedMargin" && x.NewValue == "27.5");
        Assert.All(product.Changes, x => Assert.Equal(manager.Login, x.ChangedBy));
    }
    [Fact]
    public async Task Edit_fields_and_dictionary_selection_records_eight_diffs()
    {
        await using var owner = new ProjectSqlTests.Scope(); await using var manager = new ProjectSqlTests.Scope();
        var id = await Submitted(owner); await Manager(manager);
        var input = await Input(manager, id, ReviewDecision.EditedAndApproved);
        input.Product!.ProductCategoryId = 3; input.Product.ProductSubcategoryId = (await CustomerSubcategoryTests.Subcategory(owner,3)).Id; input.Product.SKU = "changed"; input.Product.Quantity = 7;
        input.Product.EstimatedValue = 12.34m; input.Product.EstimatedMargin = 10; input.Product.FormulaStatus = FormulaStatus.NewFormula;
        await Service(manager).DecideAsync(input);
        Assert.Equal(8, (await owner.Projects.DetailsAsync(id))!.Products[0].Changes.Count);
    }
    [Fact]
    public async Task Unchanged_edit_creates_review_without_fake_diffs()
    {
        await using var owner = new ProjectSqlTests.Scope(); await using var manager = new ProjectSqlTests.Scope();
        var id = await Submitted(owner); await Manager(manager);
        await Service(manager).DecideAsync(await Input(manager, id, ReviewDecision.EditedAndApproved));
        Assert.Empty((await owner.Projects.DetailsAsync(id))!.Products[0].Changes);
    }
    [Fact]
    public async Task Draft_and_cross_project_product_ids_cannot_be_reviewed()
    {
        await using var owner = new ProjectSqlTests.Scope(); await using var manager = new ProjectSqlTests.Scope();
        var draft = await owner.Projects.SaveDraftAsync(WizardTests.ValidDraft()); var id = await Submitted(owner); await Manager(manager);
        Assert.Null(await Service(manager).ReviewAsync(draft));
        var input = await Input(manager, id, ReviewDecision.Approved);
        input.ProductId = (await owner.Projects.DetailsAsync(draft))!.Products[0].Id;
        await Assert.ThrowsAsync<ValidationException>(() => Service(manager).DecideAsync(input));
        input.ProjectId = draft; input.ProjectVersion = (await owner.Projects.DetailsAsync(draft))!.UpdatedAtUtc;
        await Assert.ThrowsAsync<ValidationException>(() => Service(manager).DecideAsync(input));
        Assert.Empty(await owner.Db.ProductReviews.Where(x => x.ProjectProduct.ProjectId == draft).ToListAsync());
    }
    [Fact]
    public async Task Concurrent_forms_cannot_overwrite_a_newer_review()
    {
        await using var owner = new ProjectSqlTests.Scope(); await using var manager = new ProjectSqlTests.Scope();
        var id = await Submitted(owner); await Manager(manager);
        var first = await Input(manager, id, ReviewDecision.Approved);
        var stale = await Input(manager, id, ReviewDecision.Rejected, 1); stale.Comment = "old form";
        await Service(manager).DecideAsync(first);
        await Assert.ThrowsAsync<ValidationException>(() => Service(manager).DecideAsync(stale));
        Assert.Equal(ProductReviewStatus.Pending, (await owner.Projects.DetailsAsync(id))!.Products[1].ReviewStatus);
    }
    private sealed class ReviewFailure : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            if (command.CommandText.Contains("INSERT INTO [ProductReviews]")) throw new InvalidOperationException("Deliberate review failure.");
            return base.ReaderExecutingAsync(command, data, result, ct);
        }
    }
    [Fact]
    public async Task Review_failure_rolls_back_product_audit_review_and_project()
    {
        await using var owner = new ProjectSqlTests.Scope(); await using var manager = new ProjectSqlTests.Scope();
        var id = await Submitted(owner); await Manager(manager);
        var before = (await owner.Projects.DetailsAsync(id))!;
        var input = await Input(manager, id, ReviewDecision.EditedAndApproved); input.Product!.Quantity = 987;
        await using var failing = manager.NewContext(new ReviewFailure());
        var error = await Assert.ThrowsAnyAsync<Exception>(() => Service(manager, failing).DecideAsync(input));
        Assert.Contains("Deliberate review failure.", error.ToString());
        var after = (await owner.Projects.DetailsAsync(id))!;
        Assert.Equal(before.UpdatedAtUtc, after.UpdatedAtUtc); Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.Products[0].Quantity, after.Products[0].Quantity);
        Assert.Empty(after.Products[0].Reviews); Assert.Empty(after.Products[0].Changes);
    }
    [Theory]
    [InlineData("/Approvals")]
    [InlineData("/Approvals/Review/1")]
    [InlineData("/Approvals/Decision?projectId=1&productId=1&decision=Approved")]
    public async Task Project_manager_cannot_access_manager_routes(string path)
    {
        await using var factory = new AuthenticatedFactory(); using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "reader");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
    }
    [Theory]
    [InlineData("/Projects/Submit")]
    [InlineData("/Approvals/Decision")]
    public async Task Workflow_posts_require_antiforgery(string path)
    {
        await using var factory = new AuthenticatedFactory(); using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "reader");
        if (path == "/Approvals/Decision") client.DefaultRequestHeaders.Add("X-Test-AppRole", "Manager");
        var status = (await client.PostAsync(path, new FormUrlEncodedContent([]))).StatusCode;
        Assert.Equal(HttpStatusCode.BadRequest, status);
    }
}
