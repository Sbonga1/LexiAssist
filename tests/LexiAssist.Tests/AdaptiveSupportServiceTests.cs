using LexiAssist.Web.Models.Activities;
using LexiAssist.Web.Services;
using Microsoft.Extensions.Options;

namespace LexiAssist.Tests;

public sealed class AdaptiveSupportServiceTests
{
    private static AdaptiveSupportService CreateService(
        int assigned = 3,
        int minimum = 1,
        int maximum = 5,
        int threshold = 3,
        int reducedItems = 2) =>
        new(Options.Create(new AdaptiveSupportOptions
        {
            AssignedDifficultyLevel = assigned,
            MinimumDifficultyLevel = minimum,
            MaximumDifficultyLevel = maximum,
            IncorrectAnswersBeforeAdaptation = threshold,
            ReducedDifficultyItemCount = reducedItems
        }));

    [Fact]
    public void State_starts_at_assigned_level_with_no_adaptation()
    {
        var service = CreateService();

        var state = service.CreateState([1, 2, 3]);

        Assert.Equal(3, state.AssignedDifficultyLevel);
        Assert.Equal(3, state.CurrentDifficultyLevel);
        Assert.Equal(0, state.ConsecutiveIncorrectAnswers);
        Assert.Equal(0, state.ReducedDifficultyItemsRemaining);
        Assert.Contains("assigned practice level", state.AdaptationReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Exactly_three_consecutive_incorrect_answers_reduce_level_for_two_items()
    {
        var service = CreateService();
        var state = service.CreateState([1, 2, 3]);

        state = service.RecordAnswer(state, false, [1, 1, 2, 2, 3]);
        state = service.RecordAnswer(state, false, [1, 1, 2, 2, 3]);
        state = service.RecordAnswer(state, false, [1, 1, 2, 2, 3]);

        Assert.Equal(0, state.ConsecutiveIncorrectAnswers);
        Assert.Equal(2, state.CurrentDifficultyLevel);
        Assert.Equal(2, state.ReducedDifficultyItemsRemaining);
        Assert.Contains("3 consecutive incorrect answers", state.AdaptationReason);
    }

    [Fact]
    public void Reduced_period_expires_then_returns_one_level_toward_assigned_level()
    {
        var service = CreateService();
        var state = service.CreateState([1, 2, 3]);
        state = service.RecordAnswer(state, false, [1, 1, 2, 2, 3]);
        state = service.RecordAnswer(state, false, [1, 1, 2, 2, 3]);
        state = service.RecordAnswer(state, false, [1, 1, 2, 2, 3]);

        state = service.RecordAnswer(state, true, [2, 2, 3]);
        Assert.Equal(1, state.ReducedDifficultyItemsRemaining);
        Assert.Equal(2, state.CurrentDifficultyLevel);

        state = service.RecordAnswer(state, true, [3]);
        Assert.Equal(0, state.ReducedDifficultyItemsRemaining);
        Assert.Equal(3, state.CurrentDifficultyLevel);
        Assert.Contains("returned to the assigned", state.AdaptationReason);
    }

    [Fact]
    public void Return_to_a_higher_assigned_level_is_gradual()
    {
        var service = CreateService(assigned: 4, reducedItems: 2);
        var state = service.CreateState([1, 2, 3, 4]);
        state.CurrentDifficultyLevel = 3;

        state = service.RecordAnswer(state, false, [1, 1, 2, 2, 3, 4]);
        state = service.RecordAnswer(state, false, [1, 1, 2, 2, 3, 4]);
        state = service.RecordAnswer(state, false, [1, 1, 2, 2, 3, 4]);
        Assert.Equal(2, state.CurrentDifficultyLevel);

        state = service.RecordAnswer(state, true, [2, 3, 4]);
        state = service.RecordAnswer(state, true, [3, 4]);
        Assert.Equal(3, state.CurrentDifficultyLevel);

        state = service.RecordAnswer(state, true, [4]);
        Assert.Equal(4, state.CurrentDifficultyLevel);
        Assert.False(state.IsReturningToAssignedLevel);
    }

    [Fact]
    public void A_correct_answer_breaks_the_incorrect_streak_before_threshold()
    {
        var service = CreateService();
        var state = service.CreateState([1, 2, 3]);

        state = service.RecordAnswer(state, false, [1, 2, 3]);
        state = service.RecordAnswer(state, false, [1, 2, 3]);
        state = service.RecordAnswer(state, true, [1, 2, 3]);
        state = service.RecordAnswer(state, false, [1, 2, 3]);

        Assert.Equal(1, state.ConsecutiveIncorrectAnswers);
        Assert.Equal(3, state.CurrentDifficultyLevel);
        Assert.Equal(0, state.ReducedDifficultyItemsRemaining);
    }

    [Fact]
    public void Minimum_level_is_never_reduced_below_configured_boundary()
    {
        var service = CreateService(assigned: 2, minimum: 1);
        var state = service.CreateState([1, 2]);
        state.CurrentDifficultyLevel = 1;

        state = service.RecordAnswer(state, false, [1, 2]);
        state = service.RecordAnswer(state, false, [1, 2]);
        state = service.RecordAnswer(state, false, [1, 2]);

        Assert.Equal(1, state.CurrentDifficultyLevel);
        Assert.Contains("minimum configured practice level", state.AdaptationReason);
    }

    [Fact]
    public void Next_item_selection_is_deterministic_and_prefers_current_level()
    {
        var service = CreateService();
        var state = service.CreateState([1, 2, 3]);
        state.CurrentDifficultyLevel = 2;
        state.ReducedDifficultyItemsRemaining = 1;
        var items = new[]
        {
            new TestItem("first", 3),
            new TestItem("second", 2),
            new TestItem("third", 3)
        };

        var selected = service.SelectNextItem(items, ["first"], item => item.Id, item => item.Difficulty, state);

        Assert.Equal("second", selected?.Id);
    }

    private sealed record TestItem(string Id, int Difficulty);
}
