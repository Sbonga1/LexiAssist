namespace LexiAssist.Web.Models.Identity;

/// <summary>
/// A learner-selected interest key used to tailor future practice themes.
/// The key is intentionally a string so new interests can be configured without
/// changing the database schema or adding enum values.
/// </summary>
public sealed class LearnerInterest
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public string InterestKey { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public ApplicationUser User { get; set; } = null!;
}

public sealed class LearnerInterestOptions
{
    public const string SectionName = "LearnerInterests";

    public List<LearnerInterestOption> Available { get; set; } = [];
}

public sealed class LearnerInterestOption
{
    public string Key { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;
}
