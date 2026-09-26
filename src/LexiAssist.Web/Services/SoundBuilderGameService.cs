using System.Security.Cryptography;
using System.Text.Json;
using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Activities;
using LexiAssist.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LexiAssist.Web.Services;

public sealed class SoundBuilderGameService(
    ApplicationDbContext dbContext,
    IOptions<SoundBuilderOptions> configuredOptions,
    TimeProvider timeProvider,
    AdaptiveSupportService adaptiveSupportService,
    ISupportiveFeedbackService supportiveFeedbackService) : ISoundBuilderGameService
{
    private static readonly TimeSpan LongInactivityThreshold = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SoundBuilderOptions options = configuredOptions.Value;

    public async Task<SoundBuilderSession?> GetSessionAsync(
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
            var contentAvailable = TryCreateAttemptState(out var initialState);
            return new SoundBuilderSession(
                assignment.Id,
                assignment.LearningActivity.Name,
                assignment.Status != ActivityAssignmentStatus.Completed && contentAvailable,
                assignment.Status == ActivityAssignmentStatus.Completed,
                null,
                0,
                contentAvailable ? initialState.Items.Count : 0,
                0,
                null,
                null,
                null,
                contentAvailable ? null : "Sound Builder content is not configured yet. Please ask your support professional for help.",
                null);
        }

        var state = ReadState(attempt.MetadataJson);
        return state is null
            ? new SoundBuilderSession(
                assignment.Id,
                assignment.LearningActivity.Name,
                false,
                attempt.CompletedAtUtc.HasValue,
                attempt.Id,
                0,
                0,
                0,
                attempt.Accuracy,
                attempt.DurationMilliseconds,
                null,
                "Saved practice content could not be read. This attempt is kept in your progress history.",
                null)
            : BuildSession(assignment, attempt, state);
    }

    public async Task<SoundBuilderSession?> StartAsync(
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

        var currentAttempt = await dbContext.ActivityAttempts
            .Where(item => item.ActivityAssignmentId == assignmentId && item.CompletedAtUtc == null)
            .OrderByDescending(item => item.StartedAtUtc)
            .ThenByDescending(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (currentAttempt is not null)
        {
            var savedState = ReadState(currentAttempt.MetadataJson);
            return savedState is null ? null : BuildSession(assignment, currentAttempt, savedState);
        }

        if (!TryCreateAttemptState(out var state))
        {
            return new SoundBuilderSession(
                assignment.Id,
                assignment.LearningActivity.Name,
                false,
                false,
                null,
                0,
                0,
                0,
                null,
                null,
                null,
                "Sound Builder content is not configured yet. Please ask your support professional for help.",
                null);
        }

        var startedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        var attempt = new ActivityAttempt
        {
            ActivityAssignmentId = assignment.Id,
            StartedAtUtc = startedAtUtc,
            MetadataJson = JsonSerializer.Serialize(state, JsonOptions)
        };
        assignment.Status = ActivityAssignmentStatus.InProgress;
        dbContext.ActivityAttempts.Add(attempt);
        await dbContext.SaveChangesAsync(cancellationToken);

        return BuildSession(assignment, attempt, state);
    }

    public async Task<SoundBuilderSession?> SubmitAsync(
        string learnerId,
        int assignmentId,
        int attemptId,
        string itemId,
        string orderedChunkIndexes,
        string? blendedWord,
        string? segmentedChunks,
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

        // A repeated form submission is an idempotent return to the current saved state.
        if (state.Responses.Any(response => string.Equals(response.ItemId, itemId, StringComparison.Ordinal)))
        {
            return BuildSession(assignment, attempt, state);
        }

        if (attempt.CompletedAtUtc is not null || assignment.Status == ActivityAssignmentStatus.Completed)
        {
            return BuildSession(assignment, attempt, state);
        }

        var nextItem = GetNextItem(state);
        if (nextItem is null || !string.Equals(nextItem.Id, itemId, StringComparison.Ordinal))
        {
            throw new SoundBuilderAnswerException("That practice item is no longer current. Your saved progress is still available.");
        }

        List<int> submittedIndexes = [];
        List<string> orderedChunks = [];
        List<string> segmentation = [];
        var answerIsCorrect = false;
        string? typedWord = null;
        if (nextItem.Mode == SoundBuilderMode.Blend)
        {
            submittedIndexes = ParseAndValidateChunkOrder(orderedChunkIndexes, nextItem.Chunks.Count);
            orderedChunks = submittedIndexes.Select(index => nextItem.Chunks[index]).ToList();
            typedWord = string.IsNullOrWhiteSpace(blendedWord) ? null : blendedWord.Trim();
            if (string.IsNullOrWhiteSpace(typedWord))
            {
                throw new SoundBuilderAnswerException("Type the word you blended from the chunks.");
            }

            answerIsCorrect = submittedIndexes.SequenceEqual(Enumerable.Range(0, nextItem.Chunks.Count)) &&
                              string.Equals(NormalizeWord(typedWord), NormalizeWord(nextItem.Word), StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            segmentation = ParseAndValidateSegmentation(segmentedChunks);
            answerIsCorrect = segmentation.SequenceEqual(nextItem.Chunks, StringComparer.OrdinalIgnoreCase);
        }

        var submittedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        state.Responses.Add(new SoundBuilderItemResponse
        {
            ItemId = nextItem.Id,
            OrderedChunkIndexes = submittedIndexes,
            OrderedChunks = orderedChunks,
            SegmentedChunks = segmentation,
            BlendedWord = nextItem.Mode == SoundBuilderMode.Blend ? typedWord : null,
            IsCorrect = answerIsCorrect,
            DifficultyLevel = nextItem.DifficultyLevel,
            SubmittedAtUtc = submittedAtUtc
        });

        var previousAdaptiveSupport = state.AdaptiveSupport;
        state.AdaptiveSupport = adaptiveSupportService.RecordAnswer(
            state.AdaptiveSupport,
            answerIsCorrect,
            state.Items.Where(item => state.Responses.All(response => response.ItemId != item.Id))
                .Select(item => item.DifficultyLevel)
                .ToArray());

        var correctCount = state.Responses.Count(response => response.IsCorrect);
        var isComplete = state.Responses.Count == state.Items.Count;
        attempt.Score = correctCount;
        attempt.Accuracy = state.Responses.Count == 0
            ? null
            : Math.Round(correctCount * 100d / state.Responses.Count, 2, MidpointRounding.AwayFromZero);
        attempt.DurationMilliseconds = Math.Max(0L, (long)(submittedAtUtc - attempt.StartedAtUtc).TotalMilliseconds);
        var savedStateJson = JsonSerializer.Serialize(state, JsonOptions);
        if (savedStateJson.Length > 16000)
        {
            state.Responses.RemoveAt(state.Responses.Count - 1);
            state.AdaptiveSupport = previousAdaptiveSupport;
            throw new SoundBuilderAnswerException("This configured practice round is too large to save. Please ask your support professional to review its content.");
        }

        attempt.MetadataJson = savedStateJson;
        if (isComplete)
        {
            attempt.CompletedAtUtc = submittedAtUtc;
            assignment.Status = ActivityAssignmentStatus.Completed;
            assignment.CompletedAtUtc = submittedAtUtc;
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
                assignment.LearningActivity.Name == "Sound Builder" &&
                assignment.LearningActivity.ActivityType == LearningActivityType.PhonologicalAwareness,
                cancellationToken);
    }

    private bool TryCreateAttemptState(out SoundBuilderAttemptState state)
    {
        state = new SoundBuilderAttemptState();
        var items = options.Items;
        if (string.IsNullOrWhiteSpace(options.ContentVersion) || options.ContentVersion.Length > 64 ||
            items is null || items.Count is < 1 or > 20 ||
            items.Any(item => string.IsNullOrWhiteSpace(item.Id) || item.Id.Length > 80 ||
                              string.IsNullOrWhiteSpace(item.Prompt) || item.Prompt.Length > 500 ||
                              string.IsNullOrWhiteSpace(item.Word) || item.Word.Length > 100 ||
                              item.DifficultyLevel is < 1 or > 5 ||
                              item.Chunks is null || item.Chunks.Count is < 2 or > 12 ||
                              item.Chunks.Any(chunk => string.IsNullOrWhiteSpace(chunk) || chunk.Length > 24) ||
                              !Enum.IsDefined(item.Mode)) ||
            items.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != items.Count)
        {
            return false;
        }

        if (items.Any(item => !string.Equals(
                NormalizeWord(string.Concat(item.Chunks)),
                NormalizeWord(item.Word),
                StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        state = new SoundBuilderAttemptState
        {
            ContentVersion = options.ContentVersion.Trim(),
            AdaptiveSupport = adaptiveSupportService.CreateState(items.Select(item => item.DifficultyLevel)),
            Items = items.Select(item => new SoundBuilderAttemptItem
            {
                Id = item.Id,
                Mode = item.Mode,
                Prompt = item.Prompt.Trim(),
                Word = item.Word.Trim(),
                DifficultyLevel = item.DifficultyLevel,
                Chunks = item.Chunks.Select(chunk => chunk.Trim()).ToList(),
                PresentationOrder = Shuffle(Enumerable.Range(0, item.Chunks.Count).ToList())
            }).ToList()
        };

        if (JsonSerializer.Serialize(state, JsonOptions).Length > 16000)
        {
            state = new SoundBuilderAttemptState();
            return false;
        }

        return true;
    }

    private static List<int> ParseAndValidateChunkOrder(string value, int expectedCount)
    {
        var indexes = (value ?? string.Empty)
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(item => int.TryParse(item, out var index) ? index : -1)
            .ToList();
        if (indexes.Count != expectedCount ||
            indexes.Any(index => index < 0 || index >= expectedCount) ||
            indexes.Distinct().Count() != expectedCount)
        {
            throw new SoundBuilderAnswerException("Place each sound chunk in the answer area once before continuing.");
        }

        return indexes;
    }

    private static List<string> ParseAndValidateSegmentation(string? value)
    {
        var chunks = (value ?? string.Empty)
            .Split([' ', '\t', '\r', '\n', ',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        if (chunks.Count is < 2 or > 12 || chunks.Any(chunk => chunk.Length > 24))
        {
            throw new SoundBuilderAnswerException("Type the sound chunks in order, with a space between each chunk.");
        }

        return chunks;
    }

    private static SoundBuilderAttemptState? ReadState(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SoundBuilderAttemptState>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private SoundBuilderSession BuildSession(
        ActivityAssignment assignment,
        ActivityAttempt attempt,
        SoundBuilderAttemptState state)
    {
        var nextItem = GetNextItem(state);
        var correctCount = state.Responses.Count(response => response.IsCorrect);
        var isCompleted = attempt.CompletedAtUtc is not null || assignment.Status == ActivityAssignmentStatus.Completed;
        var lastResponse = state.Responses.LastOrDefault();
        var feedback = lastResponse is null
            ? null
            : lastResponse.IsCorrect
                ? "Nice work putting those sounds together. Keep going at your own pace."
                : "Good effort. Sound chunks can take practice; try the next one when you are ready.";
        var feedbackEvent = isCompleted
            ? SupportiveFeedbackEvent.ActivityCompleted
            : IsInactive(lastResponse?.SubmittedAtUtc ?? attempt.StartedAtUtc)
                ? SupportiveFeedbackEvent.LongInactivity
                : lastResponse is null
                    ? (SupportiveFeedbackEvent?)null
                    : GetResponseFeedbackEvent(state, lastResponse);
        if (feedbackEvent is not null)
        {
            feedback = AppendSupportiveFeedback(feedback, feedbackEvent.Value, assignment.LearningActivity.Name);
        }
        feedback = AppendAdaptiveSupportMessage(feedback, state.AdaptiveSupport);
        var currentQuestion = nextItem is null || isCompleted
            ? null
            : new SoundBuilderQuestion(
                nextItem.Id,
                nextItem.Mode,
                nextItem.Prompt,
                nextItem.Mode == SoundBuilderMode.Segment ? nextItem.Word : string.Empty,
                nextItem.Mode == SoundBuilderMode.Blend
                    ? nextItem.PresentationOrder.Select(index => new SoundBuilderChunk(index, nextItem.Chunks[index])).ToArray()
                    : []);

        return new SoundBuilderSession(
            assignment.Id,
            assignment.LearningActivity.Name,
            false,
            isCompleted,
            attempt.Id,
            state.Responses.Count,
            state.Items.Count,
            correctCount,
            attempt.Accuracy,
            attempt.DurationMilliseconds,
            feedback,
            null,
            currentQuestion);
    }

    private SoundBuilderAttemptItem? GetNextItem(SoundBuilderAttemptState state) =>
        adaptiveSupportService.SelectNextItem(
            state.Items,
            state.Responses.Select(response => response.ItemId),
            item => item.Id,
            item => item.DifficultyLevel,
            state.AdaptiveSupport);

    private string AppendAdaptiveSupportMessage(string? feedback, AdaptiveSupportState state) =>
        string.IsNullOrWhiteSpace(feedback)
            ? adaptiveSupportService.Describe(state)
            : $"{feedback} {adaptiveSupportService.Describe(state)}";

    private string? AppendSupportiveFeedback(
        string? feedback,
        SupportiveFeedbackEvent feedbackEvent,
        string activityName) =>
        string.IsNullOrWhiteSpace(feedback)
            ? supportiveFeedbackService.GetFeedback(feedbackEvent, activityName)
            : $"{feedback} {supportiveFeedbackService.GetFeedback(feedbackEvent, activityName)}";

    private bool IsInactive(DateTime startedAtUtc) =>
        timeProvider.GetUtcNow().UtcDateTime - startedAtUtc >= LongInactivityThreshold;

    private static SupportiveFeedbackEvent GetResponseFeedbackEvent(
        SoundBuilderAttemptState state,
        SoundBuilderItemResponse lastResponse)
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

    private static List<int> Shuffle(List<int> values)
    {
        for (var index = values.Count - 1; index > 0; index--)
        {
            var other = RandomNumberGenerator.GetInt32(index + 1);
            (values[index], values[other]) = (values[other], values[index]);
        }

        return values;
    }

    private static string NormalizeWord(string? word) =>
        string.Concat((word ?? string.Empty).Where(character => !char.IsWhiteSpace(character)));
}
