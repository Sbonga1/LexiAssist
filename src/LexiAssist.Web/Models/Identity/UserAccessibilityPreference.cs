namespace LexiAssist.Web.Models.Identity;

public class UserAccessibilityPreference
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public string BackgroundTheme { get; set; } = AccessibilityPreferenceOptions.StandardLight;

    public double FontScale { get; set; } = AccessibilityPreferenceOptions.DefaultFontScale;

    public bool IncreasedLineSpacing { get; set; }

    public bool ReduceMotion { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}

public static class AccessibilityPreferenceOptions
{
    public const string StandardLight = "standard-light";
    public const string WarmCream = "warm-cream";
    public const string SoftBlue = "soft-blue";
    public const string MutedGray = "muted-gray";
    public const double DefaultFontScale = 1.0;

    public static bool IsBackgroundThemeSupported(string? theme) => theme is
        StandardLight or WarmCream or SoftBlue or MutedGray;

    public static string NormalizeBackgroundTheme(string? theme) =>
        IsBackgroundThemeSupported(theme) ? theme! : StandardLight;

    public static double NormalizeFontScale(double scale) =>
        double.IsFinite(scale) ? Math.Clamp(scale, 0.9, 1.3) : DefaultFontScale;
}
