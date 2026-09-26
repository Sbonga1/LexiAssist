using LexiAssist.Web.Models.Identity;

namespace LexiAssist.Web.ViewModels;

public sealed record LearnerDashboardViewModel(RecentScreeningResultViewModel? MostRecentScreeningResult)
{
    public TodayPracticeViewModel TodayPractice { get; init; } = TodayPracticeViewModel.Default;
}

public sealed record RecentScreeningResultViewModel(int AttemptId, DateTime CompletedAtUtc);

public sealed record TodayPracticeViewModel(
    int TargetMinutes,
    int CompletedMinutes,
    bool RemindersEnabled,
    TimeOnly PreferredReminderTime)
{
    public static TodayPracticeViewModel Default { get; } = new(
        LearnerReminderPreferenceOptions.DefaultDailyPracticeTargetMinutes,
        0,
        false,
        LearnerReminderPreferenceOptions.DefaultPreferredReminderTime);

    public bool TargetReached => CompletedMinutes >= TargetMinutes;

    public int RemainingMinutes => Math.Max(0, TargetMinutes - CompletedMinutes);
}
