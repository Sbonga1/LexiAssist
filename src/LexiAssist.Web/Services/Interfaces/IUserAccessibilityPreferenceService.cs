using LexiAssist.Web.Models.Identity;

namespace LexiAssist.Web.Services.Interfaces;

public interface IUserAccessibilityPreferenceService
{
    Task<UserAccessibilityPreference> GetForUserAsync(string userId, CancellationToken cancellationToken = default);

    Task SaveForUserAsync(UserAccessibilityPreference preference, CancellationToken cancellationToken = default);
}
