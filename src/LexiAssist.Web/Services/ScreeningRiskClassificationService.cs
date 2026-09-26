using LexiAssist.Web.Models.Screening;
using LexiAssist.Web.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace LexiAssist.Web.Services;

public sealed class ScreeningRiskClassificationService(
    IOptions<ScreeningRiskClassificationOptions> configuredOptions,
    TimeProvider timeProvider) : IScreeningRiskClassificationService
{
    private readonly ScreeningRiskClassificationOptions options = configuredOptions.Value;

    public ScreeningRiskClassificationResult? Classify(ScreeningOverallScoringResult screeningResult)
    {
        ArgumentNullException.ThrowIfNull(screeningResult);
        ValidateOptions();

        if (screeningResult.CompletionStatus != ScreeningCompletionStatus.Completed ||
            screeningResult.Percentage is null)
        {
            return null;
        }

        if (screeningResult.Percentage is < 0m or > 100m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(screeningResult),
                "The screening percentage must be between 0 and 100.");
        }

        var tier = screeningResult.Percentage < options.RedBelowPercentage
            ? ScreeningSupportTier.Red
            : screeningResult.Percentage < options.AmberBelowPercentage
                ? ScreeningSupportTier.Amber
                : ScreeningSupportTier.Green;

        return new ScreeningRiskClassificationResult(
            tier,
            ScreeningRiskClassificationResult.GetTierDescription(tier),
            screeningResult.Percentage.Value,
            options.CalculationVersion.Trim(),
            timeProvider.GetUtcNow().UtcDateTime);
    }

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(options.CalculationVersion) || options.CalculationVersion.Trim().Length > 64)
        {
            throw new InvalidOperationException("A calculation version from 1 to 64 characters is required.");
        }

        if (options.RedBelowPercentage is < 0m or > 100m ||
            options.AmberBelowPercentage is < 0m or > 100m ||
            options.RedBelowPercentage >= options.AmberBelowPercentage)
        {
            throw new InvalidOperationException(
                "Risk classification thresholds must satisfy 0 <= RedBelowPercentage < AmberBelowPercentage <= 100.");
        }
    }

}
