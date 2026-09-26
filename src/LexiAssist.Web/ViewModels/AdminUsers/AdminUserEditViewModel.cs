using System.ComponentModel.DataAnnotations;

namespace LexiAssist.Web.ViewModels.AdminUsers;

public sealed class AdminUserEditViewModel
{
    public required string Id { get; init; }

    [Required]
    [StringLength(100)]
    [Display(Name = "First name")]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    [Display(Name = "Last name")]
    public string LastName { get; set; } = string.Empty;

    [StringLength(100)]
    [Display(Name = "Preferred name (optional)")]
    public string? PreferredName { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Date of birth (optional)")]
    public DateOnly? DateOfBirth { get; set; }

    [Required]
    [EmailAddress]
    [StringLength(256)]
    [Display(Name = "Email address")]
    public string Email { get; set; } = string.Empty;
}
