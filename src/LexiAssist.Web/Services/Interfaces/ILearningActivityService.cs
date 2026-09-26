using LexiAssist.Web.Models.Activities;

namespace LexiAssist.Web.Services.Interfaces;

public interface ILearningActivityService
{
    Task<IReadOnlyList<ActivityRecommendation>> GetRecommendationsAsync(
        int screeningReportId,
        CancellationToken cancellationToken = default);

    Task<ActivityAssignment> AssignAsync(
        int screeningReportId,
        int learningActivityId,
        string assignedByUserId,
        CancellationToken cancellationToken = default);
}

public sealed record ActivityRecommendation(
    int LearningActivityId,
    string ActivityName,
    string Description,
    IReadOnlyList<string> MatchedIndicators);

public sealed class ActivityAssignmentException(string message) : InvalidOperationException(message);
