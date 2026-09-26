namespace LexiAssist.Web.Models.Identity;

public sealed class LearnerReminderPreference
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public bool RemindersEnabled { get; set; }

    public TimeOnly PreferredReminderTime { get; set; } = LearnerReminderPreferenceOptions.DefaultPreferredReminderTime;

    public int DailyPracticeTargetMinutes { get; set; } = LearnerReminderPreferenceOptions.DefaultDailyPracticeTargetMinutes;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public ApplicationUser User { get; set; } = null!;
}

public static class LearnerReminderPreferenceOptions
{
    public const int DefaultDailyPracticeTargetMinutes = 5;
    public const int MinimumDailyPracticeTargetMinutes = 1;
    public const int MaximumDailyPracticeTargetMinutes = 120;
    public static readonly TimeOnly DefaultPreferredReminderTime = new(17, 0);

    public static int NormalizeDailyPracticeTargetMinutes(int minutes) =>
        minutes is >= MinimumDailyPracticeTargetMinutes and <= MaximumDailyPracticeTargetMinutes
            ? minutes
            : DefaultDailyPracticeTargetMinutes;
}
