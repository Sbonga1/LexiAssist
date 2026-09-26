using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Identity;
using LexiAssist.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LexiAssist.Web.Services;

public sealed class UserInterestService(
    ApplicationDbContext dbContext,
    IOptions<LearnerInterestOptions> configuredOptions) : IUserInterestService
{
    private readonly LearnerInterestOptions options = configuredOptions.Value;

    public async Task<IReadOnlyList<string>> GetSelectedKeysAsync(
        string userId,
        CancellationToken cancellationToken = default) =>
        await dbContext.LearnerInterests
            .AsNoTracking()
            .Where(selection => selection.UserId == userId)
            .OrderBy(selection => selection.InterestKey)
            .Select(selection => selection.InterestKey)
            .ToListAsync(cancellationToken);

    public async Task SaveSelectedKeysAsync(
        string userId,
        IEnumerable<string> interestKeys,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(interestKeys);

        var availableKeys = options.Available
            .Select(option => NormalizeKey(option.Key))
            .Where(key => key.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        var selectedKeys = interestKeys
            .Select(NormalizeKey)
            .Where(key => key.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        if (selectedKeys.Any(key => !availableKeys.Contains(key)))
        {
            throw new ArgumentException("One or more selected interests are not available.", nameof(interestKeys));
        }

        var existing = await dbContext.LearnerInterests
            .Where(selection => selection.UserId == userId)
            .ToListAsync(cancellationToken);
        var existingKeys = existing
            .Select(selection => NormalizeKey(selection.InterestKey))
            .ToHashSet(StringComparer.Ordinal);

        dbContext.LearnerInterests.RemoveRange(
            existing.Where(selection => !selectedKeys.Contains(NormalizeKey(selection.InterestKey))));

        var now = DateTime.UtcNow;
        foreach (var key in selectedKeys.Where(key => !existingKeys.Contains(key)))
        {
            dbContext.LearnerInterests.Add(new LearnerInterest
            {
                UserId = userId,
                InterestKey = key,
                CreatedAtUtc = now
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string NormalizeKey(string? key) =>
        string.IsNullOrWhiteSpace(key) ? string.Empty : key.Trim().ToLowerInvariant();
}
