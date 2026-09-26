using LexiAssist.Web.Models.Screening;

namespace LexiAssist.Web.Services.Interfaces;

/// <summary>
/// Applies explicit configurable support-routing rules to completed screening results.
/// </summary>
public interface IScreeningRiskClassificationService
{
    /// <summary>
    /// Returns null when the screening is incomplete or has no scorable percentage.
    /// </summary>
    ScreeningRiskClassificationResult? Classify(ScreeningOverallScoringResult screeningResult);
}
