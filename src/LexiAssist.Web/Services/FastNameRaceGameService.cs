using System.Security.Cryptography;
using System.Text.Json;
using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Activities;
using LexiAssist.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LexiAssist.Web.Services;

public sealed class FastNameRaceGameService(
    ApplicationDbContext dbContext,
    IOptions<FastNameRaceOptions> configuredOptions,
    TimeProvider timeProvider,
    ISupportiveFeedbackService supportiveFeedbackService) : IFastNameRaceGameService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly FastNameRaceOptions options = configuredOptions.Value;

    public async Task<FastNameRaceSession?> GetSessionAsync(
        string learnerId,
        int assignmentId,
        CancellationToken cancellationToken = default)
    {
        var assignment = await GetOwnedAssignmentAsync(learnerId, assignmentId, tracked: false, cancellationToken);
        if (assignment is null)
        {
            return null;
        }

        var attempt = await dbContext.ActivityAttempts
            .AsNoTracking()
            .Where(item => item.ActivityAssignmentId == assignmentId)
            .OrderByDescending(item => item.StartedAtUtc)
            .ThenByDescending(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (attempt is null)
        {
            var contentAvailable = IsValidOptions();
            return new FastNameRaceSession(
                assignment.Id,
                assignment.LearningActivity.Name,
                assignment.Status != ActivityAssignmentStatus.Completed && contentAvailable,
                assignment.Status == ActivityAssignmentStatus.Completed,
                null,
                contentAvailable ? options.DurationSeconds : 60,
                [],
                0,
                0,
                null,
                null,
                null,
                contentAvailable ? null : "Fast Name Race content is not configured correctly yet. Please ask your support professional for help.");
        }

        var state = ReadState(attempt.MetadataJson);
        return state is null
            ? new FastNameRaceSession(
                assignment.Id,
                assignment.LearningActivity.Name,
                false,
                attempt.CompletedAtUtc.HasValue,
                attempt.Id,
                0,
                [],
                0,
                0,
                attempt.DurationMilliseconds,
                null,
                attempt.Accuracy,
                "Saved activity details could not be read. This attempt remains in your progress history.")
            : BuildSession(assignment, attempt, state);
    }

    public async Task<FastNameRaceSession?> StartAsync(
        string learnerId,
        int assignmentId,
        CancellationToken cancellationToken = default)
    {
        var assignment = await GetOwnedAssignmentAsync(learnerId, assignmentId, tracked: true, cancellationToken);
        if (assignment is null)
        {
            return null;
        }

        if (assignment.Status == ActivityAssignmentStatus.Completed)
        {
            return await GetSessionAsync(learnerId, assignmentId, cancellationToken);
        }

        var activeAttempt = await dbContext.ActivityAttempts
            .Where(item => item.ActivityAssignmentId == assignmentId && item.CompletedAtUtc == null)
            .OrderByDescending(item => item.StartedAtUtc)
            .ThenByDescending(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (activeAttempt is not null)
        {
            var activeState = ReadState(activeAttempt.MetadataJson);
            return activeState is null ? null : BuildSession(assignment, activeAttempt, activeState);
        }

        if (!TryCreateState(out var state))
        {
            return new FastNameRaceSession(
                assignment.Id,
                assignment.LearningActivity.Name,
                false,
                false,
                null,
                IsValidOptions() ? options.DurationSeconds : 60,
                [],
                0,
                0,
                null,
                null,
                null,
                "Fast Name Race content is not configured correctly yet. Please ask your support professional for help.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var attempt = new ActivityAttempt
        {
            ActivityAssignmentId = assignment.Id,
            StartedAtUtc = now,
            MetadataJson = JsonSerializer.Serialize(state, JsonOptions)
        };
        assignment.Status = ActivityAssignmentStatus.InProgress;
        dbContext.ActivityAttempts.Add(attempt);
        await dbContext.SaveChangesAsync(cancellationToken);

        return BuildSession(assignment, attempt, state);
    }

    public async Task<FastNameRaceSession?> CompleteAsync(
        string learnerId,
        int assignmentId,
        int attemptId,
        int itemsCompleted,
        string? errorIndexesJson,
        long activeDurationMilliseconds,
        CancellationToken cancellationToken = default)
    {
        var assignment = await GetOwnedAssignmentAsync(learnerId, assignmentId, tracked: true, cancellationToken);
        if (assignment is null)
        {
            return null;
        }

        var attempt = await dbContext.ActivityAttempts.SingleOrDefaultAsync(
            item => item.Id == attemptId && item.ActivityAssignmentId == assignmentId,
            cancellationToken);
        if (attempt is null)
        {
            return null;
        }

        var state = ReadState(attempt.MetadataJson);
        if (state is null)
        {
            return null;
        }

        // A repeated POST returns the saved result without changing or duplicating it.
        if (attempt.CompletedAtUtc is not null)
        {
            return BuildSession(assignment, attempt, state);
        }

        var errorIndexes = ParseErrorIndexes(errorIndexesJson);
        if (itemsCompleted < 0 || itemsCompleted > state.Items.Count)
        {
            throw new FastNameRaceSubmissionException("The completed item count is outside this activity grid.");
        }

        if (errorIndexes.Any(index => index < 0 || index >= itemsCompleted) ||
            errorIndexes.Distinct().Count() != errorIndexes.Count)
        {
            throw new FastNameRaceSubmissionException("Marked items must be unique positions among the completed items.");
        }

        var maxDuration = checked((long)state.DurationSeconds * 1000L);
        if (activeDurationMilliseconds < 0 || activeDurationMilliseconds > maxDuration)
        {
            throw new FastNameRaceSubmissionException("The active practice time is outside the configured activity duration.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var maximumPossibleElapsed = Math.Max(0L, (long)(now - attempt.StartedAtUtc).TotalMilliseconds) + 5000L;
        if (activeDurationMilliseconds > maximumPossibleElapsed)
        {
            throw new FastNameRaceSubmissionException("The active practice time is longer than the elapsed session time.");
        }

        state.ItemsCompleted = itemsCompleted;
        state.ErrorIndexes = errorIndexes.Order().ToList();
        state.ActiveDurationMilliseconds = activeDurationMilliseconds;
        state.ItemsPerMinute = activeDurationMilliseconds == 0
            ? null
            : Math.Round(itemsCompleted * 60000d / activeDurationMilliseconds, 2, MidpointRounding.AwayFromZero);
        state.Accuracy = itemsCompleted == 0
            ? null
            : Math.Round((itemsCompleted - errorIndexes.Count) * 100d / itemsCompleted, 2, MidpointRounding.AwayFromZero);

        var serialized = JsonSerializer.Serialize(state, JsonOptions);
        if (serialized.Length > 16000)
        {
            throw new FastNameRaceSubmissionException("This configured activity is too large to save. Please ask your support professional to review its content.");
        }

        attempt.Score = itemsCompleted;
        attempt.Accuracy = state.Accuracy;
        attempt.DurationMilliseconds = activeDurationMilliseconds;
        attempt.MetadataJson = serialized;
        attempt.CompletedAtUtc = now;
        assignment.Status = ActivityAssignmentStatus.Completed;
        assignment.CompletedAtUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        return BuildSession(assignment, attempt, state);
    }

    private async Task<ActivityAssignment?> GetOwnedAssignmentAsync(
        string learnerId,
        int assignmentId,
        bool tracked,
        CancellationToken cancellationToken)
    {
        var query = dbContext.ActivityAssignments.AsQueryable();
        if (!tracked)
        {
            query = query.AsNoTracking();
        }

        return await query
            .Include(assignment => assignment.LearningActivity)
            .SingleOrDefaultAsync(assignment =>
                assignment.Id == assignmentId &&
                assignment.LearnerId == learnerId &&
                assignment.Status != ActivityAssignmentStatus.Cancelled &&
                assignment.LearningActivity.IsActive &&
                assignment.LearningActivity.Name == "Fast Name Race" &&
                assignment.LearningActivity.ActivityType == LearningActivityType.Fluency,
                cancellationToken);
    }

    private bool TryCreateState(out FastNameRaceAttemptState state)
    {
        state = new FastNameRaceAttemptState();
        if (!IsValidOptions())
        {
            return false;
        }

        var items = Shuffle(options.Items.Select(item => item.Trim()).ToList());
        state = new FastNameRaceAttemptState
        {
            ContentVersion = options.ContentVersion.Trim(),
            DurationSeconds = options.DurationSeconds,
            Items = items
        };
        if (JsonSerializer.Serialize(state, JsonOptions).Length <= 16000)
        {
            return true;
        }

        state = new FastNameRaceAttemptState();
        return false;
    }

    private bool IsValidOptions() =>
        !string.IsNullOrWhiteSpace(options.ContentVersion) && options.ContentVersion.Length <= 64 &&
        options.DurationSeconds is >= 10 and <= 600 &&
        options.Items is { Count: >= 4 and <= 80 } &&
        options.Items.All(item => !string.IsNullOrWhiteSpace(item) && item.Trim().Length <= 64);

    private static List<int> ParseErrorIndexes(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        if (json.Length > 2048)
        {
            throw new FastNameRaceSubmissionException("The marked item list is too large.");
        }

        try
        {
            return JsonSerializer.Deserialize<List<int>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            throw new FastNameRaceSubmissionException("The marked item list could not be read.");
        }
    }

    private static FastNameRaceAttemptState? ReadState(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<FastNameRaceAttemptState>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private FastNameRaceSession BuildSession(
        ActivityAssignment assignment,
        ActivityAttempt attempt,
        FastNameRaceAttemptState state)
    {
        var isCompleted = attempt.CompletedAtUtc is not null || assignment.Status == ActivityAssignmentStatus.Completed;
        return new FastNameRaceSession(
            assignment.Id,
            assignment.LearningActivity.Name,
            false,
            isCompleted,
            attempt.Id,
            state.DurationSeconds,
            state.Items,
            state.ItemsCompleted,
            state.ErrorIndexes.Count,
            state.ActiveDurationMilliseconds,
            state.ItemsPerMinute,
            state.Accuracy,
            isCompleted
                ? supportiveFeedbackService.GetFeedback(SupportiveFeedbackEvent.ActivityCompleted, assignment.LearningActivity.Name)
                : null);
    }

    private static List<string> Shuffle(List<string> items)
    {
        for (var index = items.Count - 1; index > 0; index--)
        {
            var other = RandomNumberGenerator.GetInt32(index + 1);
            (items[index], items[other]) = (items[other], items[index]);
        }

        return items;
    }
}
