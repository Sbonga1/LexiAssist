using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Identity;
using LexiAssist.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace LexiAssist.Web.Services;

public sealed class UserReminderPreferenceService(ApplicationDbContext dbContext) : IUserReminderPreferenceService
{
    public async Task<LearnerReminderPreference> GetForUserAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var preference = await dbContext.LearnerReminderPreferences
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);

        if (preference is null)
        {
            return new LearnerReminderPreference { UserId = userId };
        }

        preference.DailyPracticeTargetMinutes = LearnerReminderPreferenceOptions.NormalizeDailyPracticeTargetMinutes(
            preference.DailyPracticeTargetMinutes);
        return preference;
    }

    public async Task SaveForUserAsync(
        LearnerReminderPreference preference,
        CancellationToken cancellationToken = default)
    {
        var storedPreference = await dbContext.LearnerReminderPreferences
            .SingleOrDefaultAsync(item => item.UserId == preference.UserId, cancellationToken);
        var now = DateTime.UtcNow;

        if (storedPreference is null)
        {
            storedPreference = new LearnerReminderPreference
            {
                UserId = preference.UserId,
                CreatedAtUtc = now
            };
            dbContext.LearnerReminderPreferences.Add(storedPreference);
        }

        storedPreference.RemindersEnabled = preference.RemindersEnabled;
        storedPreference.PreferredReminderTime = preference.PreferredReminderTime;
        storedPreference.DailyPracticeTargetMinutes = LearnerReminderPreferenceOptions.NormalizeDailyPracticeTargetMinutes(
            preference.DailyPracticeTargetMinutes);
        storedPreference.UpdatedAtUtc = now;

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
