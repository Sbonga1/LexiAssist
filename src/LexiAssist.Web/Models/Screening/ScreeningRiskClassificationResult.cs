namespace LexiAssist.Web.Models.Screening;

public sealed record ScreeningRiskClassificationResult(
    ScreeningSupportTier Tier,
    string Description,
    decimal OverallPercentage,
    string CalculationVersion,
    DateTime CalculatedAtUtc)
{
    public const string NonDiagnosticNotice =
        "These screening/support tiers are for support planning only. They are not clinical diagnoses.";

    public static string GetTierDescription(ScreeningSupportTier tier) => tier switch
    {
        ScreeningSupportTier.Green => "Mild screening indicators; standard support.",
        ScreeningSupportTier.Amber => "Moderate screening indicators; additional support review.",
        ScreeningSupportTier.Red => "Strong screening indicators; professional follow-up recommended.",
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown screening support tier.")
    };
}
