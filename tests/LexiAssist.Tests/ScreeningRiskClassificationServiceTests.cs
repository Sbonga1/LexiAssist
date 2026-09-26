using LexiAssist.Web.Models.Screening;
using LexiAssist.Web.Services;
using Microsoft.Extensions.Options;

namespace LexiAssist.Tests;

public sealed class ScreeningRiskClassificationServiceTests
{
    private static readonly DateTimeOffset FixedCalculationTime =
        new(2026, 9, 21, 12, 30, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, ScreeningSupportTier.Red)]
    [InlineData(49.99, ScreeningSupportTier.Red)]
    [InlineData(50, ScreeningSupportTier.Amber)]
    [InlineData(50.01, ScreeningSupportTier.Amber)]
    [InlineData(74.99, ScreeningSupportTier.Amber)]
    [InlineData(75, ScreeningSupportTier.Green)]
    [InlineData(75.01, ScreeningSupportTier.Green)]
    [InlineData(100, ScreeningSupportTier.Green)]
    public void Uses_explicit_thresholds_with_exact_boundary_semantics(
        double percentage,
        ScreeningSupportTier expectedTier)
    {
        var service = CreateService();

        var result = service.Classify(CompletedResult((decimal)percentage));

        Assert.NotNull(result);
        Assert.Equal(expectedTier, result.Tier);
    }

    [Fact]
    public void Custom_thresholds_are_applied_without_code_changes()
    {
        var service = CreateService(new ScreeningRiskClassificationOptions
        {
            CalculationVersion = "support-rules-2",
            RedBelowPercentage = 20m,
            AmberBelowPercentage = 40m
        });

        Assert.Equal(ScreeningSupportTier.Red, service.Classify(CompletedResult(19.99m))?.Tier);
        Assert.Equal(ScreeningSupportTier.Amber, service.Classify(CompletedResult(20m))?.Tier);
        Assert.Equal(ScreeningSupportTier.Amber, service.Classify(CompletedResult(39.99m))?.Tier);
        Assert.Equal(ScreeningSupportTier.Green, service.Classify(CompletedResult(40m))?.Tier);
    }

    [Theory]
    [InlineData(ScreeningCompletionStatus.NotStarted)]
    [InlineData(ScreeningCompletionStatus.InProgress)]
    public void Incomplete_screenings_do_not_receive_a_tier(ScreeningCompletionStatus status)
    {
        var result = CreateService().Classify(CompletedResult(10m, status));

        Assert.Null(result);
    }

    [Fact]
    public void Completed_result_without_a_scorable_percentage_does_not_default_to_green()
    {
        var result = CreateService().Classify(CompletedResult(null));

        Assert.Null(result);
    }

    [Fact]
    public void Result_contains_description_version_utc_date_and_non_diagnostic_notice()
    {
        var service = CreateService(new ScreeningRiskClassificationOptions
        {
            CalculationVersion = "rules-2026-09",
            RedBelowPercentage = 40m,
            AmberBelowPercentage = 70m
        });

        var result = service.Classify(CompletedResult(30m));

        Assert.NotNull(result);
        Assert.Equal(ScreeningSupportTier.Red, result.Tier);
        Assert.Equal("Strong screening indicators; professional follow-up recommended.", result.Description);
        Assert.Equal(30m, result.OverallPercentage);
        Assert.Equal("rules-2026-09", result.CalculationVersion);
        Assert.Equal(FixedCalculationTime.UtcDateTime, result.CalculatedAtUtc);
        Assert.Equal(DateTimeKind.Utc, result.CalculatedAtUtc.Kind);
        Assert.Contains("screening/support tiers", ScreeningRiskClassificationResult.NonDiagnosticNotice);
        Assert.Contains("not clinical diagnoses", ScreeningRiskClassificationResult.NonDiagnosticNotice);
    }

    [Theory]
    [InlineData(101, 110)]
    [InlineData(50, 50)]
    [InlineData(75, 50)]
    [InlineData(-1, 75)]
    [InlineData(50, 101)]
    public void Invalid_or_overlapping_thresholds_are_rejected(decimal redBelow, decimal amberBelow)
    {
        var service = CreateService(new ScreeningRiskClassificationOptions
        {
            RedBelowPercentage = redBelow,
            AmberBelowPercentage = amberBelow
        });

        Assert.Throws<InvalidOperationException>(() => service.Classify(CompletedResult(50m)));
    }

    [Fact]
    public void Missing_calculation_version_is_rejected()
    {
        var service = CreateService(new ScreeningRiskClassificationOptions { CalculationVersion = " " });

        Assert.Throws<InvalidOperationException>(() => service.Classify(CompletedResult(50m)));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(100.01)]
    public void Percentages_outside_the_valid_range_are_rejected(double percentage)
    {
        var service = CreateService();

        Assert.Throws<ArgumentOutOfRangeException>(() => service.Classify(CompletedResult((decimal)percentage)));
    }

    private static ScreeningRiskClassificationService CreateService(
        ScreeningRiskClassificationOptions? options = null) => new(
            Options.Create(options ?? new ScreeningRiskClassificationOptions()),
            new FixedTimeProvider(FixedCalculationTime));

    private static ScreeningOverallScoringResult CompletedResult(
        decimal? percentage,
        ScreeningCompletionStatus status = ScreeningCompletionStatus.Completed) => new(
        RawScore: percentage ?? 0m,
        AvailableScore: percentage is null ? 0m : 100m,
        Percentage: percentage,
        FlaggedSkillAreas: Array.Empty<string>(),
        CompletionStatus: status,
        AnsweredQuestionCount: status == ScreeningCompletionStatus.Completed ? 1 : 0,
        ExpectedQuestionCount: 1,
        TestResults: Array.Empty<ScreeningTestScoringResult>());

    private sealed class FixedTimeProvider(DateTimeOffset currentTime) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => currentTime;
    }
}
