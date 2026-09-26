using LexiAssist.Web.Services.Interfaces;

namespace LexiAssist.Web.Services;

/// <summary>
/// Provides deterministic, reviewable practice wording. This intentionally has
/// no model or external service dependency so the wording remains predictable.
/// </summary>
public sealed class SupportiveFeedbackService : ISupportiveFeedbackService
{
    private static readonly IReadOnlyDictionary<SupportiveFeedbackEvent, string> Templates =
        new Dictionary<SupportiveFeedbackEvent, string>
        {
            [SupportiveFeedbackEvent.CorrectResponse] = "Your response has been saved. Keep working at a comfortable pace.",
            [SupportiveFeedbackEvent.FirstIncorrectAttempt] = "That item was challenging. Take another look and continue when you’re ready.",
            [SupportiveFeedbackEvent.RepeatedDifficulty] = "This skill is taking practice. We’ll keep the next steps manageable.",
            [SupportiveFeedbackEvent.LongInactivity] = "Take your time. You can continue when you’re ready.",
            [SupportiveFeedbackEvent.SuccessfulRetry] = "Your retry response has been saved. Keep using the strategy that helped.",
            [SupportiveFeedbackEvent.ActivityCompleted] = "You completed this practice activity. Your progress has been saved."
        };

    public string GetFeedback(SupportiveFeedbackContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!Templates.TryGetValue(context.Event, out var template))
        {
            template = "Your practice progress has been saved. Continue when you’re ready.";
        }

        var activityName = context.ActivityName?.Trim();
        return string.IsNullOrWhiteSpace(activityName)
            ? template
            : $"{activityName}: {template}";
    }

    public string GetFeedback(SupportiveFeedbackEvent feedbackEvent, string? activityName = null) =>
        GetFeedback(new SupportiveFeedbackContext(feedbackEvent, activityName));
}
