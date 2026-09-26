namespace LexiAssist.Web.Models.Screening;

/// <summary>
/// Explicit percentage rules for non-diagnostic screening/support tiers.
/// These are configurable operational rules, not clinical cutoffs.
/// </summary>
public sealed class ScreeningRiskClassificationOptions
{
    public string CalculationVersion { get; set; } = "1.0";

    /// <summary>Results strictly below this percentage map to Red.</summary>
    public decimal RedBelowPercentage { get; set; } = 50m;

    /// <summary>Results below this percentage, and at or above Red, map to Amber.</summary>
    public decimal AmberBelowPercentage { get; set; } = 75m;
}
