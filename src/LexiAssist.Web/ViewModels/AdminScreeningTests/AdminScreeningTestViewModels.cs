using System.ComponentModel.DataAnnotations;

namespace LexiAssist.Web.ViewModels.AdminScreeningTests;

public sealed class AdminScreeningTestFormViewModel
{
    public int Id { get; set; }

    [Required]
    [StringLength(200)]
    [Display(Name = "Test name")]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(160)]
    [RegularExpression("^[A-Za-z0-9]+(?:-[A-Za-z0-9]+)*$", ErrorMessage = "Use letters, numbers, and single hyphens only.")]
    [Display(Name = "Slug")]
    public string Slug { get; set; } = string.Empty;

    [Required]
    [StringLength(2000)]
    [DataType(DataType.MultilineText)]
    public string Description { get; set; } = string.Empty;

    [Required]
    [StringLength(4000)]
    [DataType(DataType.MultilineText)]
    public string Instructions { get; set; } = string.Empty;
}

public sealed record AdminScreeningTestListItemViewModel(
    int Id,
    string Name,
    string Slug,
    int SortOrder,
    bool IsActive);

public sealed record AdminScreeningTestsIndexViewModel(
    IReadOnlyList<AdminScreeningTestListItemViewModel> Tests,
    string Search,
    int CurrentPage,
    int PageSize,
    int TotalItems,
    int TotalPages)
{
    public bool HasPreviousPage => CurrentPage > 1;

    public bool HasNextPage => CurrentPage < TotalPages;
};

public sealed record AdminScreeningTestDetailsViewModel(
    int Id,
    string Name,
    string Slug,
    string Description,
    string Instructions,
    int SortOrder,
    bool IsActive,
    int QuestionCount);
