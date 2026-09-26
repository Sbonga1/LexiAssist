namespace LexiAssist.Web.Models.Activities;

/// <summary>Deterministic practice adaptation state saved with an activity attempt.</summary>
public sealed class AdaptiveSupportState
{
    public int AssignedDifficultyLevel { get; set; } = 3;

    public int CurrentDifficultyLevel { get; set; } = 3;

    public int ConsecutiveIncorrectAnswers { get; set; }

    public int ReducedDifficultyItemsRemaining { get; set; }

    public bool IsReturningToAssignedLevel { get; set; }

    public string AdaptationReason { get; set; } = "Using the assigned practice level.";
}

public sealed class AdaptiveSupportOptions
{
    public const string SectionName = "AdaptiveSupport";

    public int AssignedDifficultyLevel { get; set; } = 3;

    public int MinimumDifficultyLevel { get; set; } = 1;

    public int MaximumDifficultyLevel { get; set; } = 5;

    public int IncorrectAnswersBeforeAdaptation { get; set; } = 3;

    public int ReducedDifficultyItemCount { get; set; } = 2;
}
