namespace LexiAssist.Web.Models.Activities;

/// <summary>
/// A generated practice story snapshot. The saved inputs and method version
/// make the story reproducible without relying on an external generator.
/// </summary>
public sealed class PracticeStory
{
    public int Id { get; set; }

    public string LearnerId { get; set; } = string.Empty;

    public string Theme { get; set; } = string.Empty;

    public string TargetPhoneticPattern { get; set; } = string.Empty;

    public string TargetWordsJson { get; set; } = "[]";

    public int DifficultyLevel { get; set; }

    public string StoryText { get; set; } = string.Empty;

    public string GenerationMethod { get; set; } = string.Empty;

    public DateTime GeneratedAtUtc { get; set; }

    public Models.Identity.ApplicationUser Learner { get; set; } = null!;
}
