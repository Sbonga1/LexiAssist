using System.ComponentModel.DataAnnotations;

namespace LexiAssist.Web.ViewModels.AdminUsers;

public sealed class AdminUserCreateViewModel
{
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

    [Required]
    [EnumDataType(typeof(ManagedUserRole))]
    [Display(Name = "Account type")]
    public ManagedUserRole? Role { get; set; }

    [Required]
    [StringLength(256)]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Required]
    [StringLength(256)]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "The password and confirmation do not match.")]
    [Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
