using LexiAssist.Web.Models.Identity;

namespace LexiAssist.Web.Services.Interfaces;

public interface IUserReminderPreferenceService
{
    Task<LearnerReminderPreference> GetForUserAsync(
        string userId,
        CancellationToken cancellationToken = default);

    Task SaveForUserAsync(
        LearnerReminderPreference preference,
        CancellationToken cancellationToken = default);
}
