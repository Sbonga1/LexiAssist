using LexiAssist.Web.Models.Screening;

namespace LexiAssist.Web.Services.Interfaces;

public interface IScreeningScoringService
{
    /// <summary>
    /// Scores active, answer-key questions in a test. A previously started test
    /// may opt into scoring its still-active questions after category deactivation.
    /// Unanswered scorable items count in the available score, while unscorable
    /// items affect completion only.
    /// </summary>
    ScreeningTestScoringResult ScoreTest(
        ScreeningTest screeningTest,
        IEnumerable<ScreeningResponse> responses,
        bool includeInactive = false);

    /// <summary>
    /// Combines test results by summing raw and available points. Percentages are
    /// never averaged, so differently sized tests retain their configured weights.
    /// </summary>
    ScreeningOverallScoringResult ScoreOverall(
        IEnumerable<ScreeningTestScoringResult> testResults);
}
