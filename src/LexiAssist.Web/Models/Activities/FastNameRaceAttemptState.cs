namespace LexiAssist.Web.Models.Activities;

/// <summary>
/// Snapshot of the configured item sequence and learner-reported activity metrics.
/// </summary>
public sealed class FastNameRaceAttemptState
{
    public string ContentVersion { get; set; } = string.Empty;

    public int DurationSeconds { get; set; }

    public List<string> Items { get; set; } = [];

    public int ItemsCompleted { get; set; }

    /// <summary>Zero-based positions in the randomized sequence that the learner marked for correction.</summary>
    public List<int> ErrorIndexes { get; set; } = [];

    public long ActiveDurationMilliseconds { get; set; }

    public double? ItemsPerMinute { get; set; }

    public double? Accuracy { get; set; }
}
