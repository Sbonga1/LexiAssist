using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Audit;
using LexiAssist.Web.Models.Identity;
using LexiAssist.Web.Models.Screening;
using LexiAssist.Web.Services;
using LexiAssist.Web.Services.Interfaces;
using LexiAssist.Web.ViewModels.AdminScreeningTests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LexiAssist.Web.Controllers;

[Authorize(Roles = ApplicationRoles.Administrator)]
[Route("admin/screening-tests/{testId:int}/questions")]
public sealed class AdminScreeningQuestionsController(ApplicationDbContext dbContext, IAuditService auditService) : Controller
{
    private static readonly JsonSerializerOptions MetadataJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [HttpGet("")]
    public async Task<IActionResult> Index(int testId)
    {
        var screeningTest = await dbContext.ScreeningTests
            .AsNoTracking()
            .Where(test => test.Id == testId)
            .Select(test => new { test.Id, test.Name })
            .SingleOrDefaultAsync();

        if (screeningTest is null)
        {
            return NotFound();
        }

        var questions = await dbContext.ScreeningQuestions
            .AsNoTracking()
            .Where(question => question.ScreeningTestId == testId)
            .OrderBy(question => question.SortOrder)
            .ThenBy(question => question.Id)
            .Select(question => new AdminScreeningQuestionListItemViewModel(
                question.Id,
                question.Prompt,
                GetQuestionTypeDisplayName(question.QuestionType),
                question.SortOrder,
                question.IsActive))
            .ToListAsync();

        return View(new AdminScreeningQuestionsIndexViewModel(screeningTest.Id, screeningTest.Name, questions));
    }

    [HttpGet("create")]
    public async Task<IActionResult> Create(int testId)
    {
        var screeningTest = await FindScreeningTestAsync(testId);
        if (screeningTest is null)
        {
            return NotFound();
        }

        SetTestContext(screeningTest);
        return View(new AdminScreeningQuestionFormViewModel());
    }

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(int testId, AdminScreeningQuestionFormViewModel model)
    {
        var screeningTest = await FindScreeningTestAsync(testId);
        if (screeningTest is null)
        {
            return NotFound();
        }

        SetTestContext(screeningTest);
        ValidateQuestionSpecificFields(model);
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var question = new ScreeningQuestion
        {
            ScreeningTestId = screeningTest.Id,
            Prompt = model.Prompt.Trim(),
            QuestionType = model.QuestionType,
            CorrectAnswer = NormalizeOptionalText(GetCorrectAnswer(model)),
            AcceptedAnswersJson = SerializeAcceptedAnswers(model),
            Weight = 1.0,
            SortOrder = (await dbContext.ScreeningQuestions
                .Where(item => item.ScreeningTestId == screeningTest.Id)
                .MaxAsync(item => (int?)item.SortOrder) ?? 0) + 1,
            IsActive = true,
            AudioPath = GetAudioPath(model),
            ImagePath = GetImagePath(model),
            MetadataJson = SerializeMetadata(model),
            AllowsAudioRecording = model.AllowsAudioRecording
        };

        dbContext.ScreeningQuestions.Add(question);
        await dbContext.SaveChangesAsync();
        await auditService.RecordAsync(AuditActions.ScreeningQuestionCreated, AuditEntityTypes.ScreeningQuestion, question.Id.ToString(), true,
            new { question.ScreeningTestId, QuestionType = question.QuestionType.ToString() });

        TempData["StatusMessage"] = "The question was created.";
        return RedirectToAction(nameof(Index), new { testId = screeningTest.Id });
    }

    [HttpGet("{questionId:int}/edit")]
    public async Task<IActionResult> Edit(int testId, int questionId)
    {
        var question = await FindQuestionAsync(testId, questionId, includeTest: true);
        if (question is null)
        {
            return NotFound();
        }

        SetTestContext(question.ScreeningTest);
        return View(ToFormViewModel(question));
    }

