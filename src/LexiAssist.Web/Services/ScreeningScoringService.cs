using System.Text.Json;
using LexiAssist.Web.Models.Screening;
using LexiAssist.Web.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace LexiAssist.Web.Services;

public sealed class ScreeningScoringService(IOptions<ScreeningScoringOptions> configuredOptions) : IScreeningScoringService
{
    private static readonly JsonSerializerOptions ResponseDetailsJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ScreeningScoringOptions options = configuredOptions.Value;

    public ScreeningTestScoringResult ScoreTest(
        ScreeningTest screeningTest,
        IEnumerable<ScreeningResponse> responses,
        bool includeInactive = false)
    {
        ArgumentNullException.ThrowIfNull(screeningTest);
        ArgumentNullException.ThrowIfNull(responses);
        ValidateOptions();

        var activeQuestions = (screeningTest.IsActive || includeInactive)
            ? screeningTest.Questions
                .Where(question => question.IsActive && Enum.IsDefined(question.QuestionType))
                .OrderBy(question => question.Id)
                .ToArray()
            : [];
        var questionIds = activeQuestions.Select(question => question.Id).ToHashSet();
        var responseByQuestionId = new Dictionary<int, ScreeningResponse>();

        foreach (var response in responses)
        {
            if (!questionIds.Contains(response.ScreeningQuestionId))
            {
                continue;
            }

            if (!responseByQuestionId.TryAdd(response.ScreeningQuestionId, response))
            {
                throw new ArgumentException(
                    $"More than one response was provided for question {response.ScreeningQuestionId}.",
                    nameof(responses));
            }
        }

        var questionScores = activeQuestions
            .Select(question => ScoreQuestion(question, responseByQuestionId.GetValueOrDefault(question.Id)))
            .ToArray();
        var rawScore = questionScores.Sum(score => score.RawScore);
        var availableScore = questionScores.Sum(score => score.AvailableScore);
        var percentage = CalculatePercentage(rawScore, availableScore);
        var answeredCount = questionScores.Count(score => score.HasResponse);
        var completionStatus = GetCompletionStatus(answeredCount, questionScores.Length);
        var skillArea = GetFlaggedSkillArea(screeningTest.Name, percentage);
        var activityMetrics = BuildActivityMetrics(activeQuestions, responseByQuestionId, questionScores);

        return new ScreeningTestScoringResult(
            screeningTest.Id,
            screeningTest.Name,
            screeningTest.Slug,
            rawScore,
            availableScore,
            percentage,
            skillArea is null ? Array.Empty<string>() : [skillArea],
            completionStatus,
            answeredCount,
            questionScores.Length,
            questionScores.Count(score => score.IsScorable),
            questionScores)
        {
            ActivityMetrics = activityMetrics
        };
    }

    public ScreeningOverallScoringResult ScoreOverall(IEnumerable<ScreeningTestScoringResult> testResults)
    {
        ArgumentNullException.ThrowIfNull(testResults);
        ValidateOptions();

        var orderedResults = testResults
            .OrderBy(result => result.ScreeningTestId)
            .ToArray();
        if (orderedResults.Select(result => result.ScreeningTestId).Distinct().Count() != orderedResults.Length)
        {
            throw new ArgumentException("Only one result per screening test can be combined.", nameof(testResults));
        }

        var rawScore = orderedResults.Sum(result => result.RawScore);
        var availableScore = orderedResults.Sum(result => result.AvailableScore);
        var answeredCount = orderedResults.Sum(result => result.AnsweredQuestionCount);
        var expectedCount = orderedResults.Sum(result => result.ExpectedQuestionCount);
        var completionStatus = GetCompletionStatus(answeredCount, expectedCount);
        var flaggedSkillAreas = orderedResults
            .SelectMany(result => result.FlaggedSkillAreas)
            .Where(area => !string.IsNullOrWhiteSpace(area))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(area => area, StringComparer.Ordinal)
            .ToArray();

        return new ScreeningOverallScoringResult(
            rawScore,
            availableScore,
            CalculatePercentage(rawScore, availableScore),
            flaggedSkillAreas,
            completionStatus,
            answeredCount,
            expectedCount,
            orderedResults);
    }

