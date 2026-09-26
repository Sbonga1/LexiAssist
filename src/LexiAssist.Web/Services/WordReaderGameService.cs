using System.Text.Json;
using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Activities;
using LexiAssist.Web.Models.Identity;
using LexiAssist.Web.Models.Reports;
using LexiAssist.Web.Models.Screening;
using LexiAssist.Web.Services.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LexiAssist.Web.Services;

public sealed class WordReaderGameService(
    ApplicationDbContext dbContext,
    IOptions<WordReaderOptions> configuredOptions,
    IWordReaderAttemptEvaluator attemptEvaluator,
    IWebHostEnvironment webHostEnvironment,
    TimeProvider timeProvider,
    AdaptiveSupportService adaptiveSupportService,
    ISupportiveFeedbackService supportiveFeedbackService) : IWordReaderGameService
{
    private static readonly TimeSpan LongInactivityThreshold = TimeSpan.FromMinutes(5);
    private const int MaximumMetadataLength = 16000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> SupportedAudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".ogg", ".webm"
    };
    private readonly WordReaderOptions options = configuredOptions.Value;

    public async Task<WordReaderSession?> GetSessionAsync(
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
            var set = await FindTargetSetAsync(assignment, learnerId, cancellationToken);
            var valid = set is not null && IsValidOptions(set);
            return new WordReaderSession(
                assignment.Id,
                assignment.LearningActivity.Name,
                valid ? set!.SkillArea : null,
                assignment.Status != ActivityAssignmentStatus.Completed && valid,
                assignment.Status == ActivityAssignmentStatus.Completed,
                null,
                0,
                valid ? set!.Items.Count : 0,
                0,
                0,
                0,
                null,
                null,
                null,
                valid
                    ? null
                    : "No valid word practice content is configured for this assignment's target skill yet. Please ask your support professional for help.");
        }

        var state = ReadState(attempt.MetadataJson);
        return state is null
            ? new WordReaderSession(
                assignment.Id,
                assignment.LearningActivity.Name,
                null,
                false,
                attempt.CompletedAtUtc.HasValue,
                attempt.Id,
                0,
                0,
                0,
                0,
                0,
                attempt.DurationMilliseconds,
                null,
                null,
                "Saved word practice details could not be read. This attempt remains in your progress history.")
            : BuildSession(assignment, attempt, state);
    }

    public async Task<WordReaderSession?> StartAsync(
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

        var set = await FindTargetSetAsync(assignment, learnerId, cancellationToken);
        if (set is null || !IsValidOptions(set))
        {
            return new WordReaderSession(
                assignment.Id,
                assignment.LearningActivity.Name,
                null,
                false,
                false,
                null,
                0,
                0,
                0,
                0,
                0,
                null,
                null,
                null,
                "No valid word practice content is configured for this assignment's target skill yet. Please ask your support professional for help.");
        }

        var state = new WordReaderAttemptState
        {
            ContentVersion = options.ContentVersion.Trim(),
            SkillArea = set.SkillArea.Trim(),
            AdaptiveSupport = adaptiveSupportService.CreateState(set.Items.Select(item => item.DifficultyLevel)),
            Items = set.Items.Select(item => new WordReaderAttemptItem
            {
                Id = item.Id.Trim(),
                Word = item.Word.Trim(),
                DifficultyLevel = item.DifficultyLevel,
                AudioPath = TryGetSafeAudioRelativePath(item.AudioPath, out _) ? item.AudioPath!.Trim() : null,
                AudioUnavailable = !string.IsNullOrWhiteSpace(item.AudioPath) &&
                                   !TryGetSafeAudioRelativePath(item.AudioPath, out _)
            }).ToList()
        };

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

    public async Task<WordReaderSession?> RespondAsync(
        string learnerId,
        int assignmentId,
        int attemptId,
        string itemId,
        Guid submissionId,
        WordReaderPracticeOutcome outcome,
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

        if (state.Responses.Any(response => response.SubmissionId == submissionId))
        {
            return BuildSession(assignment, attempt, state);
        }

        if (attempt.CompletedAtUtc is not null || assignment.Status == ActivityAssignmentStatus.Completed)
        {
            return BuildSession(assignment, attempt, state);
        }

        if (submissionId == Guid.Empty || !Enum.IsDefined(outcome))
        {
            throw new WordReaderAnswerException("Choose how this practice try went before continuing.");
        }

        var currentItem = GetNextItem(state);
        if (currentItem is null || !string.Equals(currentItem.Id, itemId, StringComparison.Ordinal))
        {
            throw new WordReaderAnswerException("That word is no longer current. Your saved practice progress is still available.");
        }

        var evaluation = await attemptEvaluator.EvaluateAsync(new WordReaderAttemptEvaluationContext(
                currentItem.Word,
                outcome,
                AudioRecordingPath: null),
            cancellationToken);
        if (evaluation.Method.Length > 64)
        {
            throw new WordReaderAnswerException("This practice response could not be saved. Please try again.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        state.Responses.Add(new WordReaderAttemptResponse
        {
            SubmissionId = submissionId,
            ItemId = currentItem.Id,
            Outcome = outcome,
            IsCorrect = evaluation.IsCorrect,
            DifficultyLevel = currentItem.DifficultyLevel,
            EvaluationMethod = evaluation.Method,
            CreatedAtUtc = now
        });
        var previousAdaptiveSupport = state.AdaptiveSupport;
        state.AdaptiveSupport = adaptiveSupportService.RecordAnswer(
            state.AdaptiveSupport,
            outcome == WordReaderPracticeOutcome.RetryRequested ? null : evaluation.IsCorrect,
            state.Items.Where(item => state.Responses
                    .Where(response => response.Outcome != WordReaderPracticeOutcome.RetryRequested)
                    .All(response => response.ItemId != item.Id))
                .Select(item => item.DifficultyLevel)
                .ToArray());
        if (outcome != WordReaderPracticeOutcome.RetryRequested)
        {
            state.CurrentItemIndex += 1;
        }

        var isComplete = state.CurrentItemIndex >= state.Items.Count;
        var serializedState = JsonSerializer.Serialize(state, JsonOptions);
        if (serializedState.Length > MaximumMetadataLength)
        {
            state.Responses.RemoveAt(state.Responses.Count - 1);
            if (outcome != WordReaderPracticeOutcome.RetryRequested)
            {
                state.CurrentItemIndex -= 1;
            }

            state.AdaptiveSupport = previousAdaptiveSupport;

            throw new WordReaderAnswerException("This activity has reached its saved response limit. Please ask your support professional for help.");
        }

        attempt.Score = state.CurrentItemIndex;
        attempt.Accuracy = null;
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
                assignment.LearningActivity.Name == "Word Reader" &&
                assignment.LearningActivity.ActivityType == LearningActivityType.Reading,
                cancellationToken);
    }

    private async Task<WordReaderSkillSet?> FindTargetSetAsync(
        ActivityAssignment assignment,
        string learnerId,
        CancellationToken cancellationToken)
    {
        if (!IsValidOptions() || assignment.RelatedScreeningReportId is not { } reportId)
        {
            return null;
        }

        var flaggedAreasJson = await dbContext.ScreeningReports
            .AsNoTracking()
            .Where(report => report.Id == reportId && report.UserId == learnerId &&
                             report.ScreeningAttempt.Status == ScreeningAttemptStatus.Completed &&
                             report.ScreeningAttempt.CompletedAtUtc != null)
            .Select(report => report.FlaggedAreasJson)
            .SingleOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(flaggedAreasJson))
        {
            return null;
        }

        List<ScreeningReportFlaggedAreaSnapshot>? flaggedAreas;
        try
        {
            flaggedAreas = JsonSerializer.Deserialize<List<ScreeningReportFlaggedAreaSnapshot>>(flaggedAreasJson, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        if (flaggedAreas is null)
        {
            return null;
        }

        foreach (var flaggedArea in flaggedAreas)
        {
            var targetSet = options.SkillSets.FirstOrDefault(set =>
                string.Equals(set.SkillArea.Trim(), flaggedArea.Name?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (targetSet is not null)
            {
                return targetSet;
            }
        }

        return null;
    }

    private bool IsValidOptions() =>
        !string.IsNullOrWhiteSpace(options.ContentVersion) && options.ContentVersion.Length <= 64 &&
        options.SkillSets is { Count: >= 1 and <= 20 } &&
        options.SkillSets.All(IsValidOptions) &&
        options.SkillSets.Select(set => set.SkillArea.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() == options.SkillSets.Count;

    private static bool IsValidOptions(WordReaderSkillSet set) =>
        !string.IsNullOrWhiteSpace(set.SkillArea) && set.SkillArea.Trim().Length <= 120 &&
        set.Items is { Count: >= 1 and <= 40 } &&
        set.Items.All(item => !string.IsNullOrWhiteSpace(item.Id) && item.Id.Trim().Length <= 80 &&
                              !string.IsNullOrWhiteSpace(item.Word) && item.Word.Trim().Length <= 100 &&
                              item.DifficultyLevel is >= 1 and <= 5) &&
        set.Items.Select(item => item.Id.Trim()).Distinct(StringComparer.Ordinal).Count() == set.Items.Count;

    private WordReaderSession BuildSession(
        ActivityAssignment assignment,
        ActivityAttempt attempt,
        WordReaderAttemptState state)
    {
        var isCompleted = attempt.CompletedAtUtc is not null || assignment.Status == ActivityAssignmentStatus.Completed;
        WordReaderPracticeItem? currentItem = null;
        if (!isCompleted)
        {
            var savedItem = GetNextItem(state);
            if (savedItem is null)
            {
                return new WordReaderSession(assignment.Id, assignment.LearningActivity.Name, state.SkillArea, false, isCompleted, attempt.Id, state.CurrentItemIndex, state.Items.Count, state.Responses.Count, state.Responses.Count(response => response.Outcome == WordReaderPracticeOutcome.RetryRequested), state.Responses.Count(response => response.Outcome == WordReaderPracticeOutcome.PractisedWithSupport), attempt.DurationMilliseconds, null, adaptiveSupportService.Describe(state.AdaptiveSupport), null);
            }
            var audioPath = string.IsNullOrWhiteSpace(savedItem.AudioPath) ? null : savedItem.AudioPath;
            var audioAvailable = audioPath is not null && IsExistingSafeAudioPath(audioPath);
            currentItem = new WordReaderPracticeItem(
                savedItem.Id,
                savedItem.Word,
                audioAvailable ? audioPath : null,
                savedItem.AudioUnavailable || audioPath is not null && !audioAvailable);
        }

        var lastResponse = state.Responses.LastOrDefault();
        var feedback = lastResponse?.Outcome switch
        {
            WordReaderPracticeOutcome.ReadWithoutHelp => "Your self-reported practice try is saved. Move on when you feel ready.",
            WordReaderPracticeOutcome.PractisedWithSupport => "Your supported practice try is saved. Using support is part of learning.",
            WordReaderPracticeOutcome.RetryRequested => "Take another look at this word or listen to the model, then try again when you are ready.",
            _ => null
        };
        var feedbackEvent = isCompleted
            ? SupportiveFeedbackEvent.ActivityCompleted
            : IsInactive(lastResponse?.CreatedAtUtc ?? attempt.StartedAtUtc)
                ? SupportiveFeedbackEvent.LongInactivity
                : lastResponse is null
                    ? (SupportiveFeedbackEvent?)null
                    : GetResponseFeedbackEvent(state, lastResponse);
        if (feedbackEvent is not null)
        {
            feedback = AppendSupportiveFeedback(feedback, feedbackEvent.Value, assignment.LearningActivity.Name);
        }
        feedback = string.IsNullOrWhiteSpace(feedback)
            ? adaptiveSupportService.Describe(state.AdaptiveSupport)
            : $"{feedback} {adaptiveSupportService.Describe(state.AdaptiveSupport)}";

        var durationMilliseconds = attempt.DurationMilliseconds ??
                                  Math.Max(0L, (long)(timeProvider.GetUtcNow().UtcDateTime - attempt.StartedAtUtc).TotalMilliseconds);
        return new WordReaderSession(
            assignment.Id,
            assignment.LearningActivity.Name,
            state.SkillArea,
            false,
            isCompleted,
            attempt.Id,
            state.CurrentItemIndex,
            state.Items.Count,
            state.Responses.Count,
            state.Responses.Count(response => response.Outcome == WordReaderPracticeOutcome.RetryRequested),
            state.Responses.Count(response => response.Outcome == WordReaderPracticeOutcome.PractisedWithSupport),
            durationMilliseconds,
            currentItem,
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
        WordReaderAttemptState state,
        WordReaderAttemptResponse lastResponse)
    {
        var itemResponses = state.Responses.Where(response => response.ItemId == lastResponse.ItemId).ToList();
        return lastResponse.Outcome switch
        {
            WordReaderPracticeOutcome.ReadWithoutHelp or WordReaderPracticeOutcome.PractisedWithSupport =>
                itemResponses.Any(response => response.Outcome == WordReaderPracticeOutcome.RetryRequested)
                    ? SupportiveFeedbackEvent.SuccessfulRetry
                    : SupportiveFeedbackEvent.CorrectResponse,
            WordReaderPracticeOutcome.RetryRequested =>
                itemResponses.Count(response => response.Outcome == WordReaderPracticeOutcome.RetryRequested) >= 3
                    ? SupportiveFeedbackEvent.RepeatedDifficulty
                    : SupportiveFeedbackEvent.FirstIncorrectAttempt,
            _ => SupportiveFeedbackEvent.FirstIncorrectAttempt
        };
    }

    private WordReaderAttemptItem? GetNextItem(WordReaderAttemptState state) =>
        adaptiveSupportService.SelectNextItem(
            state.Items,
            state.Responses
                .Where(response => response.Outcome != WordReaderPracticeOutcome.RetryRequested)
                .Select(response => response.ItemId),
            item => item.Id,
            item => item.DifficultyLevel,
            state.AdaptiveSupport);

    private bool IsExistingSafeAudioPath(string path) =>
        TryGetSafeAudioRelativePath(path, out var relativePath) &&
        webHostEnvironment.WebRootFileProvider.GetFileInfo(relativePath).Exists;

    private static bool TryGetSafeAudioRelativePath(string? path, out string relativePath)
    {
        relativePath = string.Empty;
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("/media/word-reader/", StringComparison.Ordinal) ||
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

    private static WordReaderAttemptState? ReadState(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<WordReaderAttemptState>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
