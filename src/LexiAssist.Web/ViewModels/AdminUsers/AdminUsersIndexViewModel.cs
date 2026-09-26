namespace LexiAssist.Web.ViewModels.AdminUsers;

public sealed class AdminUsersIndexViewModel
{
    public IReadOnlyList<AdminUserListItemViewModel> Users { get; init; } = [];

    public string? SearchTerm { get; init; }

    public int PageNumber { get; init; }

    public int TotalPages { get; init; }

    public int TotalCount { get; init; }

    public bool HasPreviousPage => PageNumber > 1;

    public bool HasNextPage => PageNumber < TotalPages;
}

public sealed record AdminUserListItemViewModel(
    string Id,
    string FirstName,
    string LastName,
    string? PreferredName,
    string Email,
    bool IsActive,
    string Roles);
