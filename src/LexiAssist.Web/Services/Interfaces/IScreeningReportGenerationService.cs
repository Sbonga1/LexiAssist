using LexiAssist.Web.Models.Reports;
using LexiAssist.Web.Models.Screening;

namespace LexiAssist.Web.Services.Interfaces;

public interface IScreeningReportGenerationService
{
    ScreeningReport Generate(
        string userId,
        int screeningAttemptId,
        ScreeningOverallScoringResult scoringResult,
        ScreeningRiskClassificationResult? riskClassification);
}
