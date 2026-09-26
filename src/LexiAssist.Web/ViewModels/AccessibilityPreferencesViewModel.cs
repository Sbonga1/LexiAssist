using System.ComponentModel.DataAnnotations;
using LexiAssist.Web.Models.Identity;

namespace LexiAssist.Web.ViewModels;

public sealed class AccessibilityPreferencesViewModel
{
    public IReadOnlyList<LearnerInterestOption> AvailableInterests { get; set; } = [];

    public List<string> SelectedInterests { get; set; } = [];

    [Display(Name = "Practice reminders")]
    public bool RemindersEnabled { get; set; }

    [Required]
    [DataType(DataType.Time)]
    [Display(Name = "Preferred reminder time")]
    public TimeOnly PreferredReminderTime { get; set; } = LearnerReminderPreferenceOptions.DefaultPreferredReminderTime;

    [Range(
        LearnerReminderPreferenceOptions.MinimumDailyPracticeTargetMinutes,
        LearnerReminderPreferenceOptions.MaximumDailyPracticeTargetMinutes,
        ErrorMessage = "Choose a daily practice target between 1 and 120 minutes.")]
    [Display(Name = "Daily practice target")]
    public int DailyPracticeTargetMinutes { get; set; } = LearnerReminderPreferenceOptions.DefaultDailyPracticeTargetMinutes;

    [Required]
    [RegularExpression("^(standard-light|warm-cream|soft-blue|muted-gray)$", ErrorMessage = "Choose one of the available reading backgrounds.")]
    [Display(Name = "Reading background")]
    public string BackgroundTheme { get; set; } = AccessibilityPreferenceOptions.StandardLight;

    [Range(0.9, 1.3, ErrorMessage = "Font size must be between 90% and 130%.")]
    [Display(Name = "Font size")]
    public double FontScale { get; set; } = AccessibilityPreferenceOptions.DefaultFontScale;

    [Display(Name = "Increase line spacing")]
    public bool IncreasedLineSpacing { get; set; }

    [Display(Name = "Reduce motion")]
    public bool ReduceMotion { get; set; }

    public static AccessibilityPreferencesViewModel FromPreference(UserAccessibilityPreference preference) => new()
    {
        BackgroundTheme = preference.BackgroundTheme,
        FontScale = preference.FontScale,
        IncreasedLineSpacing = preference.IncreasedLineSpacing,
        ReduceMotion = preference.ReduceMotion
    };

    public static AccessibilityPreferencesViewModel FromPreference(
        UserAccessibilityPreference preference,
        IEnumerable<LearnerInterestOption> availableInterests,
        IEnumerable<string> selectedInterests) => new()
    {
        BackgroundTheme = preference.BackgroundTheme,
        FontScale = preference.FontScale,
        IncreasedLineSpacing = preference.IncreasedLineSpacing,
        ReduceMotion = preference.ReduceMotion,
        AvailableInterests = availableInterests.ToList(),
        SelectedInterests = selectedInterests.ToList()
    };

    public static AccessibilityPreferencesViewModel FromPreference(
        UserAccessibilityPreference preference,
        IEnumerable<LearnerInterestOption> availableInterests,
        IEnumerable<string> selectedInterests,
        LearnerReminderPreference reminderPreference) => new()
    {
        BackgroundTheme = preference.BackgroundTheme,
        FontScale = preference.FontScale,
        IncreasedLineSpacing = preference.IncreasedLineSpacing,
        ReduceMotion = preference.ReduceMotion,
        AvailableInterests = availableInterests.ToList(),
        SelectedInterests = selectedInterests.ToList(),
        RemindersEnabled = reminderPreference.RemindersEnabled,
        PreferredReminderTime = reminderPreference.PreferredReminderTime,
        DailyPracticeTargetMinutes = reminderPreference.DailyPracticeTargetMinutes
    };

    public UserAccessibilityPreference ToPreference(string userId) => new()
    {
        UserId = userId,
        BackgroundTheme = BackgroundTheme,
        FontScale = FontScale,
        IncreasedLineSpacing = IncreasedLineSpacing,
        ReduceMotion = ReduceMotion
    };

    public LearnerReminderPreference ToReminderPreference(string userId) => new()
    {
        UserId = userId,
        RemindersEnabled = RemindersEnabled,
        PreferredReminderTime = PreferredReminderTime,
        DailyPracticeTargetMinutes = DailyPracticeTargetMinutes
    };
}
