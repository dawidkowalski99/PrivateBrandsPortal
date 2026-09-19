using System.ComponentModel.DataAnnotations;
using System.Net;
using PrivateBrandsPortal.Web.Configuration;
using PrivateBrandsPortal.Web.Models.Enums;
using PrivateBrandsPortal.Web.Services;
using PrivateBrandsPortal.Web.ViewModels;
namespace PrivateBrandsPortal.Tests;

public sealed class WizardTests
{
    internal static DraftInput ValidDraft() => new() {
        Brief = new() { Customer = "  Test customer  ", CountryId = 2 },
        Products = [
            new() { ProductTypeId = 1, SKU = "  SKU-1  ", Quantity = 20000, EstimatedValue = 150000m, EstimatedMargin = 31.5m, FormulaStatus = FormulaStatus.ReadyToGo },
            new() { ProductTypeId = 3, SKU = "SKU-2", Quantity = 10000, EstimatedValue = 95000m, EstimatedMargin = 28m, FormulaStatus = FormulaStatus.NewFormula }
        ]
    };
    [Fact]
    public void Validation_trims_text_and_accepts_multiple_products()
    {
        var input = ValidDraft();
        ProjectService.Validate(input);
        Assert.Equal("Test customer", input.Brief.Customer);
        Assert.Equal("SKU-1", input.Products[0].SKU);
    }
    [Fact]
    public void Draft_requires_at_least_one_product()
    {
        var input = ValidDraft(); input.Products.Clear();
        Assert.Throws<ValidationException>(() => ProjectService.Validate(input));
    }
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Quantity_must_be_positive(int quantity)
    {
        var input = ValidDraft(); input.Products[0].Quantity = quantity;
        Assert.Throws<ValidationException>(() => ProjectService.Validate(input));
    }
    [Theory]
    [InlineData("-0.01", "31.5")]
    [InlineData("100", "-0.01")]
    [InlineData("100", "100.01")]
    [InlineData("100.001", "31")]
    [InlineData("100", "31.555")]
    public void Invalid_estimates_are_rejected(string value, string margin)
    {
        var input = ValidDraft();
        input.Products[0].EstimatedValue = decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        input.Products[0].EstimatedMargin = decimal.Parse(margin, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Throws<ValidationException>(() => ProjectService.Validate(input));
    }
    [Theory]
    [InlineData("31,5", "31.5")]
    [InlineData("31.5", "31.5")]
    [InlineData("150000,00", "150000")]
    public void Decimal_binding_supports_both_separators(string input, string expected)
    {
        Assert.True(DecimalModelBinder.TryParse(input, out var result));
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), result);
    }
    [Theory]
    [InlineData("1,000.50")]
    [InlineData("1.000,50")]
    [InlineData("NaN")]
    public void Ambiguous_numbers_are_rejected(string input) => Assert.False(DecimalModelBinder.TryParse(input, out _));
    [Fact]
    public async Task Wizard_state_is_owner_scoped_and_serializes_concurrent_requests()
    {
        using var store = new WizardStore();
        var token = store.Create(1, ValidDraft());
        Assert.Null(await store.WithAsync<object>(token, 2, _ => Task.FromResult<object>("leak"), default));
        var actions = Enumerable.Range(0, 10).Select(_ => store.WithAsync(token, 1, async state => {
            var version = state.Revision; await Task.Yield(); state.Revision = version + 1; return new object();
        }, default));
        await Task.WhenAll(actions);
        var revision = await store.WithAsync(token, 1, s => Task.FromResult(s.Revision.ToString()), default);
        Assert.Equal("10", revision);
    }
    [Theory]
    [InlineData("/Projects/Brief")]
    [InlineData("/Projects/Product")]
    [InlineData("/Projects/Remove")]
    [InlineData("/Projects/Save")]
    public async Task Every_wizard_post_requires_antiforgery(string route)
    {
        await using var factory = new AuthenticatedFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "reader");
        var response = await client.PostAsync(route, new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
