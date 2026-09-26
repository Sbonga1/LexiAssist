using System.Globalization;
using System.Text.Json;
using LexiAssist.Web.Models.Reports;
using LexiAssist.Web.Models.Referrals;
using LexiAssist.Web.Models.Screening;
using LexiAssist.Web.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace LexiAssist.Web.Services;

public sealed class ScreeningReportGenerationService(
    IOptions<ScreeningScoringOptions> configuredOptions,
    TimeProvider timeProvider) : IScreeningReportGenerationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ScreeningScoringOptions options = configuredOptions.Value;

    public ScreeningReport Generate(
        string userId,
        int screeningAttemptId,
        ScreeningOverallScoringResult scoringResult,
        ScreeningRiskClassificationResult? riskClassification)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(screeningAttemptId);
        ArgumentNullException.ThrowIfNull(scoringResult);

        if (scoringResult.CompletionStatus != ScreeningCompletionStatus.Completed)
        {
            throw new InvalidOperationException("A report can only be generated for a completed screening.");
        }

        var scoringVersion = options.ScoringVersion?.Trim();
        if (string.IsNullOrWhiteSpace(scoringVersion) || scoringVersion.Length > 64)
        {
            throw new InvalidOperationException("The scoring version must contain between 1 and 64 characters.");
        }

        var skillAreas = CreateSkillAreaSnapshots(scoringResult.TestResults);
        var flaggedAreas = skillAreas
            .Where(area => area.IsFlaggedForSupport)
            .Select(area => new ScreeningReportFlaggedAreaSnapshot(area.Name, area.PercentageScore))
            .ToArray();

        var generatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        var referralRecommended = riskClassification?.Tier == ScreeningSupportTier.Red;
        var report = new ScreeningReport
        {
            ScreeningAttemptId = screeningAttemptId,
            UserId = userId,
            GeneratedAtUtc = generatedAtUtc,
            RiskTier = riskClassification?.Tier.ToString(),
            Summary = BuildSummary(scoringResult.Percentage, riskClassification?.Tier),
            FlaggedAreasJson = JsonSerializer.Serialize(flaggedAreas, JsonOptions),
            ScoringVersion = scoringVersion,
            // Amber calls for additional review; Red carries the explicit professional follow-up recommendation.
            IsReferralRecommended = referralRecommended,
            SkillAreasJson = JsonSerializer.Serialize(skillAreas, JsonOptions),
            RecommendedNextAction = BuildRecommendedNextAction(riskClassification?.Tier)
        };

        if (referralRecommended)
        {
            report.Referral = new Referral
            {
                LearnerId = userId,
                Status = ReferralStatus.Pending,
                CreatedAtUtc = generatedAtUtc
            };
        }

        return report;
    }

    private static ScreeningReportSkillAreaSnapshot[] CreateSkillAreaSnapshots(
        IReadOnlyList<ScreeningTestScoringResult> testResults)
    {
        var areas = testResults
            .Where(result => result.ExpectedQuestionCount > 0)
            .OrderBy(result => result.ScreeningTestId)
            .Select(result =>
            {
                var isFlagged = result.FlaggedSkillAreas.Count > 0;
                var isStrengthCandidate = result.Percentage is > 0 && !isFlagged;
                var summary = result.Percentage is { } percentage
                    ? $"Responses matched {percentage.ToString("0.##", CultureInfo.InvariantCulture)}% of the configured answer-key points. " +
                      (isFlagged
                          ? "This area met the configured rule for additional support review."
                          : "The configured review rule did not flag this area for additional support review.")
                    : "An objective answer-key summary was not available for this area.";

                return new ScreeningReportSkillAreaSnapshot(
                    result.TestName,
                    result.Percentage,
                    isStrengthCandidate,
                    isFlagged,
                    summary)
                {
                    ActivityMetrics = result.ActivityMetrics
                };
            })
            .ToArray();

        var highestUnflaggedMatchRate = areas
            .Where(area => area.IsStrength)
            .Select(area => area.PercentageScore!.Value)
            .DefaultIfEmpty()
            .Max();

        return areas.Select(area => area with
            {
                IsStrength = highestUnflaggedMatchRate > 0 &&
                             area.IsStrength &&
                             area.PercentageScore == highestUnflaggedMatchRate
            })
            .ToArray();
    }

    private static string BuildSummary(decimal? percentage, ScreeningSupportTier? tier)
    {
        var scoreSummary = percentage is { } score
            ? $"The overall configured answer-key result was {score.ToString("0.##", CultureInfo.InvariantCulture)}% of available points. "
            : "An overall answer-key result was not available. ";
        var tierSummary = tier is { } supportTier
            ? $"The {supportTier} screening/support tier reflects the configured rules at report generation. "
            : "There was not enough scored information to calculate a screening/support tier. ";

        return $"{scoreSummary}{tierSummary}This is an educational screening result, not a clinical diagnosis.";
    }

    private static string BuildRecommendedNextAction(ScreeningSupportTier? tier) => tier switch
    {
        ScreeningSupportTier.Green => "Continue with learning supports that work for you. If you or someone supporting you has questions, consider discussing this result with a suitable support professional.",
        ScreeningSupportTier.Amber => "Consider talking with a trusted adult or suitable support professional about the areas below. A suitable support professional may review this screening according to the LexiAssist support workflow.",
        ScreeningSupportTier.Red => "Consider discussing next steps with a suitable support professional. A suitable support professional may review this screening according to the LexiAssist support workflow.",
        _ => "If you would like help planning next steps, consider reviewing the area summaries with a trusted support person or suitable professional."
    };
}
