using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Activities;
using LexiAssist.Web.Models.Reports;
using LexiAssist.Web.Models.Screening;
using LexiAssist.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LexiAssist.Web.Services;

public sealed class LearningActivityService(
    ApplicationDbContext dbContext,
    IOptions<ActivityRecommendationOptions> configuredOptions,
    TimeProvider timeProvider) : ILearningActivityService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ActivityRecommendationOptions options = configuredOptions.Value;

    public async Task<IReadOnlyList<ActivityRecommendation>> GetRecommendationsAsync(
        int screeningReportId,
        CancellationToken cancellationToken = default)
    {
        var report = await dbContext.ScreeningReports
            .AsNoTracking()
            .Where(item => item.Id == screeningReportId &&
                           item.ScreeningAttempt.Status == ScreeningAttemptStatus.Completed &&
                           item.ScreeningAttempt.CompletedAtUtc != null)
            .Select(item => new { item.FlaggedAreasJson, item.ScreeningAttemptId })
            .SingleOrDefaultAsync(cancellationToken);
        if (report is null)
        {
            return [];
        }

        var indicators = ReadFlaggedAreaNames(report.FlaggedAreasJson)
            .Select(name => new RecommendationIndicator(name, name))
            .ToList();
        var taggedCategories = await dbContext.ScreeningResponses
            .AsNoTracking()
            .Where(response => response.ScreeningTestAttempt.ScreeningAttemptId == report.ScreeningAttemptId)
            .SelectMany(response => response.ErrorCategories)
            .Select(tag => tag.Category)
            .Distinct()
            .ToListAsync(cancellationToken);
        indicators.AddRange(taggedCategories.Select(category => new RecommendationIndicator(
            category.ToString(),
            GetDisplayName(category))));

        if (indicators.Count == 0 || options.Mappings.Count == 0)
        {
            return [];
        }

        var matchedActivities = new Dictionary<int, (LearningActivity Activity, HashSet<string> Indicators)>();
        var activities = await dbContext.LearningActivities
            .AsNoTracking()
            .Where(activity => activity.IsActive)
            .OrderBy(activity => activity.Name)
            .ToListAsync(cancellationToken);

        foreach (var mapping in options.Mappings)
        {
            var matchedIndicator = indicators.FirstOrDefault(indicator =>
                string.Equals(Normalize(indicator.Key), Normalize(mapping.Key), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Normalize(indicator.Label), Normalize(mapping.Key), StringComparison.OrdinalIgnoreCase));
            if (matchedIndicator is null || mapping.Value is null)
            {
                continue;
            }

            foreach (var activityName in mapping.Value.Where(name => !string.IsNullOrWhiteSpace(name)))
            {
                foreach (var activity in activities.Where(activity =>
                             string.Equals(Normalize(activity.Name), Normalize(activityName), StringComparison.OrdinalIgnoreCase)))
                {
                    if (!matchedActivities.TryGetValue(activity.Id, out var match))
                    {
                        match = (activity, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                        matchedActivities.Add(activity.Id, match);
                    }

                    match.Indicators.Add(matchedIndicator.Label);
                }
            }
        }

        return matchedActivities.Values
            .OrderBy(match => match.Activity.Name)
            .Select(match => new ActivityRecommendation(
                match.Activity.Id,
                match.Activity.Name,
                match.Activity.Description,
                match.Indicators.OrderBy(indicator => indicator, StringComparer.OrdinalIgnoreCase).ToArray()))
            .ToArray();
    }

    public async Task<ActivityAssignment> AssignAsync(
        int screeningReportId,
        int learningActivityId,
        string assignedByUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assignedByUserId);

        var report = await dbContext.ScreeningReports
            .AsNoTracking()
            .Where(item => item.Id == screeningReportId &&
                           item.ScreeningAttempt.Status == ScreeningAttemptStatus.Completed &&
                           item.ScreeningAttempt.CompletedAtUtc != null)
            .Select(item => new { item.Id, item.UserId })
            .SingleOrDefaultAsync(cancellationToken);
        if (report is null)
        {
            throw new ActivityAssignmentException("The completed screening report is unavailable for assignment.");
        }

        var activity = await dbContext.LearningActivities
            .SingleOrDefaultAsync(item => item.Id == learningActivityId && item.IsActive, cancellationToken);
        if (activity is null)
        {
            throw new ActivityAssignmentException("Choose an active learning activity.");
        }

        var duplicateExists = await dbContext.ActivityAssignments.AnyAsync(assignment =>
            assignment.RelatedScreeningReportId == screeningReportId &&
            assignment.LearnerId == report.UserId &&
            assignment.LearningActivityId == learningActivityId &&
            (assignment.Status == ActivityAssignmentStatus.Assigned ||
             assignment.Status == ActivityAssignmentStatus.InProgress),
            cancellationToken);
        if (duplicateExists)
        {
            throw new ActivityAssignmentException("This activity is already assigned for this screening report.");
        }

        var assignment = new ActivityAssignment
        {
            LearnerId = report.UserId,
            LearningActivityId = learningActivityId,
            AssignedByUserId = assignedByUserId,
            RelatedScreeningReportId = report.Id,
            AssignedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
            Status = ActivityAssignmentStatus.Assigned
        };

        dbContext.ActivityAssignments.Add(assignment);
        await dbContext.SaveChangesAsync(cancellationToken);
        return assignment;
    }

    private static IReadOnlyList<string> ReadFlaggedAreaNames(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<ScreeningReportFlaggedAreaSnapshot>>(json, JsonOptions)?
                .Select(area => area.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string Normalize(string? value) =>
        string.Join(' ', (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string GetDisplayName(ScreeningErrorCategory category)
    {
        var member = typeof(ScreeningErrorCategory).GetMember(category.ToString()).FirstOrDefault();
        return member?.GetCustomAttribute<DisplayAttribute>()?.GetName() ?? category.ToString();
    }

    private sealed record RecommendationIndicator(string Key, string Label);
}
