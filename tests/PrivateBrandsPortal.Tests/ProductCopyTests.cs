using Microsoft.EntityFrameworkCore;
using PrivateBrandsPortal.Web.Models.Enums;
using PrivateBrandsPortal.Web.Services;
namespace PrivateBrandsPortal.Tests;

[Collection("SQL integration")]
public sealed class ProductCopyTests
{
    [Fact]
    public void Duplicate_has_new_identity_and_does_not_mutate_source()
    {
        var source=WizardTests.ValidDraft().Products[0];source.PersistedId=123;
        var copy=ProductCopyService.Duplicate(source);
        Assert.NotEqual(source.Key,copy.Key);Assert.Null(copy.PersistedId);Assert.Null(copy.ProductTypeId);
        Assert.Equal(source.EstimatedValue,copy.EstimatedValue);copy.SKU="CHANGED";Assert.NotEqual(source.SKU,copy.SKU);
    }
    [Fact]
    public async Task Copy_from_checks_ownership_and_creates_pending_product_without_history()
    {
        await using var owner=new ProjectSqlTests.Scope();await using var stranger=new ProjectSqlTests.Scope();
        var id=await owner.Projects.SaveDraftAsync(WizardTests.ValidDraft());
        var source=await owner.Db.ProjectProducts.Where(x=>x.ProjectId==id).FirstAsync();
        await owner.Db.ProjectProducts.Where(x=>x.Id==source.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ReviewStatus,ProductReviewStatus.Approved).SetProperty(x=>x.CommercialStatus,CommercialStatus.SalesAndDelivery));
        var copyService=new ProductCopyService(owner.Db,owner.Users);
        var denied=new ProductCopyService(stranger.Db,stranger.Users);
        Assert.Null(await denied.CopyAsync(source.Id,default));Assert.Empty(await denied.SourcesAsync(null,default));
        var copy=await copyService.CopyAsync(source.Id,default);Assert.NotNull(copy);Assert.Null(copy.PersistedId);
        Assert.Empty(copy.SKU); Assert.Equal(source.ProductSubcategoryId,copy.ProductSubcategoryId); copy.SKU=source.SKU; var draft=WizardTests.ValidDraft();draft.Products=[copy];var created=await owner.Projects.SaveDraftAsync(draft);
        var saved=await owner.Db.ProjectProducts.AsNoTracking().SingleAsync(x=>x.ProjectId==created);
        Assert.Equal(ProductReviewStatus.Pending,saved.ReviewStatus);Assert.Null(saved.CommercialStatus);
        Assert.False(await owner.Db.ProductReviews.AnyAsync(x=>x.ProjectProductId==saved.Id));
        Assert.False(await owner.Db.AuditLogs.AnyAsync(x=>x.EntityType=="ProjectProduct"&&x.EntityId==saved.Id));
        Assert.Equal(2,(await copyService.SourcesAsync(source.SKU,default)).Count);
    }
}