    private static IReadOnlyList<ScreeningActivityMetric> BuildActivityMetrics(
        IReadOnlyList<ScreeningQuestion> activeQuestions,
        IReadOnlyDictionary<int, ScreeningResponse> responseByQuestionId,
        IReadOnlyList<ScreeningQuestionScore> questionScores)
    {
        var scoreByQuestionId = questionScores.ToDictionary(score => score.QuestionId);
        var metrics = new List<ScreeningActivityMetric>();

        var workingMemoryQuestions = activeQuestions
            .Where(question => question.QuestionType == ScreeningQuestionType.WorkingMemorySequence)
            .ToArray();
        if (workingMemoryQuestions.Length > 0)
        {
            metrics.Add(BuildWorkingMemoryMetric(workingMemoryQuestions, responseByQuestionId, scoreByQuestionId));
        }

        var processingSpeedQuestions = activeQuestions
            .Where(question => question.QuestionType == ScreeningQuestionType.TimedMatching)
            .ToArray();
        if (processingSpeedQuestions.Length > 0)
        {
            metrics.Add(BuildProcessingSpeedMetric(processingSpeedQuestions, responseByQuestionId, scoreByQuestionId));
        }

        var patternReasoningQuestions = activeQuestions
            .Where(question => question.QuestionType == ScreeningQuestionType.PatternReasoning)
            .ToArray();
        if (patternReasoningQuestions.Length > 0)
        {
            metrics.Add(BuildPatternReasoningMetric(patternReasoningQuestions, responseByQuestionId, scoreByQuestionId));
        }

        return metrics;
    }

    private static ScreeningActivityMetric BuildWorkingMemoryMetric(
        IReadOnlyList<ScreeningQuestion> questions,
        IReadOnlyDictionary<int, ScreeningResponse> responseByQuestionId,
        IReadOnlyDictionary<int, ScreeningQuestionScore> scoreByQuestionId)
    {
        var scorableQuestions = questions
            .Where(question => scoreByQuestionId[question.Id].IsScorable)
            .ToArray();
        var availableScore = scorableQuestions.Sum(question => scoreByQuestionId[question.Id].AvailableScore);
        var sequenceLengths = scorableQuestions
            .Select(question => GetWorkingMemorySequenceLength(question.MetadataJson))
            .ToArray();
        var hasConfiguredLengths = sequenceLengths.All(length => length is > 0);
        var availableItemCount = hasConfiguredLengths && sequenceLengths.Length > 0
            ? sequenceLengths.Sum(length => length!.Value)
            : (int?)null;
        var answeredQuestions = scorableQuestions
            .Where(question => responseByQuestionId.ContainsKey(question.Id))
            .ToArray();
        var answeredSequenceLengths = answeredQuestions
            .Select(question => GetWorkingMemorySequenceLength(question.MetadataJson))
            .ToArray();
        var hasAnsweredLengths = answeredSequenceLengths.All(length => length is > 0);
        var itemsAttempted = hasAnsweredLengths
            ? answeredSequenceLengths.Sum(length => length!.Value)
            : (int?)null;
        var itemsCorrect = hasAnsweredLengths
            ? answeredQuestions
                .Where(question => scoreByQuestionId[question.Id].IsCorrect == true)
                .Sum(question => GetWorkingMemorySequenceLength(question.MetadataJson)!.Value)
            : (int?)null;
        var itemsIncorrect = itemsAttempted is { } attempted && itemsCorrect is { } correct
            ? attempted - correct
            : (int?)null;
        var accuracy = itemsCorrect is { } correctItems && availableItemCount is > 0 && answeredQuestions.Length > 0
            ? CalculatePercentage(correctItems, availableItemCount.Value)
            : null;
        var highestCorrectSequenceLength = answeredQuestions
            .Where(question => scoreByQuestionId[question.Id].IsCorrect == true)
            .Select(question => GetWorkingMemorySequenceLength(question.MetadataJson))
            .Where(length => length is > 0)
            .Select(length => length!.Value)
            .DefaultIfEmpty()
            .Max();
        var hasCorrectSequence = answeredQuestions.Any(question => scoreByQuestionId[question.Id].IsCorrect == true);
        var percentage = answeredQuestions.Length > 0
            ? CalculatePercentage(
                scorableQuestions.Sum(question => scoreByQuestionId[question.Id].RawScore),
                availableScore)
            : null;

        return new ScreeningActivityMetric(
            ScreeningQuestionType.WorkingMemorySequence,
            "Working Memory",
            itemsAttempted,
            itemsCorrect,
            itemsIncorrect,
            availableItemCount,
            accuracy,
            hasCorrectSequence ? highestCorrectSequenceLength : null,
            null,
            null,
            null,
            null,
            availableScore,
            percentage);
    }

