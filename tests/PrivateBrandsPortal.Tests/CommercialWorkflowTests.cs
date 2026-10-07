using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PrivateBrandsPortal.Web.Models.Entities;
using PrivateBrandsPortal.Web.Models.Enums;
using PrivateBrandsPortal.Web.Services;
using PrivateBrandsPortal.Web.ViewModels;
namespace PrivateBrandsPortal.Tests;

[Collection("SQL integration")]
public sealed class CommercialWorkflowTests
{
    internal static ApprovalService Review(ProjectSqlTests.Scope s)=>new(s.Db,s.Users,TimeProvider.System,DemoAccessTests.Access(configured:s.Login,login:s.Login));
    internal static CommercialService Commercial(ProjectSqlTests.Scope s)=>new(s.Db,s.Users,TimeProvider.System);
    internal static async Task<int> Submitted(ProjectSqlTests.Scope s,DraftInput? draft=null)
    {
        var id=await s.Projects.SaveDraftAsync(draft??WizardTests.ValidDraft());
        await Review(s).SubmitAsync(id,(await s.Projects.DetailsAsync(id))!.UpdatedAtUtc);return id;
    }
    internal static async Task Decide(ProjectSqlTests.Scope s,int id,int index,ReviewDecision decision,int? reason=null)
    {
        var product=(await s.Projects.DetailsAsync(id))!.Products[index];
        var input=(await Review(s).FormAsync(id,product.Id,decision))!.Input;
        input.RejectionReasonId=reason;if(reason==10)input.Comment="Test reason";
        await Review(s).DecideAsync(input);
    }
    // Existing success-path fixtures now include the newly required document metadata.
    internal static async Task SupplyDocuments(ProjectSqlTests.Scope s,int id) {
        var actor=(await s.Users.GetCurrentAsync()).Id;
        foreach(var type in new[]{AttachmentType.Offer,AttachmentType.Calculation})
            if(!await s.Db.ProjectAttachments.AnyAsync(x=>x.ProjectId==id && x.AttachmentType==type && x.DeletedAtUtc==null && x.ProjectProductId==null))
                s.Db.ProjectAttachments.Add(new ProjectAttachment{ProjectId=id,AttachmentType=type,OriginalFileName="Fixture.xlsx",StorageKey=$"projects/{id}/{Guid.NewGuid():N}.xlsx",ContentType="application/octet-stream",FileSize=1,UploadedByUserId=actor,UploadedAtUtc=DateTimeOffset.UtcNow});
        await s.Db.SaveChangesAsync();
    }
    internal static async Task ApproveImplementation(ProjectSqlTests.Scope s,int id,int productId)
    {
        var reviewer=await s.Db.AppUsers.SingleOrDefaultAsync(x=>x.DomainLogin==s.Login+"-reviewer");
        if(reviewer is null){reviewer=new(){DomainLogin=s.Login+"-reviewer",DisplayName="Test implementation reviewer",Role=AppRole.Manager,IsActive=true};s.Db.AppUsers.Add(reviewer);await s.Db.SaveChangesAsync();}
        var approval=await s.Db.ImplementationApprovals.AsNoTracking().SingleAsync(x=>x.ProjectProductId==productId && x.Status==ImplementationApprovalStatus.Pending);
        await new ImplementationService(s.Db,new PermissionTests.FixedUser(reviewer),TimeProvider.System).DecideAsync(new(){Id=approval.Id,Version=(await s.Projects.DetailsAsync(id))!.UpdatedAtUtc,Decision=ImplementationApprovalStatus.Approved},default);
    }
    internal static async Task Status(ProjectSqlTests.Scope s,int id,int index,CommercialStatus status)
    {
        if(status is CommercialStatus.SalesAndDelivery or CommercialStatus.ImplementationIntoProduction) await SupplyDocuments(s,id);
        var product=(await s.Projects.DetailsAsync(id))!.Products[index];
        if(status==CommercialStatus.SalesAndDelivery){
            if(product.CommercialStatus!=CommercialStatus.ImplementationIntoProduction)await Status(s,id,index,CommercialStatus.ImplementationIntoProduction);
            await ApproveImplementation(s,id,product.Id);
        }
        var form=(await Commercial(s).FormAsync(id,product.Id,default))!;form.Input.Status=status;
        await Commercial(s).UpdateAsync(form.Input);
    }
    [Theory]
    [InlineData(ReviewDecision.Approved)]
    [InlineData(ReviewDecision.EditedAndApproved)]
    public async Task Accepted_sku_has_independent_status_history_and_final_delivery(ReviewDecision decision)
    {
        await using var s=new ProjectSqlTests.Scope();var id=await Submitted(s);await Decide(s,id,0,decision);
        Assert.Equal(1,await s.Projects.AwaitingPmAsync());
        foreach(var status in new[]{CommercialStatus.PriceOfferSubmitted,CommercialStatus.OfferUnderNegotiation,CommercialStatus.CustomerApprovedOrder,CommercialStatus.ImplementationIntoProduction,CommercialStatus.SalesAndDelivery})await Status(s,id,0,status);
        var p=(await s.Projects.DetailsAsync(id))!;
        Assert.Equal(5,p.Products[0].CommercialHistory.Count);Assert.Equal("Awaiting PM update",p.Products[0].CommercialHistory[0].OldDisplay);
        Assert.Null(p.ArchivedAtUtc); // A pending sibling prevents archive.
        Assert.Null(await Commercial(s).FormAsync(id,p.Products[0].Id,default));
        await Decide(s,id,1,ReviewDecision.Rejected,7);
        p=(await s.Projects.DetailsAsync(id))!;
        Assert.NotNull(p.ArchivedAtUtc);Assert.Equal(ProjectStatus.PartiallyApproved,p.Status);
        Assert.Contains(await Commercial(s).ArchiveAsync(default),x=>x.Id==id && x.Completed==1 && x.Products==2);
        Assert.DoesNotContain(await s.Projects.ListAsync(),x=>x.Id==id);
        Assert.Equal(0,await s.Projects.AwaitingPmAsync());
    }
    [Fact]
    public async Task All_accepted_skus_must_finish_before_positive_archive()
    {
        await using var s=new ProjectSqlTests.Scope();var id=await Submitted(s);await Decide(s,id,0,ReviewDecision.Approved);await Decide(s,id,1,ReviewDecision.Approved);
        await Status(s,id,0,CommercialStatus.SalesAndDelivery);await Assert.ThrowsAsync<ValidationException>(()=>Status(s,id,1,CommercialStatus.CustomerNotApproved));
        Assert.Null((await s.Projects.DetailsAsync(id))!.ArchivedAtUtc);
        await Status(s,id,1,CommercialStatus.SalesAndDelivery);var p=(await s.Projects.DetailsAsync(id))!;Assert.NotNull(p.ArchivedAtUtc);Assert.Equal(ProjectStatus.Approved,p.Status);
        await CommercialService.ArchiveIfCompleteAsync(s.Db,id,DateTimeOffset.UtcNow.AddDays(1),default);
        Assert.Equal(p.ArchivedAtUtc,(await s.Projects.DetailsAsync(id))!.ArchivedAtUtc);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pending_and_rejected_skus_cannot_be_updated_or_archived(bool rejected)
    {
        await using var s=new ProjectSqlTests.Scope();var id=await Submitted(s);
        if(rejected){await Decide(s,id,0,ReviewDecision.Rejected,7);await Decide(s,id,1,ReviewDecision.Rejected,7);}
        var p=(await s.Projects.DetailsAsync(id))!;
        Assert.Null(await Commercial(s).FormAsync(id,p.Products[0].Id,default));
        await Assert.ThrowsAsync<ValidationException>(()=>Commercial(s).UpdateAsync(new(){ProjectId=id,ProductId=p.Products[0].Id,Version=p.UpdatedAtUtc,Status=CommercialStatus.SalesAndDelivery}));
        Assert.Equal(p.UpdatedAtUtc,(await s.Projects.DetailsAsync(id))!.UpdatedAtUtc);
        Assert.Null((await s.Projects.DetailsAsync(id))!.ArchivedAtUtc);
    }
    [Fact]
    public async Task Ownership_stale_version_invalid_enum_and_noop_are_safe()
    {
        await using var s=new ProjectSqlTests.Scope();await using var other=new ProjectSqlTests.Scope();var id=await Submitted(s);await Decide(s,id,0,ReviewDecision.Approved);
        var p=(await s.Projects.DetailsAsync(id))!;var input=new CommercialInput{ProjectId=id,ProductId=p.Products[0].Id,Version=p.UpdatedAtUtc,Status=CommercialStatus.PriceOfferSubmitted};
        Assert.Null(await Commercial(other).FormAsync(id,input.ProductId,default));await Assert.ThrowsAsync<ValidationException>(()=>Commercial(other).UpdateAsync(input));
        input.Status=(CommercialStatus)999;await Assert.ThrowsAsync<ValidationException>(()=>Commercial(s).UpdateAsync(input));input.Status=CommercialStatus.PriceOfferSubmitted;
        await Commercial(s).UpdateAsync(input);await Assert.ThrowsAsync<ValidationException>(()=>Commercial(s).UpdateAsync(input));
        await Status(s,id,0,CommercialStatus.PriceOfferSubmitted);Assert.Single((await s.Projects.DetailsAsync(id))!.Products[0].CommercialHistory);
    }
    [Fact]
    public async Task Rejection_dictionary_enforces_active_reason_other_comment_and_preserves_name()
    {
        await using var s=new ProjectSqlTests.Scope();var id=await Submitted(s);var p=(await s.Projects.DetailsAsync(id))!;
        var form=(await Review(s).FormAsync(id,p.Products[0].Id,ReviewDecision.Rejected))!;
        Assert.Equal(10,form.RejectionReasons.Count);Assert.Contains(form.RejectionReasons,x=>x.Name=="Other" && x.RequiresComment);
        var input=form.Input;input.Comment="Text alone is insufficient";
        await Assert.ThrowsAsync<ValidationException>(()=>Review(s).DecideAsync(input));
        input.RejectionReasonId=10;input.Comment="  ";await Assert.ThrowsAsync<ValidationException>(()=>Review(s).DecideAsync(input));
        var reason=new RejectionReason{Name=s.Login+" reason",IsActive=false,CreatedAtUtc=DateTimeOffset.UtcNow,UpdatedAtUtc=DateTimeOffset.UtcNow};s.Db.RejectionReasons.Add(reason);await s.Db.SaveChangesAsync();
        input.RejectionReasonId=reason.Id;Assert.DoesNotContain((await Review(s).FormAsync(id,p.Products[0].Id,ReviewDecision.Rejected))!.RejectionReasons,x=>x.Id==reason.Id);
        await Assert.ThrowsAsync<ValidationException>(()=>Review(s).DecideAsync(input));
        reason.IsActive=true;await s.Db.SaveChangesAsync();input.Comment=null;await Review(s).DecideAsync(input);
        reason.IsActive=false;reason.Name=s.Login+" renamed";await s.Db.SaveChangesAsync();
        var history=Assert.Single((await s.Projects.DetailsAsync(id))!.Products[0].Reviews);Assert.Equal(s.Login+" reason",history.RejectionReason);
        Assert.Equal(reason.Id,await s.Db.ProductReviews.Where(x=>x.ProjectProductId==p.Products[0].Id).Select(x=>x.RejectionReasonId).SingleAsync());
    }
    [Fact]
    public async Task Legacy_rejection_and_product_type_still_render()
    {
        await using var s=new ProjectSqlTests.Scope();var id=await Submitted(s);var p=(await s.Projects.DetailsAsync(id))!;var user=await s.Users.GetCurrentAsync();
        await s.Db.ProjectProducts.Where(x=>x.Id==p.Products[0].Id).ExecuteUpdateAsync(x=>x.SetProperty(p=>p.ProductTypeId,1).SetProperty(p=>p.ProductCategoryId,(int?)null).SetProperty(p=>p.Subcategory,(string?)null).SetProperty(p=>p.ReviewStatus,ProductReviewStatus.Rejected).SetProperty(p=>p.ReviewComment,"Historical comment"));
        s.Db.ProductReviews.Add(new ProductReview{ProjectProductId=p.Products[0].Id,ReviewerId=user.Id,Decision=ReviewDecision.Rejected,Comment="Historical comment"});await s.Db.SaveChangesAsync();
        var legacy=(await s.Projects.DetailsAsync(id))!.Products[0];Assert.Equal("Shampoo",legacy.ProductType);Assert.Null(legacy.Category);Assert.Equal("Historical comment",Assert.Single(legacy.Reviews).Comment);Assert.Null(legacy.Reviews[0].RejectionReason);
        Assert.Equal(4,await s.Db.ProductTypes.CountAsync());
    }
    [Theory]
    [InlineData("category")]
    [InlineData("subcategory")]
    [InlineData("long")]
    public void New_products_require_bounded_category_and_subcategory(string missing)
    {
        var input=WizardTests.ValidDraft();if(missing=="category")input.Products[0].ProductCategoryId=null;else if(missing=="subcategory")input.Products[0].ProductSubcategoryId=null;else input.Products[0].Subcategory=new string('a',101);
        Assert.Throws<ValidationException>(()=>ProjectService.Validate(input));
    }
    [Fact]
    public async Task Inactive_category_is_excluded_and_rejected_and_subcategory_is_trimmed()
    {
        await using var s=new ProjectSqlTests.Scope();var category=new ProductCategory{Name=s.Login+" category",IsActive=false};s.Db.ProductCategories.Add(category);await s.Db.SaveChangesAsync();
        Assert.DoesNotContain(await s.Projects.ProductCategoriesAsync(),x=>x.Id==category.Id);
        var input=WizardTests.ValidDraft();input.Products[0].ProductCategoryId=category.Id;await Assert.ThrowsAsync<ValidationException>(()=>s.Projects.SaveDraftAsync(input));
        input.Products[0].ProductCategoryId=1;input.Products[0].Subcategory="  Szampon  ";var id=await s.Projects.SaveDraftAsync(input);Assert.Equal(await s.Db.ProductSubcategories.Where(x=>x.Id==DictionaryFixture.ShampooId).Select(x=>x.Name).SingleAsync(),(await s.Projects.DetailsAsync(id))!.Products[0].ProductType);
        Assert.Null(await s.Db.ProjectProducts.Where(x=>x.ProjectId==id).Select(x=>x.ProductTypeId).FirstAsync());
    }
    [Theory]
    [InlineData("/Commercial/Change")]
    [InlineData("/Dictionaries/Edit")]
    public async Task New_posts_do_not_accept_requests_without_csrf(string path)
    {
        await using var factory=new AuthenticatedFactory();using var client=factory.CreateClient();client.DefaultRequestHeaders.Add("X-Test-User","reader");
        var response=await client.PostAsync(path,new FormUrlEncodedContent([]));Assert.Contains(response.StatusCode,new[]{HttpStatusCode.BadRequest,HttpStatusCode.Forbidden});
    }
}
