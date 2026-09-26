using System.Text.Json;
using LexiAssist.Web.Models.Reports;
using LexiAssist.Web.Models.Referrals;
using LexiAssist.Web.Models.Screening;
using LexiAssist.Web.Services;
using Microsoft.Extensions.Options;

namespace LexiAssist.Tests;

public sealed class ScreeningReportGenerationServiceTests
{
    private static readonly DateTime GeneratedAtUtc = new(2026, 9, 21, 12, 30, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(ScreeningSupportTier.Green, false)]
    [InlineData(ScreeningSupportTier.Amber, false)]
    [InlineData(ScreeningSupportTier.Red, true)]
    public void Generates_a_non_diagnostic_snapshot_with_tier_appropriate_referral_recommendation(
        ScreeningSupportTier tier,
        bool isReferralRecommended)
    {
        var service = CreateService();
        var report = service.Generate("learner-123", 42, CreateScoringResult(), CreateClassification(tier));

        Assert.Equal(42, report.ScreeningAttemptId);
        Assert.Equal("learner-123", report.UserId);
        Assert.Equal(GeneratedAtUtc, report.GeneratedAtUtc);
        Assert.Equal(tier.ToString(), report.RiskTier);
        Assert.Equal("score-rules-1.0", report.ScoringVersion);
        Assert.Equal(isReferralRecommended, report.IsReferralRecommended);
        if (tier == ScreeningSupportTier.Red)
        {
            Assert.NotNull(report.Referral);
            Assert.Equal("learner-123", report.Referral.LearnerId);
            Assert.Equal(ReferralStatus.Pending, report.Referral.Status);
            Assert.Equal(report.GeneratedAtUtc, report.Referral.CreatedAtUtc);
        }
        else
        {
            Assert.Null(report.Referral);
        }
        Assert.Contains("not a clinical diagnosis", report.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("80%", report.Summary);

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var flaggedAreas = JsonSerializer.Deserialize<ScreeningReportFlaggedAreaSnapshot[]>(report.FlaggedAreasJson, options);
        Assert.NotNull(flaggedAreas);
        var flaggedArea = Assert.Single(flaggedAreas);
        Assert.Equal("Word Reading", flaggedArea.Name);
        Assert.Equal(60m, flaggedArea.PercentageScore);

        var skillAreas = JsonSerializer.Deserialize<ScreeningReportSkillAreaSnapshot[]>(report.SkillAreasJson, options);
        Assert.NotNull(skillAreas);
        Assert.Collection(skillAreas,
            area =>
            {
                Assert.Equal("Word Reading", area.Name);
                Assert.False(area.IsStrength);
                Assert.True(area.IsFlaggedForSupport);
                Assert.Equal(60m, area.PercentageScore);
            },
            area =>
            {
                Assert.Equal("Rapid Naming", area.Name);
                Assert.True(area.IsStrength);
                Assert.False(area.IsFlaggedForSupport);
                Assert.Equal(85m, area.PercentageScore);
            });

        var expectedAction = tier is ScreeningSupportTier.Amber or ScreeningSupportTier.Red
            ? "A suitable support professional may review this screening according to the LexiAssist support workflow."
            : "Continue with learning supports that work for you.";
        Assert.Contains(expectedAction, report.RecommendedNextAction);
    }

    [Fact]
    public void Existing_snapshot_does_not_change_when_scoring_options_are_updated()
    {
        var options = new ScreeningScoringOptions { ScoringVersion = "score-rules-1.0" };
        var service = new ScreeningReportGenerationService(Options.Create(options), new FixedTimeProvider(GeneratedAtUtc));
        var report = service.Generate("learner-123", 42, CreateScoringResult(), CreateClassification(ScreeningSupportTier.Amber));
        var originalSummary = report.Summary;
        var originalSkillAreas = report.SkillAreasJson;
        var originalFlaggedAreas = report.FlaggedAreasJson;

        options.ScoringVersion = "score-rules-2.0";
        var changedResult = CreateScoringResult(overallPercentage: 10m);
        _ = service.Generate("learner-123", 43, changedResult, CreateClassification(ScreeningSupportTier.Red));

        Assert.Equal("score-rules-1.0", report.ScoringVersion);
        Assert.Equal("Amber", report.RiskTier);
        Assert.False(report.IsReferralRecommended);
        Assert.Equal(originalSummary, report.Summary);
        Assert.Equal(originalSkillAreas, report.SkillAreasJson);
        Assert.Equal(originalFlaggedAreas, report.FlaggedAreasJson);
    }

    [Fact]
    public void Rejects_incomplete_scoring_result()
    {
        var service = CreateService();
        var incompleteResult = CreateScoringResult() with
        {
            CompletionStatus = ScreeningCompletionStatus.InProgress
        };

        Assert.Throws<InvalidOperationException>(() =>
            service.Generate("learner-123", 42, incompleteResult, null));
    }

    [Fact]
    public void Report_snapshot_preserves_neutral_activity_metrics_without_reinterpreting_them()
    {
        var metric = new ScreeningActivityMetric(
            ScreeningQuestionType.WorkingMemorySequence,
            "Working Memory",
            3,
            3,
            0,
            3,
            100m,
            3,
            null,
            null,
            null,
            null,
            1m,
            100m);
        var scoringResult = CreateScoringResult() with
        {
            TestResults = [CreateScoringResult().TestResults[0] with { ActivityMetrics = [metric] }]
        };

        var report = CreateService().Generate("learner-123", 42, scoringResult, null);

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var areas = JsonSerializer.Deserialize<ScreeningReportSkillAreaSnapshot[]>(report.SkillAreasJson, options);
        Assert.NotNull(areas);
        var savedMetric = Assert.Single(areas[0].ActivityMetrics);
        Assert.Equal(ScreeningQuestionType.WorkingMemorySequence, savedMetric.ActivityType);
        Assert.Equal(3, savedMetric.ItemsCorrect);
        Assert.Equal(100m, savedMetric.AccuracyPercentage);
        Assert.Equal(3, savedMetric.HighestCorrectlyCompletedSequenceLength);
    }

    private static ScreeningReportGenerationService CreateService() =>
        new(Options.Create(new ScreeningScoringOptions { ScoringVersion = "score-rules-1.0" }), new FixedTimeProvider(GeneratedAtUtc));

    private static ScreeningOverallScoringResult CreateScoringResult(decimal overallPercentage = 80m)
    {
        var wordReading = new ScreeningTestScoringResult(
            1,
            "Word Reading",
            "word-reading",
            3m,
            5m,
            60m,
            ["Word Reading"],
            ScreeningCompletionStatus.Completed,
            5,
            5,
            5,
            Array.Empty<ScreeningQuestionScore>());
        var rapidNaming = new ScreeningTestScoringResult(
            4,
            "Rapid Naming",
            "rapid-naming",
            17m,
            20m,
            85m,
            Array.Empty<string>(),
            ScreeningCompletionStatus.Completed,
            20,
            20,
            20,
            Array.Empty<ScreeningQuestionScore>());

        return new ScreeningOverallScoringResult(
            overallPercentage / 100m * 25m,
            25m,
            overallPercentage,
            ["Word Reading"],
            ScreeningCompletionStatus.Completed,
            25,
            25,
            [wordReading, rapidNaming]);
    }

    private static ScreeningRiskClassificationResult CreateClassification(ScreeningSupportTier tier) =>
        new(tier, ScreeningRiskClassificationResult.GetTierDescription(tier), 80m, "risk-rules-1.0", GeneratedAtUtc);

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}
