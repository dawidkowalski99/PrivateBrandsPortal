using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;
namespace PrivateBrandsPortal.Web.Configuration;

// Form decimals accept comma or dot, never thousands separators.
// One central parser prevents culture-dependent silent scaling (31,5 -> 315).
public sealed class DecimalModelBinder : IModelBinder
{
    public static bool TryParse(string text, out decimal value)
    {
        const NumberStyles styles = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
        return decimal.TryParse(text.Trim(), styles, CultureInfo.GetCultureInfo("pl-PL"), out value)
            || decimal.TryParse(text.Trim(), styles, CultureInfo.InvariantCulture, out value);
    }
    public Task BindModelAsync(ModelBindingContext context)
    {
        var result = context.ValueProvider.GetValue(context.ModelName);
        if (result == ValueProviderResult.None) return Task.CompletedTask;
        context.ModelState.SetModelValue(context.ModelName, result);
        var text = result.FirstValue;
        if (string.IsNullOrWhiteSpace(text) && Nullable.GetUnderlyingType(context.ModelType) is not null)
            context.Result = ModelBindingResult.Success(null);
        else if (text is not null && TryParse(text, out var value))
            context.Result = ModelBindingResult.Success(value);
        else context.ModelState.TryAddModelError(context.ModelName, "Enter a decimal number, e.g. 31,50.");
        return Task.CompletedTask;
    }
}
public sealed class DecimalModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context) =>
        (Nullable.GetUnderlyingType(context.Metadata.ModelType) ?? context.Metadata.ModelType) == typeof(decimal)
            ? new DecimalModelBinder() : null;
}

