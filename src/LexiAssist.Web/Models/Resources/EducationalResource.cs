namespace LexiAssist.Web.Models.Resources;

/// <summary>
/// An educational or support resource that can be published for learners and professionals.
/// </summary>
public sealed class EducationalResource
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public string ResourceType { get; set; } = string.Empty;

    public string? ExternalUrl { get; set; }

    public bool IsPublished { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
