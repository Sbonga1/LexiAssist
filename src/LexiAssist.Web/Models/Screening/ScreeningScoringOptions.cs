namespace LexiAssist.Web.Models.Screening;

/// <summary>
/// Transparent, non-clinical rules used to calculate answer-key scores.
/// </summary>
public sealed class ScreeningScoringOptions
{
    /// <summary>
    /// Bump when scoring rules or their interpretation change. The value is copied
    /// into each generated report for historical reference.
    /// </summary>
    public string ScoringVersion { get; set; } = "1.0";

    /// <summary>
    /// Only these question types are eligible for automatic answer-key matching.
    /// Timing and pace activities are intentionally excluded by default.
    /// </summary>
    public HashSet<ScreeningQuestionType> ObjectiveQuestionTypes { get; set; } =
    [
        ScreeningQuestionType.MultipleChoice,
        ScreeningQuestionType.TextResponse,
        ScreeningQuestionType.WordReading,
        ScreeningQuestionType.NonWordReading,
        ScreeningQuestionType.PhonologicalChoice,
        ScreeningQuestionType.Spelling,
        ScreeningQuestionType.ReadingPassage,
        ScreeningQuestionType.ListeningResponse,
        ScreeningQuestionType.WorkingMemorySequence,
        ScreeningQuestionType.TimedMatching,
        ScreeningQuestionType.PatternReasoning
    ];

    /// <summary>
    /// Case-insensitive matching is easier to use for typed answers and does not
    /// change spelling or punctuation. Set false when case must match exactly.
    /// </summary>
    public bool IgnoreAnswerCase { get; set; } = true;

    /// <summary>
    /// Whitespace around an answer is ignored by default.
    /// </summary>
    public bool TrimAnswerWhitespace { get; set; } = true;

    /// <summary>
    /// A category below this percentage is included as a review cue. This is not
    /// a clinical threshold. Null disables category flags. The default of 100
    /// flags a category only when at least one scorable item was missed.
    /// </summary>
    public decimal? FlagSkillAreaBelowPercentage { get; set; } = 100m;
}
