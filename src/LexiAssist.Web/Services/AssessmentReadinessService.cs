using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Screening;
using LexiAssist.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace LexiAssist.Web.Services;

public sealed class AssessmentReadinessService(
    ApplicationDbContext dbContext,
    IWebHostEnvironment environment) : IAssessmentReadinessService
{
    private static readonly JsonSerializerOptions MetadataOptions = new(JsonSerializerDefaults.Web);

    public async Task<AssessmentReadinessResult?> GetReadinessAsync(
        int screeningTestId,
        CancellationToken cancellationToken = default)
    {
        var test = await dbContext.ScreeningTests.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == screeningTestId, cancellationToken);
        if (test is null)
        {
            return null;
        }

        var questions = await dbContext.ScreeningQuestions.AsNoTracking()
            .Where(question => question.ScreeningTestId == screeningTestId)
            .OrderBy(question => question.SortOrder)
            .ThenBy(question => question.Id)
            .ToListAsync(cancellationToken);

        var checks = new List<AssessmentReadinessCheck>();
        var questionViews = new List<AssessmentReadinessQuestion>();
        var activeQuestions = questions.Where(question => question.IsActive).ToList();
        var editDetailsUrl = $"/admin/screening-tests/{screeningTestId}/edit";
        var builderUrl = $"/admin/screening-tests/{screeningTestId}/builder";

        checks.Add(new AssessmentReadinessCheck(
            "assessment-name",
            "Assessment has a name",
            string.IsNullOrWhiteSpace(test.Name) ? AssessmentReadinessSeverity.Blocking : AssessmentReadinessSeverity.Passed,
            string.IsNullOrWhiteSpace(test.Name) ? "Enter an assessment name." : null,
            string.IsNullOrWhiteSpace(test.Name) ? editDetailsUrl : null,
            string.IsNullOrWhiteSpace(test.Name) ? "Edit details" : null));
        checks.Add(new AssessmentReadinessCheck(
            "questions-present",
            "Questions configured",
            questions.Count == 0 ? AssessmentReadinessSeverity.Blocking : AssessmentReadinessSeverity.Passed,
            questions.Count == 0 ? "Add at least one question." : $"{questions.Count} question{(questions.Count == 1 ? string.Empty : "s")} in the assessment.",
            questions.Count == 0 ? builderUrl : null,
            questions.Count == 0 ? "Go to Builder" : null));
        checks.Add(new AssessmentReadinessCheck(
            "active-questions",
            "Active questions available",
            activeQuestions.Count == 0 ? AssessmentReadinessSeverity.Blocking : AssessmentReadinessSeverity.Passed,
            activeQuestions.Count == 0 ? "Add and activate at least one question before publishing this assessment." : $"{activeQuestions.Count} active question{(activeQuestions.Count == 1 ? string.Empty : "s")} will appear to learners.",
            activeQuestions.Count == 0 ? builderUrl : null,
            activeQuestions.Count == 0 ? "Go to Builder" : null));

        foreach (var question in questions)
        {
            var typeName = GetQuestionTypeName(question.QuestionType);
            var editUrl = $"/admin/screening-tests/{screeningTestId}/questions/{question.Id}/edit?returnToBuilder=true";
            if (!question.IsActive)
            {
                questionViews.Add(new AssessmentReadinessQuestion(
                    question.Id,
                    question.SortOrder,
                    question.Prompt,
                    typeName,
                    false,
                    AssessmentReadinessSeverity.Warning,
                    ["Inactive draft; it will not be shown to learners."],
                    editUrl));
                continue;
            }

            var issues = ValidateActiveQuestion(question);
            var severity = issues.Count == 0 ? AssessmentReadinessSeverity.Passed : AssessmentReadinessSeverity.Blocking;
            var label = $"Question {question.SortOrder}: {typeName}";
            checks.Add(new AssessmentReadinessCheck(
                $"question-{question.Id}-ownership",
                $"{label} belongs to this assessment",
                question.ScreeningTestId == screeningTestId ? AssessmentReadinessSeverity.Passed : AssessmentReadinessSeverity.Blocking,
                question.ScreeningTestId == screeningTestId ? null : "This question is assigned to a different assessment.",
                question.ScreeningTestId == screeningTestId ? null : builderUrl,
                question.ScreeningTestId == screeningTestId ? null : "Go to Builder"));
            checks.Add(new AssessmentReadinessCheck(
                $"question-{question.Id}-validity",
                $"{label} is ready",
                severity,
                issues.Count == 0 ? "Active question is configured for the learner runtime." : string.Join(" ", issues),
                issues.Count == 0 ? null : editUrl,
                issues.Count == 0 ? null : "Fix question"));
            questionViews.Add(new AssessmentReadinessQuestion(
                question.Id,
                question.SortOrder,
                question.Prompt,
                typeName,
                true,
                severity,
                issues,
                editUrl));
        }

        var inactiveCount = questions.Count - activeQuestions.Count;
        if (inactiveCount > 0)
        {
            checks.Add(new AssessmentReadinessCheck(
                "inactive-drafts",
                $"{inactiveCount} inactive draft{(inactiveCount == 1 ? string.Empty : "s")} excluded from learners",
                AssessmentReadinessSeverity.Warning,
                "Inactive drafts stay out of the learner assessment until you activate them.",
                builderUrl,
                "Review drafts in Builder"));
        }

        var typeCounts = questions
            .GroupBy(question => question.QuestionType)
            .Select(group => new AssessmentQuestionTypeCount(GetQuestionTypeName(group.Key), group.Count()))
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        return new AssessmentReadinessResult
        {
            ScreeningTestId = test.Id,
            Name = test.Name,
            Slug = test.Slug,
            Description = test.Description,
            Instructions = test.Instructions,
            IsActive = test.IsActive,
            TotalQuestionCount = questions.Count,
            ActiveQuestionCount = activeQuestions.Count,
            Questions = questionViews,
            Checks = checks,
            QuestionTypeCounts = typeCounts
        };
    }

    private List<string> ValidateActiveQuestion(ScreeningQuestion question)
    {
        var issues = new List<string>();
        if (string.IsNullOrWhiteSpace(question.Prompt))
        {
            issues.Add("Question prompt is missing.");
        }

        if (!Enum.IsDefined(question.QuestionType))
        {
            issues.Add("Question type is not supported by the learner runtime.");
            return issues;
        }

        var metadata = new ScreeningQuestionMetadata();
        if (!string.IsNullOrWhiteSpace(question.MetadataJson))
        {
            try
            {
                metadata = JsonSerializer.Deserialize<ScreeningQuestionMetadata>(question.MetadataJson, MetadataOptions)
                           ?? new ScreeningQuestionMetadata();
            }
            catch (JsonException)
            {
                issues.Add("Question settings could not be read. Reopen the question editor and save it.");
                return issues;
            }
        }

        switch (question.QuestionType)
        {
            case ScreeningQuestionType.MultipleChoice:
            case ScreeningQuestionType.PhonologicalChoice:
                ValidateChoices(question, metadata, issues, requireCorrectAnswer: true);
                break;
            case ScreeningQuestionType.TextResponse:
            case ScreeningQuestionType.WordReading:
            case ScreeningQuestionType.NonWordReading:
            case ScreeningQuestionType.Spelling:
                break;
            case ScreeningQuestionType.TimedNaming:
                if (metadata.TimedNamingItems?.Count(item => !string.IsNullOrWhiteSpace(item)) != 20)
                {
                    issues.Add("Timed naming needs exactly 20 presentation items.");
                }
                if (metadata.TimeLimitSeconds is < 1 or > 600)
                {
                    issues.Add("Timed naming needs a time limit from 1 to 600 seconds.");
                }
                break;
            case ScreeningQuestionType.ReadingPassage:
                if (string.IsNullOrWhiteSpace(metadata.PassageText))
                {
                    issues.Add("Reading passage is missing.");
                }
                break;
            case ScreeningQuestionType.ListeningResponse:
                ValidateListening(question, metadata, issues);
                break;
            case ScreeningQuestionType.ReadingFluency:
                ValidatePassageMetadata(metadata, issues, "Reading fluency passage");
                var wordCount = metadata.ReferenceWordCount ?? CountWords(metadata.PassageText);
                if (wordCount is < 1 or > 10000)
                {
                    issues.Add("Reading fluency needs a passage with 1 to 10,000 reference words.");
                }
                if (metadata.TargetWordsPerMinute is < 1 or > 1000)
                {
                    issues.Add("Optional target pace must be from 1 to 1,000 words per minute.");
                }
                break;
            case ScreeningQuestionType.WorkingMemorySequence:
                var sequenceCount = metadata.WorkingMemorySequence?.Count(item => !string.IsNullOrWhiteSpace(item)) ?? 0;
                if (sequenceCount is < 2 or > 12)
                {
                    issues.Add("Working memory sequence needs 2 to 12 items.");
                }
                if (metadata.MemoryDisplayDurationMilliseconds is < 1000 or > 30000)
                {
                    issues.Add("Sequence display duration must be from 1,000 to 30,000 milliseconds.");
                }
                if (string.IsNullOrWhiteSpace(question.CorrectAnswer))
                {
                    issues.Add("Expected sequence answer is missing.");
                }
                break;
            case ScreeningQuestionType.TimedMatching:
                ValidateTimedMatching(question, metadata, issues);
                break;
            case ScreeningQuestionType.PatternReasoning:
                ValidateChoices(question, metadata, issues, requireCorrectAnswer: true);
                if (metadata.PatternDifficultyLevel is < 1 or > 5)
                {
                    issues.Add("Pattern difficulty must be from 1 to 5 when provided.");
                }
                break;
        }

        return issues.Distinct(StringComparer.Ordinal).ToList();
    }

    private static void ValidateChoices(ScreeningQuestion question, ScreeningQuestionMetadata metadata, List<string> issues, bool requireCorrectAnswer)
    {
        var options = metadata.ChoiceOptions?.Where(option => !string.IsNullOrWhiteSpace(option)).Select(option => option.Trim()).ToArray() ?? [];
        if (options.Distinct(StringComparer.OrdinalIgnoreCase).Count() < 2)
        {
            issues.Add("Add at least two different answer choices.");
        }
        if (requireCorrectAnswer && (string.IsNullOrWhiteSpace(question.CorrectAnswer) ||
                                     !options.Contains(question.CorrectAnswer.Trim(), StringComparer.OrdinalIgnoreCase)))
        {
            issues.Add("Correct answer does not match an option.");
        }
    }

    private void ValidateListening(ScreeningQuestion question, ScreeningQuestionMetadata metadata, List<string> issues)
    {
        if (!ScreeningAudioSource.TryGetContentType(question.AudioPath, out _, out _))
        {
            issues.Add("Listening stimulus is missing or uses an unsupported audio file name.");
        }
        else if (!ScreeningAudioSource.TryResolve(question.AudioPath, environment.ContentRootPath, out _, out _))
        {
            issues.Add("Listening stimulus file is missing or unavailable.");
        }

        if (metadata.MaxAudioReplays is < 0 or > 20)
        {
            issues.Add("Maximum audio replay setting must be from 0 to 20.");
        }

        var responseType = string.IsNullOrWhiteSpace(metadata.ResponseType) ? "multiple-choice" : metadata.ResponseType.Trim().ToLowerInvariant();
        switch (responseType)
        {
            case "multiple-choice":
                ValidateChoices(question, metadata, issues, requireCorrectAnswer: true);
                break;
            case "text-response":
                if (string.IsNullOrWhiteSpace(question.CorrectAnswer))
                {
                    issues.Add("Expected listening response is missing.");
                }
                break;
            case "audio-response":
                if (!question.AllowsAudioRecording)
                {
                    issues.Add("Enable learner audio recording for an audio-response question.");
                }
                break;
            default:
                issues.Add("Choose a supported listening response type.");
                break;
        }
    }

    private static void ValidateTimedMatching(ScreeningQuestion question, ScreeningQuestionMetadata metadata, List<string> issues)
    {
        if (metadata.TimeLimitSeconds is < 1 or > 600)
        {
            issues.Add("Timed matching needs a time limit from 1 to 600 seconds.");
        }

        var choices = metadata.ChoiceOptions?.Where(option => !string.IsNullOrWhiteSpace(option)).Select(option => option.Trim()).ToArray() ?? [];
        if (choices.Distinct(StringComparer.OrdinalIgnoreCase).Count() < 2)
        {
            issues.Add("Timed matching needs at least two different answer symbols.");
        }

        var mapping = metadata.TimedMatchingReferenceMapping;
        var validMapping = mapping is { Count: >= 2 } && mapping.All(pair =>
            !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value) &&
            choices.Contains(pair.Value.Trim(), StringComparer.OrdinalIgnoreCase));
        if (!validMapping)
        {
            issues.Add("Timed matching needs at least two valid reference pairs that use the answer symbols.");
            return;
        }

        var items = metadata.TimedMatchingItems?.Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Trim()).ToArray();
        if (items is { Length: > 50 } || items is { Length: > 0 } && items.Any(item => !mapping!.ContainsKey(item)))
        {
            issues.Add("Each timed matching item must use a reference pair (up to 50 items).");
        }
        else if (items is { Length: 0 })
        {
            issues.Add("Add at least one timed matching item.");
        }
    }

    private static void ValidatePassageMetadata(ScreeningQuestionMetadata metadata, List<string> issues, string label)
    {
        if (string.IsNullOrWhiteSpace(metadata.PassageText))
        {
            issues.Add($"{label} is missing.");
        }
    }

    private static int CountWords(string? text) => string.IsNullOrWhiteSpace(text)
        ? 0
        : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    private static string GetQuestionTypeName(ScreeningQuestionType questionType) =>
        typeof(ScreeningQuestionType).GetMember(questionType.ToString()).FirstOrDefault()?
            .GetCustomAttribute<DisplayAttribute>()?.GetName() ?? questionType.ToString();
}
