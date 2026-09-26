namespace LexiAssist.Web.Services.Interfaces;

/// <summary>
/// Events that can receive a short, predefined practice message.
/// These messages describe the practice interaction only; they are not clinical
/// interpretations of a learner's performance.
/// </summary>
public enum SupportiveFeedbackEvent
{
    CorrectResponse = 1,
    FirstIncorrectAttempt = 2,
    RepeatedDifficulty = 3,
    LongInactivity = 4,
    SuccessfulRetry = 5,
    ActivityCompleted = 6
}

public sealed record SupportiveFeedbackContext(
    SupportiveFeedbackEvent Event,
    string? ActivityName = null,
    string? ItemLabel = null,
    int ConsecutiveIncorrectAnswers = 0,
    TimeSpan? InactivityDuration = null);

public interface ISupportiveFeedbackService
{
    string GetFeedback(SupportiveFeedbackContext context);

    string GetFeedback(SupportiveFeedbackEvent feedbackEvent, string? activityName = null);
}