    private static ScreeningActivityMetric BuildProcessingSpeedMetric(
        IReadOnlyList<ScreeningQuestion> questions,
        IReadOnlyDictionary<int, ScreeningResponse> responseByQuestionId,
        IReadOnlyDictionary<int, ScreeningQuestionScore> scoreByQuestionId)
    {
        var scorableQuestions = questions
            .Where(question => scoreByQuestionId[question.Id].IsScorable)
            .ToArray();
        var availableScore = scorableQuestions.Sum(question => scoreByQuestionId[question.Id].AvailableScore);
        var aggregateRows = scorableQuestions
            .Where(question => responseByQuestionId.TryGetValue(question.Id, out var response) &&
                               TryGetTimedMatchingScore(response.ResponseDetailsJson, out _))
            .Select(question =>
            {
                var response = responseByQuestionId[question.Id];
                TryGetTimedMatchingScore(response.ResponseDetailsJson, out var score);
                return (Question: question, Response: response, Score: score);
            })
            .ToArray();
        var configuredItemCounts = scorableQuestions
            .Select(question => GetProcessingSpeedItemCount(question.MetadataJson))
            .ToArray();
        var availableItemCount = configuredItemCounts.All(count => count is > 0) && configuredItemCounts.Length > 0
            ? configuredItemCounts.Sum(count => count!.Value)
            : aggregateRows.Length > 0
                ? aggregateRows.Sum(row => row.Score.ItemCount)
                : (int?)null;
        var itemsAttempted = aggregateRows.Sum(row => row.Score.AttemptedCount);
        var itemsCorrect = aggregateRows.Sum(row => row.Score.CorrectCount);
        var itemsIncorrect = aggregateRows.Sum(row => row.Score.IncorrectCount);
        var hasResponses = aggregateRows.Length > 0;
        var elapsedTime = hasResponses
            ? aggregateRows.Sum(row => Math.Max(0L, row.Response.ResponseTimeMilliseconds ?? 0L))
            : (long?)null;
        var accuracy = hasResponses && availableItemCount is > 0
            ? CalculatePercentage(itemsCorrect, availableItemCount.Value)
            : null;
        var percentage = hasResponses
            ? CalculatePercentage(
                scorableQuestions.Sum(question => scoreByQuestionId[question.Id].RawScore),
                availableScore)
            : null;

        return new ScreeningActivityMetric(
            ScreeningQuestionType.TimedMatching,
            "Processing Speed",
            hasResponses ? itemsAttempted : 0,
            hasResponses ? itemsCorrect : 0,
            hasResponses ? itemsIncorrect : 0,
            availableItemCount,
            accuracy,
            null,
            elapsedTime,
            null,
            null,
            hasResponses ? itemsCorrect : 0,
            availableScore,
            percentage);
    }

