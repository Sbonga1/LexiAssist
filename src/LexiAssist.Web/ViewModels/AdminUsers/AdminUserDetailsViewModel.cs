namespace LexiAssist.Web.ViewModels.AdminUsers;

public sealed class AdminUserDetailsViewModel
{
    public required string Id { get; init; }

    public required string FirstName { get; init; }

    public required string LastName { get; init; }

    public string? PreferredName { get; init; }

    public DateOnly? DateOfBirth { get; init; }

    public required string Email { get; init; }

    public bool IsActive { get; init; }

    public DateTime CreatedAtUtc { get; init; }

    public DateTime? ScreeningPrivacyNoticeAcceptedAtUtc { get; init; }

    public IReadOnlyList<string> Roles { get; init; } = [];

    public bool IsCurrentUser { get; init; }

    public int CurrentPosition { get; init; }

    public int TotalUsers { get; init; }

    public string? PreviousUserId { get; init; }

    public string? NextUserId { get; init; }
}
