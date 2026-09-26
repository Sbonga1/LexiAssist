using LexiAssist.Web.Services;
using LexiAssist.Web.Services.Interfaces;

namespace LexiAssist.Tests;

public sealed class SupportiveFeedbackServiceTests
{
    private readonly SupportiveFeedbackService service = new();

    [Theory]
    [InlineData(SupportiveFeedbackEvent.CorrectResponse)]
    [InlineData(SupportiveFeedbackEvent.FirstIncorrectAttempt)]
    [InlineData(SupportiveFeedbackEvent.RepeatedDifficulty)]
    [InlineData(SupportiveFeedbackEvent.LongInactivity)]
    [InlineData(SupportiveFeedbackEvent.SuccessfulRetry)]
    [InlineData(SupportiveFeedbackEvent.ActivityCompleted)]
    public void Every_supported_event_returns_safe_predefined_text(SupportiveFeedbackEvent feedbackEvent)
    {
        var message = service.GetFeedback(new SupportiveFeedbackContext(
            feedbackEvent,
            ActivityName: "Sound Builder"));

        Assert.False(string.IsNullOrWhiteSpace(message));
        Assert.Contains("Sound Builder", message);
        Assert.DoesNotContain("shame", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stupid", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("smart", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("genius", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dyslexia", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("diagnos", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Same_context_always_returns_the_same_message()
    {
        var context = new SupportiveFeedbackContext(
            SupportiveFeedbackEvent.RepeatedDifficulty,
            ActivityName: "Word Reader",
            ItemLabel: "item-2",
            ConsecutiveIncorrectAnswers: 3);

        Assert.Equal(service.GetFeedback(context), service.GetFeedback(context));
    }

    [Fact]
    public void Event_overload_matches_context_overload()
    {
        Assert.Equal(
            service.GetFeedback(SupportiveFeedbackEvent.ActivityCompleted, "Fast Name Race"),
            service.GetFeedback(new SupportiveFeedbackContext(
                SupportiveFeedbackEvent.ActivityCompleted,
                ActivityName: "Fast Name Race")));
    }

    [Fact]
    public void Long_inactivity_message_invites_a_pause_without_making_a_claim()
    {
        var message = service.GetFeedback(new SupportiveFeedbackContext(
            SupportiveFeedbackEvent.LongInactivity,
            InactivityDuration: TimeSpan.FromMinutes(10)));

        Assert.Contains("take your time", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("continue", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Missing_activity_name_does_not_add_empty_prefix()
    {
        var message = service.GetFeedback(SupportiveFeedbackEvent.CorrectResponse);

        Assert.DoesNotContain(": ", message);
        Assert.StartsWith("Your response", message, StringComparison.Ordinal);
    }
}
