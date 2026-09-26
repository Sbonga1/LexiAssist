using System.Globalization;
using System.Text.Json;
using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Activities;
using LexiAssist.Web.Services.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LexiAssist.Web.Services;

public sealed class SayWriteMatchGameService(
    ApplicationDbContext dbContext,
    IOptions<SayWriteMatchOptions> configuredOptions,
    IWebHostEnvironment webHostEnvironment,
    TimeProvider timeProvider,
    AdaptiveSupportService adaptiveSupportService,
    ISupportiveFeedbackService supportiveFeedbackService) : ISayWriteMatchGameService
{
    private static readonly TimeSpan LongInactivityThreshold = TimeSpan.FromMinutes(5);
    private const int MaximumMetadataLength = 16000;
    private const int MaximumResponses = 200;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> SupportedAudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".ogg", ".webm"
    };
    private readonly SayWriteMatchOptions options = configuredOptions.Value;

    public async Task<SayWriteMatchSession?> GetSessionAsync(
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
            var valid = IsValidOptions();
            return EmptySession(
                assignment,
                valid,
                valid ? null : "No valid spelling content is configured yet. Please ask your support professional for help.");
        }

        var state = ReadState(attempt.MetadataJson);
        return state is null
            ? new SayWriteMatchSession(
                assignment.Id,
                false,
                attempt.CompletedAtUtc.HasValue,
                attempt.Id,
                0,
                0,
                0,
                0,
                null,
                attempt.DurationMilliseconds,
                null,
                string.Empty,
                null,
                "Saved practice details could not be read. This attempt remains in your progress history.")
            : BuildSession(assignment, attempt, state);
    }

    public async Task<SayWriteMatchSession?> StartAsync(
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

        if (!IsValidOptions())
        {
            return EmptySession(
                assignment,
                false,
                "No valid spelling content is configured yet. Please ask your support professional for help.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var state = new SayWriteMatchAttemptState
        {
            ContentVersion = options.ContentVersion.Trim(),
            AdaptiveSupport = adaptiveSupportService.CreateState(options.Items.Select(item => item.DifficultyLevel)),
            CurrentItemStartedAtUtc = now,
            Items = options.Items.Select(CreateAttemptItem).ToList()
        };
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

    public async Task<SayWriteMatchSession?> SubmitAsync(
        string learnerId,
        int assignmentId,
        int attemptId,
        string itemId,
        string orderedChunkIndexes,
        Guid submissionId,
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

        if (state.Responses.Any(response => response.SubmissionId == submissionId) ||
            attempt.CompletedAtUtc is not null || assignment.Status == ActivityAssignmentStatus.Completed)
        {
            return BuildSession(assignment, attempt, state);
        }

        if (submissionId == Guid.Empty)
        {
            throw new SayWriteMatchAnswerException("Please arrange the chunks and submit your answer again.");
        }

        var currentItem = GetNextItem(state);
        if (currentItem is null || !string.Equals(currentItem.Id, itemId, StringComparison.Ordinal))
        {
            throw new SayWriteMatchAnswerException("That practice item is no longer current. Your saved progress is still available.");
        }

        if (state.Responses.Count >= MaximumResponses)
        {
            throw new SayWriteMatchAnswerException("This practice round has reached its response limit. Please ask your support professional for help.");
        }

        var order = ParseAndValidateOrder(orderedChunkIndexes, currentItem.Chunks.Count);
        if (order is null)
        {
            throw new SayWriteMatchAnswerException("Arrange every letter or chunk before checking your answer.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var isCorrect = order.SequenceEqual(Enumerable.Range(0, currentItem.Chunks.Count));
        state.Responses.Add(new SayWriteMatchAttemptResponse
        {
            SubmissionId = submissionId,
            ItemId = currentItem.Id,
            TargetWord = currentItem.Word,
            OrderedChunkIndexes = order,
            IsCorrect = isCorrect,
            DifficultyLevel = currentItem.DifficultyLevel,
            ResponseTimeMilliseconds = Math.Max(0L, (long)(now - state.CurrentItemStartedAtUtc).TotalMilliseconds),
            CreatedAtUtc = now
        });
        var previousAdaptiveSupport = state.AdaptiveSupport;
        state.AdaptiveSupport = adaptiveSupportService.RecordAnswer(
            state.AdaptiveSupport,
            isCorrect,
            state.Items.Where(item => state.Responses.All(response => response.ItemId != item.Id))
                .Select(item => item.DifficultyLevel)
                .ToArray());
        state.CurrentItemIndex++;
        state.CurrentItemStartedAtUtc = now;

        var isComplete = state.CurrentItemIndex >= state.Items.Count;
        var serializedState = JsonSerializer.Serialize(state, JsonOptions);
        if (serializedState.Length > MaximumMetadataLength)
        {
            state.Responses.RemoveAt(state.Responses.Count - 1);
            state.CurrentItemIndex--;
            state.AdaptiveSupport = previousAdaptiveSupport;
            throw new SayWriteMatchAnswerException("This activity has reached its saved response limit. Please ask your support professional for help.");
        }

        var responseCount = state.Responses.Count;
        var correctCount = state.Responses.Count(response => response.IsCorrect);
        attempt.Score = correctCount;
        attempt.Accuracy = responseCount == 0 ? null : Math.Round(correctCount * 100d / responseCount, 2);
        attempt.DurationMilliseconds = Math.Max(0L, (long)(now - attempt.StartedAtUtc).TotalMilliseconds);
        attempt.MetadataJson = serializedState;
        if (isComplete)
        {
            attempt.CompletedAtUtc = now;
            assignment.Status = ActivityAssignmentStatus.Completed;
            assignment.CompletedAtUtc = now;
        }

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
                assignment.LearningActivity.Name == "Say-Write-Match" &&
                assignment.LearningActivity.ActivityType == LearningActivityType.Spelling,
                cancellationToken);
    }

    private SayWriteMatchSession BuildSession(
        ActivityAssignment assignment,
        ActivityAttempt attempt,
        SayWriteMatchAttemptState state)
    {
        var completed = attempt.CompletedAtUtc is not null || assignment.Status == ActivityAssignmentStatus.Completed;
        SayWriteMatchQuestion? currentQuestion = null;
        var order = string.Empty;
        if (!completed)
        {
            var item = GetNextItem(state);
            if (item is null)
            {
                return new SayWriteMatchSession(assignment.Id, false, completed, attempt.Id, state.CurrentItemIndex, state.Items.Count, state.Responses.Count, state.Responses.Count(response => response.IsCorrect), attempt.Accuracy, attempt.DurationMilliseconds, null, string.Empty, null, null);
            }
            currentQuestion = new SayWriteMatchQuestion(
                item.Id,
                item.Prompt,
                IsExistingSafeAudioPath(item.AudioPath) ? item.AudioPath : null,
                item.AudioUnavailable || !string.IsNullOrWhiteSpace(item.AudioPath) && !IsExistingSafeAudioPath(item.AudioPath),
                item.ScrambledChunkIndexes
                    .Where(index => index >= 0 && index < item.Chunks.Count)
                    .Select(index => new SayWriteMatchChunk(index, item.Chunks[index]))
                    .ToArray());
            order = string.Join(',', item.ScrambledChunkIndexes);
        }

        var lastResponse = state.Responses.LastOrDefault();
        var feedback = lastResponse is null
            ? null
            : lastResponse.IsCorrect
                ? $"Nice matching! The chunks make “{lastResponse.TargetWord}.” Keep it up."
                : $"Good try! The chunks for this item spell “{lastResponse.TargetWord}.” Take your time with the next one.";
        var feedbackEvent = completed
            ? SupportiveFeedbackEvent.ActivityCompleted
            : IsInactive(lastResponse?.CreatedAtUtc ?? state.CurrentItemStartedAtUtc)
                ? SupportiveFeedbackEvent.LongInactivity
                : lastResponse is null
                    ? (SupportiveFeedbackEvent?)null
                    : GetResponseFeedbackEvent(state, lastResponse);
        if (feedbackEvent is not null)
        {
            feedback = AppendSupportiveFeedback(feedback, feedbackEvent.Value, "Say-Write-Match");
        }
        feedback = string.IsNullOrWhiteSpace(feedback)
            ? adaptiveSupportService.Describe(state.AdaptiveSupport)
            : $"{feedback} {adaptiveSupportService.Describe(state.AdaptiveSupport)}";
        var responseCount = state.Responses.Count;
        var correctCount = state.Responses.Count(response => response.IsCorrect);
        var duration = attempt.DurationMilliseconds ??
                       Math.Max(0L, (long)(timeProvider.GetUtcNow().UtcDateTime - attempt.StartedAtUtc).TotalMilliseconds);

        return new SayWriteMatchSession(
            assignment.Id,
            false,
            completed,
            attempt.Id,
            state.CurrentItemIndex,
            state.Items.Count,
            responseCount,
            correctCount,
            responseCount == 0 ? null : Math.Round(correctCount * 100d / responseCount, 2),
            duration,
            currentQuestion,
            order,
            feedback,
            null);
    }

    private string? AppendSupportiveFeedback(
        string? feedback,
        SupportiveFeedbackEvent feedbackEvent,
        string activityName) =>
        string.IsNullOrWhiteSpace(feedback)
            ? supportiveFeedbackService.GetFeedback(feedbackEvent, activityName)
            : $"{feedback} {supportiveFeedbackService.GetFeedback(feedbackEvent, activityName)}";

    private bool IsInactive(DateTime startedAtUtc) =>
        startedAtUtc != default && timeProvider.GetUtcNow().UtcDateTime - startedAtUtc >= LongInactivityThreshold;

    private static SupportiveFeedbackEvent GetResponseFeedbackEvent(
        SayWriteMatchAttemptState state,
        SayWriteMatchAttemptResponse lastResponse)
    {
        if (lastResponse.IsCorrect)
        {
            return state.Responses.Count > 1 && !state.Responses[^2].IsCorrect
                ? SupportiveFeedbackEvent.SuccessfulRetry
                : SupportiveFeedbackEvent.CorrectResponse;
        }

        var consecutiveIncorrect = state.Responses
            .AsEnumerable()
            .Reverse()
            .TakeWhile(response => !response.IsCorrect)
            .Count();
        return consecutiveIncorrect >= 3
            ? SupportiveFeedbackEvent.RepeatedDifficulty
            : SupportiveFeedbackEvent.FirstIncorrectAttempt;
    }

    private bool IsExistingSafeAudioPath(string? path) =>
        TryGetSafeAudioRelativePath(path, out var relativePath) &&
        webHostEnvironment.WebRootFileProvider.GetFileInfo(relativePath).Exists;

    private static bool IsValidOptionsItem(SayWriteMatchItem? item)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.Id) || item.Id.Trim().Length > 80 ||
            string.IsNullOrWhiteSpace(item.Word) || item.Word.Trim().Length > 100 ||
            item.DifficultyLevel is < 1 or > 5 ||
            item.Word.Any(char.IsWhiteSpace) || string.IsNullOrWhiteSpace(item.Prompt) || item.Prompt.Trim().Length > 500 ||
            item.Chunks is not { Count: >= 2 and <= 12 } ||
            item.Chunks.Any(chunk => string.IsNullOrWhiteSpace(chunk) || chunk.Trim().Length > 32 || chunk.Any(char.IsWhiteSpace)) ||
            item.AudioPath is { Length: > 1024 } ||
            !string.IsNullOrWhiteSpace(item.AudioPath) && !TryGetSafeAudioRelativePath(item.AudioPath, out _))
        {
            return false;
        }

        return string.Equals(
            RemoveWhitespace(string.Concat(item.Chunks)),
            RemoveWhitespace(item.Word),
            StringComparison.OrdinalIgnoreCase);
    }

    private bool IsValidOptions() =>
        !string.IsNullOrWhiteSpace(options.ContentVersion) && options.ContentVersion.Trim().Length <= 64 &&
        options.Items is { Count: >= 1 and <= 40 } &&
        options.Items.All(IsValidOptionsItem) &&
        options.Items.Select(item => item.Id.Trim()).Distinct(StringComparer.Ordinal).Count() == options.Items.Count;

    private SayWriteMatchAttemptItem CreateAttemptItem(SayWriteMatchItem item)
    {
        var indexes = Enumerable.Range(0, item.Chunks.Count).ToArray();
        for (var index = indexes.Length - 1; index > 0; index--)
        {
            var swapIndex = Random.Shared.Next(index + 1);
            (indexes[index], indexes[swapIndex]) = (indexes[swapIndex], indexes[index]);
        }

        if (indexes.SequenceEqual(Enumerable.Range(0, indexes.Length)))
        {
            (indexes[0], indexes[1]) = (indexes[1], indexes[0]);
        }

        var hasAudioPath = !string.IsNullOrWhiteSpace(item.AudioPath);
        var safeAudioPath = hasAudioPath && TryGetSafeAudioRelativePath(item.AudioPath, out _) ? item.AudioPath!.Trim() : null;
        return new SayWriteMatchAttemptItem
        {
            Id = item.Id.Trim(),
            Word = item.Word.Trim(),
            Prompt = item.Prompt.Trim(),
            DifficultyLevel = item.DifficultyLevel,
            AudioPath = safeAudioPath,
            AudioUnavailable = hasAudioPath && (safeAudioPath is null || !IsExistingSafeAudioPath(safeAudioPath)),
            Chunks = item.Chunks.Select(chunk => chunk.Trim()).ToList(),
            ScrambledChunkIndexes = indexes.ToList()
        };
    }

    private SayWriteMatchAttemptItem? GetNextItem(SayWriteMatchAttemptState state) =>
        adaptiveSupportService.SelectNextItem(
            state.Items,
            state.Responses.Select(response => response.ItemId),
            item => item.Id,
            item => item.DifficultyLevel,
            state.AdaptiveSupport);

    private static string RemoveWhitespace(string value) =>
        string.Concat(value.Where(character => !char.IsWhiteSpace(character)));

    private static List<int>? ParseAndValidateOrder(string? value, int expectedCount)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64)
        {
            return null;
        }

        var tokens = value.Split(',', StringSplitOptions.TrimEntries);
        if (tokens.Length != expectedCount)
        {
            return null;
        }

        var indexes = new List<int>(expectedCount);
        foreach (var token in tokens)
        {
            if (!int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
            {
                return null;
            }

            indexes.Add(index);
        }

        return indexes.Distinct().Count() == expectedCount &&
               indexes.Order().SequenceEqual(Enumerable.Range(0, expectedCount))
            ? indexes
            : null;
    }

    private static bool TryGetSafeAudioRelativePath(string? path, out string relativePath)
    {
        relativePath = string.Empty;
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("/media/say-write-match/", StringComparison.Ordinal) ||
            path.Contains('\\') || path.Contains('?') || path.Contains('#') || path.Contains(':') || path.Contains('%') ||
            path.Contains('\0') || path.Contains("//", StringComparison.Ordinal))
        {
            return false;
        }

        var relative = path[1..];
        var segments = relative.Split('/');
        if (segments.Any(segment => segment is "" or "." or "..") ||
            !SupportedAudioExtensions.Contains(Path.GetExtension(relative)))
        {
            return false;
        }

        relativePath = relative;
        return true;
    }

    private SayWriteMatchSession EmptySession(ActivityAssignment assignment, bool hasValidContent, string? message)
    {
        var canStart = hasValidContent && assignment.Status != ActivityAssignmentStatus.Completed;
        return new SayWriteMatchSession(
            assignment.Id,
            canStart,
            assignment.Status == ActivityAssignmentStatus.Completed,
            null,
            0,
            hasValidContent ? options.Items.Count : 0,
            0,
            0,
            null,
            null,
            null,
            string.Empty,
            null,
            message);
    }

    private static SayWriteMatchAttemptState? ReadState(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SayWriteMatchAttemptState>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