    private static ScreeningActivityMetric BuildPatternReasoningMetric(
        IReadOnlyList<ScreeningQuestion> questions,
        IReadOnlyDictionary<int, ScreeningResponse> responseByQuestionId,
        IReadOnlyDictionary<int, ScreeningQuestionScore> scoreByQuestionId)
    {
        var scorableQuestions = questions
            .Where(question => scoreByQuestionId[question.Id].IsScorable)
            .ToArray();
        var availableScore = scorableQuestions.Sum(question => scoreByQuestionId[question.Id].AvailableScore);
        var answeredQuestions = scorableQuestions
            .Where(question => responseByQuestionId.ContainsKey(question.Id))
            .ToArray();
        var itemsAttempted = answeredQuestions.Length;
        var itemsCorrect = answeredQuestions.Count(question => scoreByQuestionId[question.Id].IsCorrect == true);
        var itemsIncorrect = answeredQuestions.Count(question => scoreByQuestionId[question.Id].IsCorrect == false);
        var accuracy = answeredQuestions.Length > 0 && scorableQuestions.Length > 0
            ? CalculatePercentage(itemsCorrect, scorableQuestions.Length)
            : null;
        var percentage = answeredQuestions.Length > 0
            ? CalculatePercentage(
                scorableQuestions.Sum(question => scoreByQuestionId[question.Id].RawScore),
                availableScore)
            : null;

        return new ScreeningActivityMetric(
            ScreeningQuestionType.PatternReasoning,
            "Pattern Reasoning",
            itemsAttempted,
            itemsCorrect,
            itemsIncorrect,
            scorableQuestions.Length == 0 ? null : scorableQuestions.Length,
            accuracy,
            null,
            null,
            null,
            null,
            answeredQuestions.Length == 0 ? 0m : itemsCorrect,
            availableScore,
            percentage);
    }

    private static int? GetWorkingMemorySequenceLength(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return null;
        }

        try
        {
            var metadata = JsonSerializer.Deserialize<ScreeningQuestionMetadata>(metadataJson, ResponseDetailsJsonOptions);
            var length = metadata?.WorkingMemorySequence?.Count(item => !string.IsNullOrWhiteSpace(item));
            return length is > 0 ? length : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int? GetProcessingSpeedItemCount(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return null;
        }

        try
        {
            var metadata = JsonSerializer.Deserialize<ScreeningQuestionMetadata>(metadataJson, ResponseDetailsJsonOptions);
            var configuredItems = metadata?.TimedMatchingItems?.Count(item => !string.IsNullOrWhiteSpace(item));
            if (configuredItems is > 0)
            {
                return configuredItems;
            }

            var referenceItems = metadata?.TimedMatchingReferenceMapping?.Count(pair =>
                !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value));
            return referenceItems is > 0 ? referenceItems : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private ScreeningQuestionScore ScoreQuestion(ScreeningQuestion question, ScreeningResponse? response)
    {
        if (!options.ObjectiveQuestionTypes.Contains(question.QuestionType) ||
            !TryGetPositiveWeight(question.Weight, out var weight))
        {
            return new ScreeningQuestionScore(question.Id, response is not null, false, null, 0m, 0m);
        }

        if (response is null)
        {
            return new ScreeningQuestionScore(question.Id, false, true, null, 0m, weight);
        }

        if (question.QuestionType == ScreeningQuestionType.TimedMatching &&
            TryGetTimedMatchingScore(response.ResponseDetailsJson, out var timedMatchingScore))
        {
            var rawScore = weight * timedMatchingScore.CorrectCount / timedMatchingScore.ItemCount;
            var isAggregateCorrect = timedMatchingScore.AttemptedCount == timedMatchingScore.ItemCount &&
                                     timedMatchingScore.CorrectCount == timedMatchingScore.ItemCount;
            return new ScreeningQuestionScore(question.Id, true, true, isAggregateCorrect, rawScore, weight);
        }

        var answerKeys = GetAnswerKeys(question);
        if (answerKeys.Count == 0)
        {
            return new ScreeningQuestionScore(question.Id, true, false, null, 0m, 0m);
        }

        var answer = GetSubmittedAnswer(question.QuestionType, response);
        if (string.IsNullOrWhiteSpace(answer))
        {
            return new ScreeningQuestionScore(question.Id, true, true, false, 0m, weight);
        }

        var normalizedAnswer = NormalizeAnswer(answer);
        var isCorrect = answerKeys.Any(key => string.Equals(
            normalizedAnswer,
            NormalizeAnswer(key),
            options.IgnoreAnswerCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));

        return new ScreeningQuestionScore(question.Id, true, true, isCorrect, isCorrect ? weight : 0m, weight);
    }

    private static bool TryGetTimedMatchingScore(string? responseDetailsJson, out TimedMatchingScore score)
    {
        score = default;
        if (string.IsNullOrWhiteSpace(responseDetailsJson))
        {
            return false;
        }

        try
        {
            var details = JsonSerializer.Deserialize<TimedMatchingResponseDetails>(
                responseDetailsJson,
                ResponseDetailsJsonOptions);
            if (details?.ItemCount is not > 0 || details.AttemptedCount is null ||
                details.CorrectCount is null || details.IncorrectCount is null)
            {
                return false;
            }

            if (details.AttemptedCount < 0 || details.CorrectCount < 0 || details.IncorrectCount < 0 ||
                details.AttemptedCount > details.ItemCount || details.CorrectCount > details.AttemptedCount ||
                details.IncorrectCount != details.AttemptedCount - details.CorrectCount)
            {
                return false;
            }

            score = new TimedMatchingScore(
                details.AttemptedCount.Value,
                details.CorrectCount.Value,
                details.IncorrectCount.Value,
                details.ItemCount.Value);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private List<string> GetAnswerKeys(ScreeningQuestion question)
    {
        var answerKeys = new List<string>();
        if (!string.IsNullOrWhiteSpace(question.CorrectAnswer))
        {
            answerKeys.Add(question.CorrectAnswer);
        }

        if (string.IsNullOrWhiteSpace(question.AcceptedAnswersJson))
        {
            return answerKeys;
        }

        try
        {
            using var document = JsonDocument.Parse(question.AcceptedAnswersJson);
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                answerKeys.AddRange(document.RootElement.EnumerateArray()
                    .Where(value => value.ValueKind == JsonValueKind.String)
                    .Select(value => value.GetString())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value!));
            }
        }
        catch (JsonException)
        {
            // A usable CorrectAnswer remains valid if the optional aliases are malformed.
        }

        return answerKeys;
    }

    private static string? GetSubmittedAnswer(ScreeningQuestionType questionType, ScreeningResponse response) =>
        questionType is ScreeningQuestionType.MultipleChoice or
            ScreeningQuestionType.PhonologicalChoice or
            ScreeningQuestionType.TimedMatching or
            ScreeningQuestionType.PatternReasoning
            ? response.SelectedAnswer
            : response.TextAnswer;

    private string NormalizeAnswer(string answer) =>
        options.TrimAnswerWhitespace ? answer.Trim() : answer;

    private string? GetFlaggedSkillArea(string testName, decimal? percentage)
    {
        if (percentage is null || options.FlagSkillAreaBelowPercentage is null ||
            percentage >= options.FlagSkillAreaBelowPercentage)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(testName) ? null : testName.Trim();
    }

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(options.ScoringVersion) || options.ScoringVersion.Trim().Length > 64)
        {
            throw new InvalidOperationException("The scoring version must contain between 1 and 64 characters.");
        }

