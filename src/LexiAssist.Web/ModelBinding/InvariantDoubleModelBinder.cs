using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace LexiAssist.Web.ModelBinding;

/// <summary>
/// HTML number controls submit decimal values with a dot regardless of the page culture.
/// Bind those values invariantly, while still accepting a localized decimal separator.
/// </summary>
public sealed class InvariantDoubleModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        ArgumentNullException.ThrowIfNull(bindingContext);

        var valueResult = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (valueResult == ValueProviderResult.None)
        {
            return Task.CompletedTask;
        }

        bindingContext.ModelState.SetModelValue(bindingContext.ModelName, valueResult);
        var rawValue = valueResult.FirstValue;
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            bindingContext.Result = ModelBindingResult.Success(0d);
            return Task.CompletedTask;
        }

        const NumberStyles styles = NumberStyles.Float;
        if (double.TryParse(rawValue, styles, CultureInfo.InvariantCulture, out var parsed) ||
            (CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator != "." &&
             rawValue.Contains(CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator, StringComparison.Ordinal) &&
             double.TryParse(rawValue, styles, CultureInfo.CurrentCulture, out parsed)))
        {
            bindingContext.Result = ModelBindingResult.Success(parsed);
            return Task.CompletedTask;
        }

        bindingContext.ModelState.TryAddModelError(bindingContext.ModelName, "Enter a valid number.");
        return Task.CompletedTask;
    }
}
