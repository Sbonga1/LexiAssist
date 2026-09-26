using LexiAssist.Web.Models.Activities;
using Microsoft.Extensions.Options;

namespace LexiAssist.Web.Services;

/// <summary>Applies the configured, deterministic support rule to practice-answer state.</summary>
public sealed class AdaptiveSupportService(IOptions<AdaptiveSupportOptions> configuredOptions)
{
    private readonly AdaptiveSupportOptions options = configuredOptions.Value;

    public AdaptiveSupportState CreateState(IEnumerable<int> availableDifficultyLevels)
    {
        var minimum = MinimumDifficultyLevel;
        var maximum = MaximumDifficultyLevel;
        var assignedLevel = Math.Clamp(options.AssignedDifficultyLevel, minimum, maximum);
        var availableLevels = availableDifficultyLevels
            .Where(level => level >= minimum && level <= maximum)
            .Distinct()
            .ToArray();
        var currentLevel = availableLevels.Length == 0 || availableLevels.Contains(assignedLevel)
            ? assignedLevel
            : availableLevels.OrderBy(level => Math.Abs(level - assignedLevel)).ThenBy(level => level).First();

        return new AdaptiveSupportState
        {
            AssignedDifficultyLevel = assignedLevel,
            CurrentDifficultyLevel = currentLevel,
            AdaptationReason = currentLevel == assignedLevel
                ? "Using the assigned practice level."
                : $"The assigned level is not available in this activity content; using practice level {currentLevel}."
        };
    }

    /// <param name="availableDifficultyLevels">Levels of items not yet presented after the submitted answer.</param>
    public AdaptiveSupportState RecordAnswer(
        AdaptiveSupportState current,
        bool? isCorrect,
        IReadOnlyCollection<int> availableDifficultyLevels)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(availableDifficultyLevels);

        var state = new AdaptiveSupportState
        {
            AssignedDifficultyLevel = Math.Clamp(current.AssignedDifficultyLevel, MinimumDifficultyLevel, MaximumDifficultyLevel),
            CurrentDifficultyLevel = Math.Clamp(current.CurrentDifficultyLevel, MinimumDifficultyLevel, MaximumDifficultyLevel),
            ConsecutiveIncorrectAnswers = Math.Max(0, current.ConsecutiveIncorrectAnswers),
            ReducedDifficultyItemsRemaining = Math.Max(0, current.ReducedDifficultyItemsRemaining),
            IsReturningToAssignedLevel = current.IsReturningToAssignedLevel,
            AdaptationReason = current.AdaptationReason ?? string.Empty
        };

        state.ConsecutiveIncorrectAnswers = isCorrect == false
            ? state.ConsecutiveIncorrectAnswers + 1
            : 0;

        var nextLevels = availableDifficultyLevels
            .Where(level => level >= MinimumDifficultyLevel && level <= MaximumDifficultyLevel)
            .ToArray();

        if (isCorrect == false && state.ConsecutiveIncorrectAnswers >= IncorrectAnswerThreshold)
        {
            state.ConsecutiveIncorrectAnswers = 0;
            if (state.CurrentDifficultyLevel <= MinimumDifficultyLevel)
            {
                state.AdaptationReason = $"{IncorrectAnswerThreshold} consecutive incorrect answers; the minimum configured practice level is already in use.";
                return state;
            }

            var nextLowerLevel = nextLevels
                .Where(level => level < state.CurrentDifficultyLevel)
                .GroupBy(level => level)
                .Where(group => group.Count() >= ReducedDifficultyItemCount)
                .Select(group => (int?)group.Key)
                .OrderByDescending(level => level)
                .FirstOrDefault();

            if (nextLowerLevel is null)
            {
                state.AdaptationReason = $"{IncorrectAnswerThreshold} consecutive incorrect answers; no lower practice level has {ReducedDifficultyItemCount} remaining items in this activity.";
                return state;
            }

            state.CurrentDifficultyLevel = nextLowerLevel.Value;
            state.ReducedDifficultyItemsRemaining = ReducedDifficultyItemCount;
            state.IsReturningToAssignedLevel = false;
            state.AdaptationReason = $"{IncorrectAnswerThreshold} consecutive incorrect answers; the next {ReducedDifficultyItemCount} items use practice level {state.CurrentDifficultyLevel}, then the level moves gradually toward the assigned level.";
            return state;
        }