    [HttpPost("{questionId:int}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int testId, int questionId, AdminScreeningQuestionFormViewModel model)
    {
        if (questionId != model.Id)
        {
            return BadRequest();
        }

        var question = await FindQuestionAsync(testId, questionId, includeTest: true);
        if (question is null)
        {
            return NotFound();
        }

        SetTestContext(question.ScreeningTest);
        ValidateQuestionSpecificFields(model);
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        question.Prompt = model.Prompt.Trim();
        question.QuestionType = model.QuestionType;
        question.CorrectAnswer = NormalizeOptionalText(GetCorrectAnswer(model));
        question.AcceptedAnswersJson = SerializeAcceptedAnswers(model);
        question.AudioPath = GetAudioPath(model);
        question.ImagePath = GetImagePath(model);
        question.MetadataJson = SerializeMetadata(model);
        question.AllowsAudioRecording = model.AllowsAudioRecording;
        await dbContext.SaveChangesAsync();
        await auditService.RecordAsync(AuditActions.ScreeningQuestionUpdated, AuditEntityTypes.ScreeningQuestion, question.Id.ToString(), true,
            new { question.ScreeningTestId, QuestionType = question.QuestionType.ToString() });

        TempData["StatusMessage"] = "The question was updated.";
        return RedirectToAction(nameof(Index), new { testId });
    }

    [HttpPost("{questionId:int}/activate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Activate(int testId, int questionId)
    {
        var question = await FindQuestionAsync(testId, questionId);
        if (question is null)
        {
            return NotFound();
        }

        question.IsActive = true;
        await dbContext.SaveChangesAsync();
        await auditService.RecordAsync(AuditActions.ScreeningQuestionActivated, AuditEntityTypes.ScreeningQuestion, questionId.ToString(), true,
            new { ScreeningTestId = testId });
        TempData["StatusMessage"] = "The question was activated.";
        return RedirectToAction(nameof(Index), new { testId });
    }

    [HttpPost("{questionId:int}/deactivate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deactivate(int testId, int questionId)
    {
        var question = await FindQuestionAsync(testId, questionId);
        if (question is null)
        {
            return NotFound();
        }

        question.IsActive = false;
        await dbContext.SaveChangesAsync();
        await auditService.RecordAsync(AuditActions.ScreeningQuestionDeactivated, AuditEntityTypes.ScreeningQuestion, questionId.ToString(), true,
            new { ScreeningTestId = testId });
        TempData["StatusMessage"] = "The question was deactivated. Its record is preserved.";
        return RedirectToAction(nameof(Index), new { testId });
    }

    [HttpPost("{questionId:int}/move")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Move(int testId, int questionId, string direction)
    {
        if (direction is not "up" and not "down")
        {
            return BadRequest();
        }

        var questions = await dbContext.ScreeningQuestions
            .Where(question => question.ScreeningTestId == testId)
            .OrderBy(question => question.SortOrder)
            .ThenBy(question => question.Id)
            .ToListAsync();
        var currentIndex = questions.FindIndex(question => question.Id == questionId);
        if (currentIndex < 0)
        {
            return NotFound();
        }

        var destinationIndex = direction == "up" ? currentIndex - 1 : currentIndex + 1;
        if (destinationIndex >= 0 && destinationIndex < questions.Count)
        {
            var question = questions[currentIndex];
            questions.RemoveAt(currentIndex);
            questions.Insert(destinationIndex, question);

            for (var index = 0; index < questions.Count; index++)
            {
                questions[index].SortOrder = index + 1;
            }

            await dbContext.SaveChangesAsync();
            await auditService.RecordAsync(AuditActions.ScreeningQuestionReordered, AuditEntityTypes.ScreeningQuestion, questionId.ToString(), true,
                new { ScreeningTestId = testId, Direction = direction });
            TempData["StatusMessage"] = "The question order was updated.";
        }

        return RedirectToAction(nameof(Index), new { testId });
    }

    private Task<ScreeningTest?> FindScreeningTestAsync(int testId) =>
        dbContext.ScreeningTests.AsNoTracking().SingleOrDefaultAsync(test => test.Id == testId);

    private Task<ScreeningQuestion?> FindQuestionAsync(int testId, int questionId, bool includeTest = false)
    {
        IQueryable<ScreeningQuestion> questions = dbContext.ScreeningQuestions;
        if (includeTest)
        {
            questions = questions.Include(question => question.ScreeningTest);
        }

        return questions.SingleOrDefaultAsync(question =>
            question.Id == questionId && question.ScreeningTestId == testId);
    }

    private void ValidateQuestionSpecificFields(AdminScreeningQuestionFormViewModel model)
    {
        var choiceOptions = ParseLines(model.ChoiceOptionsText);
        var isTimedMatching = model.QuestionType == ScreeningQuestionType.TimedMatching;
        if (model.QuestionType is ScreeningQuestionType.MultipleChoice or
            ScreeningQuestionType.PhonologicalChoice or
            ScreeningQuestionType.PatternReasoning ||
            isTimedMatching)
        {
            if (choiceOptions.Distinct(StringComparer.OrdinalIgnoreCase).Count() < 2)
            {
                ModelState.AddModelError(nameof(model.ChoiceOptionsText), "Enter at least two different answer choices.");
            }

            var correctAnswer = model.CorrectAnswer?.Trim();
            if (!isTimedMatching || string.IsNullOrWhiteSpace(model.TimedMatchingReferenceMappingText))
            {
                if (string.IsNullOrWhiteSpace(correctAnswer) ||
                    !choiceOptions.Contains(correctAnswer, StringComparer.OrdinalIgnoreCase))
                {
                    ModelState.AddModelError(nameof(model.CorrectAnswer), "Choose a correct answer from the answer choices.");
                }
            }
        }

        if (model.QuestionType is ScreeningQuestionType.TimedNaming or ScreeningQuestionType.TimedMatching &&
            model.TimeLimitSeconds is null)
        {
            ModelState.AddModelError(nameof(model.TimeLimitSeconds), "Enter a time limit for this timed activity.");
        }

        if (isTimedMatching)
        {
            var mappingLines = ParseLines(model.TimedMatchingReferenceMappingText);
            var mapping = ParseReferenceMapping(model.TimedMatchingReferenceMappingText);
            if (mappingLines.Count < 2)
            {
                ModelState.AddModelError(nameof(model.TimedMatchingReferenceMappingText), "Enter at least two reference pairs, one per line.");
            }

            if (mappingLines.Any(line => !TryParseReferenceMappingLine(line, out _, out _)))
            {
                ModelState.AddModelError(nameof(model.TimedMatchingReferenceMappingText), "Use one item and symbol per line, separated by =.");
            }

            if (mapping.Count != mappingLines.Count)
            {
                ModelState.AddModelError(nameof(model.TimedMatchingReferenceMappingText), "Each reference item must be unique.");
            }

            if (mapping.Values.Any(symbol => !choiceOptions.Contains(symbol, StringComparer.OrdinalIgnoreCase)))
            {
                ModelState.AddModelError(nameof(model.TimedMatchingReferenceMappingText), "Each mapped symbol must be included in the answer choices.");
            }

            var matchingItems = ParseLines(model.TimedMatchingItemsText);
            if (matchingItems.Count > 50)
            {
                ModelState.AddModelError(nameof(model.TimedMatchingItemsText), "Enter no more than 50 matching items.");
            }

            if (matchingItems.Any(item => !mapping.ContainsKey(item)))
            {
                ModelState.AddModelError(nameof(model.TimedMatchingItemsText), "Each matching item must appear in the reference mapping.");
            }

        }

        if (model.QuestionType == ScreeningQuestionType.TimedNaming)
        {
            var namingItems = ParseLines(model.TimedNamingItemsText);
            if (namingItems.Count != 20)
            {
                ModelState.AddModelError(nameof(model.TimedNamingItemsText), "Enter exactly 20 naming items, one per line.");
            }

            if (namingItems.Any(item => item.Length > 80))
            {
                ModelState.AddModelError(nameof(model.TimedNamingItemsText), "Each naming item must be 80 characters or fewer.");
            }
        }

        if (model.QuestionType == ScreeningQuestionType.WorkingMemorySequence)
        {
            var sequenceItems = ParseLines(model.WorkingMemorySequenceText);
            if (sequenceItems.Count is < 2 or > 12)
            {
                ModelState.AddModelError(nameof(model.WorkingMemorySequenceText), "Enter between 2 and 12 sequence items, one per line.");
            }

            if (sequenceItems.Any(item => item.Length > 80))
            {
                ModelState.AddModelError(nameof(model.WorkingMemorySequenceText), "Each sequence item must be 80 characters or fewer.");
            }

            if (model.MemoryDisplayDurationMilliseconds is null)
            {
                ModelState.AddModelError(nameof(model.MemoryDisplayDurationMilliseconds), "Enter a sequence display duration.");
            }

            if (string.IsNullOrWhiteSpace(model.CorrectAnswer))
            {
                ModelState.AddModelError(nameof(model.CorrectAnswer), "Enter the expected sequence answer.");
            }
        }

        if (model.QuestionType is ScreeningQuestionType.ReadingPassage or ScreeningQuestionType.ReadingFluency &&
            string.IsNullOrWhiteSpace(model.PassageText))
        {
            ModelState.AddModelError(nameof(model.PassageText), "Enter the passage text.");
        }

        if (model.QuestionType == ScreeningQuestionType.ListeningResponse && string.IsNullOrWhiteSpace(model.AudioPath))
        {
            ModelState.AddModelError(nameof(model.AudioPath), "Enter an audio file name for a listening comprehension question.");
        }
        else if (model.QuestionType == ScreeningQuestionType.ListeningResponse &&
                 !ScreeningAudioSource.TryGetContentType(model.AudioPath, out _, out _))
        {
            ModelState.AddModelError(nameof(model.AudioPath), "Use a supported audio file name such as story.mp3. Folder paths and URLs are not allowed.");
        }
    }

    private static string? GetCorrectAnswer(AdminScreeningQuestionFormViewModel model)
    {
        var answer = NormalizeOptionalText(model.CorrectAnswer);
        if (answer is null)
        {
            return null;
        }

        return model.QuestionType switch
        {
            ScreeningQuestionType.MultipleChoice or
            ScreeningQuestionType.PhonologicalChoice or
            ScreeningQuestionType.PatternReasoning =>
                ParseLines(model.ChoiceOptionsText)
                    .FirstOrDefault(option => string.Equals(option, answer, StringComparison.OrdinalIgnoreCase)),
            ScreeningQuestionType.TextResponse or
            ScreeningQuestionType.WordReading or
            ScreeningQuestionType.NonWordReading or
            ScreeningQuestionType.Spelling or
            ScreeningQuestionType.WorkingMemorySequence => answer,
            _ => null
        };
    }

    private static string? SerializeAcceptedAnswers(AdminScreeningQuestionFormViewModel model)
    {
        if (model.QuestionType is not (ScreeningQuestionType.TextResponse or
            ScreeningQuestionType.WordReading or
            ScreeningQuestionType.NonWordReading or
            ScreeningQuestionType.Spelling or
            ScreeningQuestionType.ReadingPassage or
            ScreeningQuestionType.ListeningResponse or
            ScreeningQuestionType.WorkingMemorySequence))
        {
            return null;
        }

        var acceptedAnswers = ParseLines(model.AcceptedAnswersText)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return acceptedAnswers.Count == 0 ? null : JsonSerializer.Serialize(acceptedAnswers);
    }

    private static string? GetAudioPath(AdminScreeningQuestionFormViewModel model) =>
        model.QuestionType is ScreeningQuestionType.WordReading or
            ScreeningQuestionType.NonWordReading or
            ScreeningQuestionType.ListeningResponse
            ? NormalizeOptionalText(model.AudioPath)
            : null;

    private static string? GetImagePath(AdminScreeningQuestionFormViewModel model) =>
        model.QuestionType is ScreeningQuestionType.MultipleChoice or
            ScreeningQuestionType.PhonologicalChoice or
            ScreeningQuestionType.TimedNaming or
            ScreeningQuestionType.PatternReasoning
            ? NormalizeOptionalText(model.ImagePath)
            : null;

    private static string? SerializeMetadata(AdminScreeningQuestionFormViewModel model)
    {
        var metadata = new ScreeningQuestionMetadata
        {
            ChoiceOptions = model.QuestionType is ScreeningQuestionType.MultipleChoice or
                ScreeningQuestionType.PhonologicalChoice or
                ScreeningQuestionType.TimedMatching or
                ScreeningQuestionType.PatternReasoning
                ? ParseLines(model.ChoiceOptionsText).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                : null,
            PassageText = model.QuestionType is ScreeningQuestionType.ReadingPassage or ScreeningQuestionType.ReadingFluency
                ? NormalizeOptionalText(model.PassageText)
                : null,
            ReferenceWordCount = model.QuestionType == ScreeningQuestionType.ReadingFluency
                ? model.ReferenceWordCount
                : null,
            TargetWordsPerMinute = model.QuestionType == ScreeningQuestionType.ReadingFluency
                ? model.TargetWordsPerMinute
                : null,
            MaxAudioReplays = model.QuestionType == ScreeningQuestionType.ListeningResponse
                ? model.MaxAudioReplays
                : null,
            TimeLimitSeconds = model.QuestionType is ScreeningQuestionType.TimedNaming or ScreeningQuestionType.TimedMatching
                ? model.TimeLimitSeconds
                : null,
            TimedMatchingReferenceMapping = model.QuestionType == ScreeningQuestionType.TimedMatching &&
                !string.IsNullOrWhiteSpace(model.TimedMatchingReferenceMappingText)
                ? ParseReferenceMapping(model.TimedMatchingReferenceMappingText)
                : null,
            TimedMatchingItems = model.QuestionType == ScreeningQuestionType.TimedMatching
                ? ParseLines(model.TimedMatchingItemsText)
                : null,
            PatternDifficultyLevel = model.QuestionType == ScreeningQuestionType.PatternReasoning
                ? model.PatternDifficultyLevel
                : null,
            TimedNamingItems = model.QuestionType == ScreeningQuestionType.TimedNaming
                ? ParseLines(model.TimedNamingItemsText)
                : null,
            WorkingMemorySequence = model.QuestionType == ScreeningQuestionType.WorkingMemorySequence
                ? ParseLines(model.WorkingMemorySequenceText)
                : null,
            MemoryDisplayDurationMilliseconds = model.QuestionType == ScreeningQuestionType.WorkingMemorySequence
                ? model.MemoryDisplayDurationMilliseconds
                : null
        };

        return metadata.ChoiceOptions is null && metadata.PassageText is null && metadata.ReferenceWordCount is null &&
               metadata.TargetWordsPerMinute is null && metadata.MaxAudioReplays is null &&
               metadata.TimeLimitSeconds is null && metadata.TimedMatchingReferenceMapping is null &&
               metadata.TimedMatchingItems is null && metadata.TimedNamingItems is null &&
               metadata.WorkingMemorySequence is null && metadata.MemoryDisplayDurationMilliseconds is null &&
               metadata.PatternDifficultyLevel is null
            ? null
            : JsonSerializer.Serialize(metadata, MetadataJsonOptions);
    }

    private static AdminScreeningQuestionFormViewModel ToFormViewModel(ScreeningQuestion question)
    {
        var metadata = DeserializeMetadata(question.MetadataJson);
        var acceptedAnswers = DeserializeAcceptedAnswers(question.AcceptedAnswersJson);

        return new AdminScreeningQuestionFormViewModel
        {
            Id = question.Id,
            Prompt = question.Prompt,
            QuestionType = question.QuestionType,
            CorrectAnswer = question.CorrectAnswer,
            AcceptedAnswersText = acceptedAnswers is null ? null : string.Join(Environment.NewLine, acceptedAnswers),
            ChoiceOptionsText = metadata.ChoiceOptions is null ? null : string.Join(Environment.NewLine, metadata.ChoiceOptions),
            PassageText = metadata.PassageText,
            ReferenceWordCount = metadata.ReferenceWordCount,
            TargetWordsPerMinute = metadata.TargetWordsPerMinute,
            MaxAudioReplays = metadata.MaxAudioReplays,
            TimeLimitSeconds = metadata.TimeLimitSeconds,
            TimedMatchingReferenceMappingText = metadata.TimedMatchingReferenceMapping is null
                ? null
                : string.Join(Environment.NewLine, metadata.TimedMatchingReferenceMapping.Select(pair => $"{pair.Key} = {pair.Value}")),
            TimedMatchingItemsText = metadata.TimedMatchingItems is null
                ? null
                : string.Join(Environment.NewLine, metadata.TimedMatchingItems),
            PatternDifficultyLevel = metadata.PatternDifficultyLevel,
            TimedNamingItemsText = metadata.TimedNamingItems is null ? null : string.Join(Environment.NewLine, metadata.TimedNamingItems),
            WorkingMemorySequenceText = metadata.WorkingMemorySequence is null ? null : string.Join(Environment.NewLine, metadata.WorkingMemorySequence),
            MemoryDisplayDurationMilliseconds = metadata.MemoryDisplayDurationMilliseconds,
            AudioPath = question.AudioPath,
            ImagePath = question.ImagePath,
            AllowsAudioRecording = question.AllowsAudioRecording
        };
    }

    private static ScreeningQuestionMetadata DeserializeMetadata(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return new ScreeningQuestionMetadata();
        }

        try
        {
            return JsonSerializer.Deserialize<ScreeningQuestionMetadata>(metadataJson, MetadataJsonOptions)
                ?? new ScreeningQuestionMetadata();
        }
        catch (JsonException)
        {
            return new ScreeningQuestionMetadata();
        }
    }

    private static List<string>? DeserializeAcceptedAnswers(string? acceptedAnswersJson)
    {
        if (string.IsNullOrWhiteSpace(acceptedAnswersJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(acceptedAnswersJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static List<string> ParseLines(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();

    private static Dictionary<string, string> ParseReferenceMapping(string? text)
    {
        var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in ParseLines(text))
        {
            if (TryParseReferenceMappingLine(line, out var item, out var symbol))
            {
                mapping[item] = symbol;
            }
        }

        return mapping;
    }

    private static bool TryParseReferenceMappingLine(string line, out string item, out string symbol)
    {
        var separatorIndex = line.IndexOf('=');
        if (separatorIndex <= 0 || separatorIndex == line.Length - 1)
        {
            item = string.Empty;
            symbol = string.Empty;
            return false;
        }

        item = line[..separatorIndex].Trim();
        symbol = line[(separatorIndex + 1)..].Trim();
        return item.Length > 0 && symbol.Length > 0;
    }

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string GetQuestionTypeDisplayName(ScreeningQuestionType questionType) =>
        typeof(ScreeningQuestionType)
            .GetMember(questionType.ToString())
            .FirstOrDefault()?
            .GetCustomAttribute<DisplayAttribute>()?
            .GetName() ?? questionType.ToString();

    private void SetTestContext(ScreeningTest screeningTest)
    {
        ViewData["ScreeningTestId"] = screeningTest.Id;
        ViewData["ScreeningTestName"] = screeningTest.Name;
    }
}
