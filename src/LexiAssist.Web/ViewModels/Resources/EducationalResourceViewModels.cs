using System.ComponentModel.DataAnnotations;

namespace LexiAssist.Web.ViewModels.Resources;

public sealed class EducationalResourceFormViewModel : IValidatableObject
{
    public int Id { get; set; }

    [Required, StringLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required, StringLength(1000)]
    [DataType(DataType.MultilineText)]
    public string Summary { get; set; } = string.Empty;

    [Required, StringLength(12000)]
    [DataType(DataType.MultilineText)]
    public string Content { get; set; } = string.Empty;

    [Required, StringLength(64)]
    [Display(Name = "Resource type")]
    public string ResourceType { get; set; } = string.Empty;

    [StringLength(2048)]
    [Display(Name = "External URL")]
    public string? ExternalUrl { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var (value, field, label) in new[]
                 {
                     (Title, nameof(Title), "Title"),
                     (Summary, nameof(Summary), "Summary"),
                     (Content, nameof(Content), "Content"),
                     (ResourceType, nameof(ResourceType), "Resource type")
                 })
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                yield return new ValidationResult($"{label} is required.", new[] { field });
            }
        }

        if (string.IsNullOrWhiteSpace(ExternalUrl))
        {
            yield break;
        }

        var candidate = ExternalUrl.Trim();
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            yield return new ValidationResult(
                "Enter a valid http or https URL.",
                new[] { nameof(ExternalUrl) });
        }
    }
}

public sealed record EducationalResourceListItemViewModel(
    int Id,
    string Title,
    string Summary,
    string ResourceType,
    bool IsPublished,
    DateTime UpdatedAtUtc);

public sealed record EducationalResourcesIndexViewModel(
    IReadOnlyList<EducationalResourceListItemViewModel> Resources,
    bool IsAdministrator)
{
    public string? Search { get; init; }
    public string? ResourceTypeFilter { get; init; }
    public string? StatusFilter { get; init; }
    public IReadOnlyList<string> ResourceTypes { get; init; } = [];
    public int CurrentPage { get; init; } = 1;
    public int PageSize { get; init; } = 5;
    public int TotalItems { get; init; }
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalItems / (double)PageSize));
    public bool HasPreviousPage => CurrentPage > 1;
    public bool HasNextPage => CurrentPage < TotalPages;
    public int FirstItemNumber => TotalItems == 0 ? 0 : ((CurrentPage - 1) * PageSize) + 1;
    public int LastItemNumber => TotalItems == 0 ? 0 : Math.Min(CurrentPage * PageSize, TotalItems);
}

public sealed record EducationalResourceDetailsViewModel(
    int Id,
    string Title,
    string Summary,
    string Content,
    string ResourceType,
    string? ExternalUrl,
    bool IsPublished,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