        if (state.ReducedDifficultyItemsRemaining > 0)
        {
            state.ReducedDifficultyItemsRemaining--;
            if (state.ReducedDifficultyItemsRemaining == 0)
            {
                state.IsReturningToAssignedLevel = state.CurrentDifficultyLevel < state.AssignedDifficultyLevel;
                MoveOneLevelTowardAssigned(state);
            }

            return state;
        }

        if (state.IsReturningToAssignedLevel)
        {
            MoveOneLevelTowardAssigned(state);
        }

        return state;
    }

    public TItem? SelectNextItem<TItem>(
        IEnumerable<TItem> items,
        IEnumerable<string> completedItemIds,
        Func<TItem, string> getItemId,
        Func<TItem, int> getDifficultyLevel,
        AdaptiveSupportState state)
        where TItem : class
    {
        var completed = completedItemIds.ToHashSet(StringComparer.Ordinal);
        var remaining = items
            .Select((item, index) => new { Item = item, Index = index, Id = getItemId(item), Level = getDifficultyLevel(item) })
            .Where(item => !completed.Contains(item.Id))
            .ToArray();
        if (state.CurrentDifficultyLevel == state.AssignedDifficultyLevel && state.ReducedDifficultyItemsRemaining == 0)
        {
            return remaining.Length == 0 ? null : remaining[0].Item;
        }

        return remaining
            .OrderBy(item => item.Level == state.CurrentDifficultyLevel ? 0 : 1)
            .ThenBy(item => Math.Abs(item.Level - state.CurrentDifficultyLevel))
            .ThenBy(item => item.Index)
            .Select(item => item.Item)
            .FirstOrDefault();
    }

    public int MaximumDifficultyLevel => MaximumDifficultyLevelValue;

    public string Describe(AdaptiveSupportState state) =>
        $"Adaptive Support: current practice level {state.CurrentDifficultyLevel} of {MaximumDifficultyLevel}. {state.AdaptationReason}";

    private int IncorrectAnswerThreshold => Math.Clamp(options.IncorrectAnswersBeforeAdaptation, 1, 20);

    private int ReducedDifficultyItemCount => Math.Clamp(options.ReducedDifficultyItemCount, 1, 10);

    private int MinimumDifficultyLevel => Math.Clamp(options.MinimumDifficultyLevel, 1, 5);

    private int MaximumDifficultyLevelValue => Math.Clamp(options.MaximumDifficultyLevel, MinimumDifficultyLevel, 5);

    private void MoveOneLevelTowardAssigned(AdaptiveSupportState state)
    {
        var nextLevel = Math.Min(state.AssignedDifficultyLevel, state.CurrentDifficultyLevel + 1);
        if (nextLevel == state.CurrentDifficultyLevel)
        {
            state.IsReturningToAssignedLevel = false;
            state.AdaptationReason = "The lower-level support period is complete; the current level will move up when the next configured level is available.";
            return;
        }

        state.CurrentDifficultyLevel = nextLevel;
        state.IsReturningToAssignedLevel = state.CurrentDifficultyLevel < state.AssignedDifficultyLevel;
        state.AdaptationReason = state.CurrentDifficultyLevel == state.AssignedDifficultyLevel
            ? "The support period is complete; returned to the assigned practice level."
            : $"The support period is complete; moved one level toward the assigned level ({state.CurrentDifficultyLevel} of {state.AssignedDifficultyLevel}).";
    }
}