        if (options.FlagSkillAreaBelowPercentage is < 0m or > 100m)
        {
            throw new InvalidOperationException("The skill-area review threshold must be between 0 and 100, or null.");
        }

        if (options.ObjectiveQuestionTypes is null)
        {
            throw new InvalidOperationException("The objective question type list cannot be null.");
        }
    }

    private static bool TryGetPositiveWeight(double configuredWeight, out decimal weight)
    {
        weight = 0m;
        if (!double.IsFinite(configuredWeight) || configuredWeight <= 0)
        {
            return false;
        }

        try
        {
            weight = (decimal)configuredWeight;
            return weight > 0m;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static decimal? CalculatePercentage(decimal rawScore, decimal availableScore) =>
        availableScore <= 0m
            ? null
            : decimal.Round(rawScore / availableScore * 100m, 2, MidpointRounding.AwayFromZero);

    private static ScreeningCompletionStatus GetCompletionStatus(int answeredCount, int expectedCount)
    {
        if (expectedCount <= 0 || answeredCount <= 0)
        {
            return ScreeningCompletionStatus.NotStarted;
        }

        return answeredCount >= expectedCount
            ? ScreeningCompletionStatus.Completed
            : ScreeningCompletionStatus.InProgress;
    }

    private sealed record TimedMatchingResponseDetails(
        int? TimeLimitSeconds,
        bool TimeLimitReached,
        int? AttemptedCount,
        int? CorrectCount,
        int? IncorrectCount,
        int? ItemCount);

    private readonly record struct TimedMatchingScore(
        int AttemptedCount,
        int CorrectCount,
        int IncorrectCount,
        int ItemCount);
}
