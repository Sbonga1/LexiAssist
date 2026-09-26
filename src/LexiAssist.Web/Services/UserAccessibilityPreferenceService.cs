using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Identity;
using LexiAssist.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace LexiAssist.Web.Services;

public sealed class UserAccessibilityPreferenceService(ApplicationDbContext dbContext) : IUserAccessibilityPreferenceService
{
    public async Task<UserAccessibilityPreference> GetForUserAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var preference = await dbContext.UserAccessibilityPreferences
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);

        if (preference is null)
        {
            return new UserAccessibilityPreference { UserId = userId };
        }

        preference.BackgroundTheme = AccessibilityPreferenceOptions.NormalizeBackgroundTheme(preference.BackgroundTheme);
        preference.FontScale = AccessibilityPreferenceOptions.NormalizeFontScale(preference.FontScale);
        return preference;
    }

    public async Task SaveForUserAsync(
        UserAccessibilityPreference preference,
        CancellationToken cancellationToken = default)
    {
        var storedPreference = await dbContext.UserAccessibilityPreferences
            .SingleOrDefaultAsync(item => item.UserId == preference.UserId, cancellationToken);
        var now = DateTime.UtcNow;

        if (storedPreference is null)
        {
            storedPreference = new UserAccessibilityPreference
            {
                UserId = preference.UserId,
                CreatedAtUtc = now
            };
            dbContext.UserAccessibilityPreferences.Add(storedPreference);
        }

        storedPreference.BackgroundTheme = AccessibilityPreferenceOptions.NormalizeBackgroundTheme(preference.BackgroundTheme);
        storedPreference.FontScale = AccessibilityPreferenceOptions.NormalizeFontScale(preference.FontScale);
        storedPreference.IncreasedLineSpacing = preference.IncreasedLineSpacing;
        storedPreference.ReduceMotion = preference.ReduceMotion;
        storedPreference.UpdatedAtUtc = now;

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
