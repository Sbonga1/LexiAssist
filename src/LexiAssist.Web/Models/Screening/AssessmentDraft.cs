namespace LexiAssist.Web.Models.Screening;

/// <summary>
/// Durable administrator-owned draft review state. Question content remains isolated from
/// published assessment questions until an explicit add operation succeeds.
/// </summary>
public sealed class AssessmentDraft
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public int ScreeningTestId { get; set; }

    public bool IsCsvImport { get; set; }

    public string ContentJson { get; set; } = "{}";

    public bool IsDiscarded { get; set; }

    public int Version { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public ScreeningTest ScreeningTest { get; set; } = null!;
}
