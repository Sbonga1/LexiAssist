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
using LexiAssist.Web.ViewModels.AssessmentBuilder;
using LexiAssist.Web.ViewModels.AdminScreeningTests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LexiAssist.Web.Controllers;

[Authorize(Roles = ApplicationRoles.Administrator)]
[Route("admin/screening-tests/{testId:int}/questions")]
public sealed class AdminScreeningQuestionsController(
    ApplicationDbContext dbContext,
    IAuditService auditService,
    IAssessmentDraftGenerator draftGenerator,
    ListeningAudioService listeningAudioService) : Controller
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
    public async Task<IActionResult> Create(int testId, bool returnToBuilder = false, int returnPage = 1)
    {
        var screeningTest = await FindScreeningTestAsync(testId);
        if (screeningTest is null)
        {
            return NotFound();
        }

        SetTestContext(screeningTest, returnToBuilder, returnPage);
        ViewData["QuestionMode"] = "create";
        return View("Authoring", new AdminScreeningQuestionFormViewModel
        {
            QuestionType = ScreeningQuestionType.MultipleChoice,
            AnswerOptions = [string.Empty, string.Empty]
        });
    }

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(int testId, AdminScreeningQuestionFormViewModel model, bool returnToBuilder = false, int returnPage = 1, string? submitMode = null)
    {
        var screeningTest = await FindScreeningTestAsync(testId);
        if (screeningTest is null)
        {
            return NotFound();
        }

        SetTestContext(screeningTest, returnToBuilder, returnPage);
        ViewData["QuestionMode"] = "create";
        NormalizeChoiceOptions(model);
        if (string.Equals(submitMode, "preview", StringComparison.OrdinalIgnoreCase))
        {
            return BuildCandidatePreview(screeningTest, model,
                Url.Action(nameof(Create), new { testId, returnToBuilder = true, returnPage }));
        }

        ValidateQuestionSpecificFields(model);
        if (!ModelState.IsValid)
        {
            return View("Authoring", model);
        }

        var question = new ScreeningQuestion
        {
            ScreeningTestId = screeningTest.Id,
            Prompt = model.Prompt.Trim(),
            QuestionType = model.QuestionType,
            CorrectAnswer = NormalizeOptionalText(GetCorrectAnswer(model)),
            AcceptedAnswersJson = SerializeAcceptedAnswers(model),
            Weight = model.Weight,
            SortOrder = (await dbContext.ScreeningQuestions
                .Where(item => item.ScreeningTestId == screeningTest.Id)
                .MaxAsync(item => (int?)item.SortOrder) ?? 0) + 1,
            IsActive = false,
            AudioPath = GetAudioPath(model),
            ImagePath = GetImagePath(model),
            MetadataJson = SerializeMetadata(model),
            AllowsAudioRecording = model.AllowsAudioRecording && SupportsLearnerRecording(model.QuestionType)
        };

        dbContext.ScreeningQuestions.Add(question);
        await dbContext.SaveChangesAsync();
        await auditService.RecordAsync(AuditActions.ScreeningQuestionCreated, AuditEntityTypes.ScreeningQuestion, question.Id.ToString(), true,
            new { question.ScreeningTestId, QuestionType = question.QuestionType.ToString() });
        if (question.QuestionType == ScreeningQuestionType.ListeningResponse && !string.IsNullOrWhiteSpace(question.AudioPath))
        {
            await auditService.RecordAsync(
                string.Equals(model.AudioSourceMode, "record", StringComparison.OrdinalIgnoreCase)
                    ? AuditActions.ListeningAudioRecorded
                    : AuditActions.ListeningAudioUploaded,
                AuditEntityTypes.ScreeningQuestion,
                question.Id.ToString(),
                true,
                new { question.ScreeningTestId, Source = model.AudioSourceMode });
        }

        TempData["StatusMessage"] = "The question was saved as an inactive draft. Review it, then activate it when it is ready.";
        if (string.Equals(submitMode, "next", StringComparison.OrdinalIgnoreCase))
        {
            TempData["StatusMessage"] = "The question was saved as an inactive draft. Add the next question from the builder when you are ready.";
            return RedirectToAction("Index", "AdminAssessmentBuilder", new { testId = screeningTest.Id, page = Math.Max(returnPage, 1) });
        }

        return RedirectToAction(nameof(Edit), new { testId = screeningTest.Id, questionId = question.Id, returnToBuilder = true, returnPage });
    }

    [HttpGet("{questionId:int}/edit")]
    public async Task<IActionResult> Edit(int testId, int questionId, bool returnToBuilder = false, int returnPage = 1)
    {
        var question = await FindQuestionAsync(testId, questionId, includeTest: true);
        if (question is null)
        {
            return NotFound();
        }

        SetTestContext(question.ScreeningTest, returnToBuilder, returnPage);
        ViewData["QuestionMode"] = "edit";
        ViewData["OriginalQuestionType"] = (int)question.QuestionType;
        ViewData["QuestionIsActive"] = question.IsActive;
        return View("Authoring", ToFormViewModel(question));
    }

    [HttpPost("audio/upload")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadAudio(int testId, IFormFile? audioFile, int? questionId, string source = "upload")
    {
        if (!await dbContext.ScreeningTests.AnyAsync(test => test.Id == testId))
        {
            return NotFound();
        }

        if (questionId is int id && await FindQuestionAsync(testId, id) is null)
        {
            return NotFound();
        }

        var result = await listeningAudioService.SaveAsync(audioFile, source, HttpContext.RequestAborted);
        if (!result.Success)
        {
            return BadRequest(new { error = result.ErrorMessage });
        }

        if (questionId is int questionKey)
        {
            await auditService.RecordAsync(
                string.Equals(source, "record", StringComparison.OrdinalIgnoreCase)
                    ? AuditActions.ListeningAudioRecorded
                    : AuditActions.ListeningAudioUploaded,
                AuditEntityTypes.ScreeningQuestion,
                questionKey.ToString(),
                true,
                new { Source = source, result.FileName, result.SizeBytes, result.DurationSeconds });
        }

        return Json(new
        {
            fileName = result.FileName,
            contentType = result.ContentType,
            sizeBytes = result.SizeBytes,
            durationSeconds = result.DurationSeconds,
            source,
            previewUrl = Url.Action(nameof(AudioPreview), new { testId, fileName = result.FileName })
        });
    }

    [HttpGet("audio/preview/{fileName}")]
    public async Task<IActionResult> AudioPreview(int testId, string fileName)
    {
        var questionUsesFile = await dbContext.ScreeningQuestions.AsNoTracking()
            .AnyAsync(question => question.ScreeningTestId == testId && question.AudioPath == fileName);
        if (!questionUsesFile && !listeningAudioService.IsOwnedFile(fileName))
        {
            return NotFound();
        }

        if (!ScreeningAudioSource.TryResolve(fileName, HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().ContentRootPath, out var path, out var contentType))
        {
            return NotFound();
        }

        Response.Headers["Content-Disposition"] = "inline";
        Response.Headers["Cache-Control"] = "private, no-store";
        Response.ContentType = contentType;
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete), contentType, enableRangeProcessing: true);
    }

    [HttpPost("{questionId:int}/audio/remove")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveAudio(int testId, int questionId)
    {
        var question = await FindQuestionAsync(testId, questionId);
        if (question is null) return NotFound();
        var oldPath = question.AudioPath;
        if (question.QuestionType == ScreeningQuestionType.ListeningResponse)
        {
            question.AudioPath = null;
            var metadata = DeserializeMetadata(question.MetadataJson);
            metadata.AudioSource = null;
            metadata.AudioDurationSeconds = null;
            question.MetadataJson = SerializeMetadata(metadata);
            await dbContext.SaveChangesAsync();
            listeningAudioService.TryDeleteOwned(oldPath);
            await auditService.RecordAsync(AuditActions.ListeningAudioRemoved, AuditEntityTypes.ScreeningQuestion, questionId.ToString(), true,
                new { ScreeningTestId = testId });
        }
        return RedirectToAction(nameof(Edit), new { testId, questionId, returnToBuilder = true });
    }

    [HttpPost("{questionId:int}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int testId, int questionId, AdminScreeningQuestionFormViewModel model, bool returnToBuilder = false, int returnPage = 1, string? submitMode = null)
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

        SetTestContext(question.ScreeningTest, returnToBuilder, returnPage);
        NormalizeChoiceOptions(model);
        if (string.Equals(submitMode, "preview", StringComparison.OrdinalIgnoreCase))
        {
            ViewData["QuestionIsActive"] = question.IsActive;
            return BuildCandidatePreview(question.ScreeningTest, model,
                Url.Action(nameof(Edit), new { testId, questionId, returnToBuilder = true, returnPage }));
        }

        if (question.QuestionType != model.QuestionType && HasQuestionTypeData(question) && !model.ConfirmTypeChange)
        {
            ModelState.AddModelError(nameof(model.QuestionType), "Changing question type clears settings stored for the current type. Select the new type again and confirm before saving.");
        }
        ValidateQuestionSpecificFields(model);
        if (!ModelState.IsValid)
        {
            ViewData["QuestionMode"] = "edit";
            ViewData["OriginalQuestionType"] = (int)question.QuestionType;
            ViewData["QuestionIsActive"] = question.IsActive;
            return View("Authoring", model);
        }

        var previousAudioPath = question.AudioPath;
        question.Prompt = model.Prompt.Trim();
        question.QuestionType = model.QuestionType;
        question.Weight = model.Weight;
        question.CorrectAnswer = NormalizeOptionalText(GetCorrectAnswer(model));
        question.AcceptedAnswersJson = SerializeAcceptedAnswers(model);
        question.AudioPath = GetAudioPath(model);
        question.ImagePath = GetImagePath(model);
        question.MetadataJson = SerializeMetadata(model);
        question.AllowsAudioRecording = model.AllowsAudioRecording && SupportsLearnerRecording(model.QuestionType);
        await dbContext.SaveChangesAsync();
        if (question.QuestionType == ScreeningQuestionType.ListeningResponse &&
            !string.Equals(previousAudioPath, question.AudioPath, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(previousAudioPath))
        {
            listeningAudioService.TryDeleteOwned(previousAudioPath);
            await auditService.RecordAsync(AuditActions.ListeningAudioReplaced, AuditEntityTypes.ScreeningQuestion, question.Id.ToString(), true,
                new { question.ScreeningTestId, PreviousAudioPath = previousAudioPath });
        }
        await auditService.RecordAsync(AuditActions.ScreeningQuestionUpdated, AuditEntityTypes.ScreeningQuestion, question.Id.ToString(), true,
            new { question.ScreeningTestId, QuestionType = question.QuestionType.ToString() });

        TempData["StatusMessage"] = question.IsActive
            ? "The question was updated. Its active status was preserved."
            : "The inactive question draft was updated.";
        if (string.Equals(submitMode, "next", StringComparison.OrdinalIgnoreCase))
        {
            var nextQuestionId = await dbContext.ScreeningQuestions.AsNoTracking()
                .Where(item => item.ScreeningTestId == testId &&
                               (item.SortOrder > question.SortOrder || item.SortOrder == question.SortOrder && item.Id > question.Id))
                .OrderBy(item => item.SortOrder)
                .ThenBy(item => item.Id)
                .Select(item => (int?)item.Id)
                .FirstOrDefaultAsync();
            if (nextQuestionId is int nextId)
            {
                return RedirectToAction(nameof(Edit), new { testId, questionId = nextId, returnToBuilder = true, returnPage });
            }

            TempData["StatusMessage"] = "The last question was saved. You are back at the builder.";
            return RedirectToAction("Index", "AdminAssessmentBuilder", new { testId, page = Math.Max(returnPage, 1) });
        }

        return RedirectToAction(nameof(Edit), new { testId, questionId, returnToBuilder = true, returnPage });
    }

    [HttpPost("{questionId:int}/duplicate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Duplicate(int testId, int questionId, bool returnToBuilder = true, int returnPage = 1)
    {
        var source = await FindQuestionAsync(testId, questionId);
        if (source is null)
        {
            return NotFound();
        }

        var sortOrder = (await dbContext.ScreeningQuestions
            .Where(item => item.ScreeningTestId == testId)
            .MaxAsync(item => (int?)item.SortOrder) ?? 0) + 1;
        var copy = ScreeningQuestionCopyUtility.CopyToTest(source, testId, sortOrder);
        dbContext.ScreeningQuestions.Add(copy);
        await dbContext.SaveChangesAsync();
        await auditService.RecordAsync(AuditActions.ScreeningQuestionCopied, AuditEntityTypes.ScreeningQuestion, copy.Id.ToString(), true,
            new { copy.ScreeningTestId, SourceQuestionId = source.Id, QuestionType = copy.QuestionType.ToString(), Source = "Question duplicated in authoring workspace" });
        TempData["StatusMessage"] = "A new inactive copy was created. Review it before activating.";
        return RedirectToAction(nameof(Edit), new { testId, questionId = copy.Id, returnToBuilder = true, returnPage = Math.Max(returnPage, 1) });
    }

    [HttpPost("{questionId:int}/activate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Activate(int testId, int questionId, bool returnToBuilder = false, int returnPage = 1)
    {
        var question = await FindQuestionAsync(testId, questionId);
        if (question is null)
        {
            return NotFound();
        }

        if (question.QuestionType == ScreeningQuestionType.ListeningResponse &&
            !ScreeningAudioSource.TryResolve(question.AudioPath, HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().ContentRootPath, out _, out _))
        {
            TempData["ErrorMessage"] = "A listening question must have a stored audio stimulus before it can be activated.";
            return RedirectAfterQuestionEdit(testId, returnToBuilder, returnPage);
        }

        question.IsActive = true;
        await dbContext.SaveChangesAsync();
        await auditService.RecordAsync(AuditActions.ScreeningQuestionActivated, AuditEntityTypes.ScreeningQuestion, questionId.ToString(), true,
            new { ScreeningTestId = testId });
        TempData["StatusMessage"] = "The question was activated.";
        return RedirectAfterQuestionEdit(testId, returnToBuilder, returnPage);
    }

    [HttpPost("{questionId:int}/deactivate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deactivate(int testId, int questionId, bool returnToBuilder = false, int returnPage = 1)
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
        return RedirectAfterQuestionEdit(testId, returnToBuilder, returnPage);
    }

    [HttpPost("{questionId:int}/move")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Move(int testId, int questionId, string direction, bool returnToBuilder = false, int returnPage = 1)
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

        return RedirectAfterQuestionEdit(testId, returnToBuilder, returnPage);
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
        else if (model.QuestionType == ScreeningQuestionType.ListeningResponse && model.ListeningComposerEnabled &&
                 !ScreeningAudioSource.TryResolve(model.AudioPath, HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().ContentRootPath, out _, out _))
        {
            ModelState.AddModelError(nameof(model.AudioPath), "Record or upload an audio stimulus before saving this listening question.");
        }

        if (model.QuestionType == ScreeningQuestionType.ListeningResponse)
        {
            var responseType = NormalizeResponseType(model.ListeningResponseType);
            if (model.ListeningComposerEnabled && responseType == "multiple-choice")
            {
                if (choiceOptions.Distinct(StringComparer.OrdinalIgnoreCase).Count() < 2)
                {
                    ModelState.AddModelError(nameof(model.ChoiceOptionsText), "Enter at least two different answer choices for a listening multiple-choice question.");
                }
                if (string.IsNullOrWhiteSpace(model.CorrectAnswer) || !choiceOptions.Contains(model.CorrectAnswer.Trim(), StringComparer.OrdinalIgnoreCase))
                {
                    ModelState.AddModelError(nameof(model.CorrectAnswer), "Choose a correct answer from the listening answer choices.");
                }
            }
            else if (model.ListeningComposerEnabled && responseType == "text-response" && string.IsNullOrWhiteSpace(model.CorrectAnswer))
            {
                ModelState.AddModelError(nameof(model.CorrectAnswer), "Enter the expected spoken response.");
            }
            else if (model.ListeningComposerEnabled && responseType == "audio-response" && !model.AllowsAudioRecording)
            {
                ModelState.AddModelError(nameof(model.AllowsAudioRecording), "Enable learner audio recording when the response type is Audio response.");
            }
        }
    }

    private static void NormalizeChoiceOptions(AdminScreeningQuestionFormViewModel model)
    {
        if (model.AnswerOptions.Any(option => !string.IsNullOrWhiteSpace(option)))
        {
            model.ChoiceOptionsText = string.Join(Environment.NewLine,
                model.AnswerOptions.Where(option => !string.IsNullOrWhiteSpace(option)).Select(option => option.Trim()));
        }

        model.AnswerOptions = ParseLines(model.ChoiceOptionsText);
        var usesAnswerChoice = (model.QuestionType is ScreeningQuestionType.MultipleChoice or
            ScreeningQuestionType.PhonologicalChoice or
            ScreeningQuestionType.PatternReasoning) ||
            model.QuestionType == ScreeningQuestionType.ListeningResponse &&
            NormalizeResponseType(model.ListeningResponseType) == "multiple-choice";
        if (usesAnswerChoice && !string.IsNullOrWhiteSpace(model.SelectedCorrectAnswer) &&
            model.AnswerOptions.Contains(model.SelectedCorrectAnswer.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            model.CorrectAnswer = model.SelectedCorrectAnswer.Trim();
        }

        if (model.AnswerOptions.Count == 0)
        {
            model.AnswerOptions = [string.Empty, string.Empty];
        }
        else if (model.AnswerOptions.Count == 1)
        {
            model.AnswerOptions.Add(string.Empty);
        }
    }

    private static bool SupportsLearnerRecording(ScreeningQuestionType questionType) => questionType is
        ScreeningQuestionType.WordReading or
        ScreeningQuestionType.NonWordReading or
        ScreeningQuestionType.TimedNaming or
        ScreeningQuestionType.ListeningResponse or
        ScreeningQuestionType.ReadingFluency;

    private static bool HasQuestionTypeData(ScreeningQuestion question) =>
        !string.IsNullOrWhiteSpace(question.CorrectAnswer) ||
        !string.IsNullOrWhiteSpace(question.AcceptedAnswersJson) ||
        !string.IsNullOrWhiteSpace(question.MetadataJson) ||
        !string.IsNullOrWhiteSpace(question.AudioPath) ||
        !string.IsNullOrWhiteSpace(question.ImagePath) ||
        question.AllowsAudioRecording;

    private IActionResult BuildCandidatePreview(ScreeningTest screeningTest, AdminScreeningQuestionFormViewModel model, string? returnUrl)
    {
        var audioPreviewUrl = model.QuestionType == ScreeningQuestionType.ListeningResponse &&
                              !string.IsNullOrWhiteSpace(model.AudioPath)
            ? Url.Action(nameof(AudioPreview), new { testId = screeningTest.Id, fileName = model.AudioPath })
            : null;
        var choices = model.QuestionType == ScreeningQuestionType.MultipleChoice ||
                      model.QuestionType == ScreeningQuestionType.ListeningResponse && NormalizeResponseType(model.ListeningResponseType) == "multiple-choice"
            ? ParseLines(model.ChoiceOptionsText)
            : model.QuestionType is ScreeningQuestionType.PhonologicalChoice or
                ScreeningQuestionType.TimedMatching or ScreeningQuestionType.PatternReasoning
                ? ParseLines(model.ChoiceOptionsText)
                : [];
        var previewQuestion = new AssessmentPreviewQuestion(
            string.IsNullOrWhiteSpace(model.Prompt) ? "Your question prompt will appear here." : model.Prompt.Trim(),
            GetQuestionTypeDisplayName(model.QuestionType),
            choices,
            model.QuestionType is ScreeningQuestionType.ReadingPassage or ScreeningQuestionType.ReadingFluency
                ? NormalizeOptionalText(model.PassageText)
                : null,
            model.AllowsAudioRecording && SupportsLearnerRecording(model.QuestionType),
            GetSafePreviewImagePath(model.ImagePath),
            audioPreviewUrl,
            model.MaxAudioReplays,
            NormalizeOptionalText(model.Transcript));

        return View("~/Views/AdminAssessmentBuilder/Preview.cshtml", new AssessmentPreviewViewModel
        {
            ScreeningTestId = screeningTest.Id,
            Name = screeningTest.Name,
            Description = screeningTest.Description,
            Instructions = screeningTest.Instructions,
            ReturnUrl = returnUrl,
            ReturnLabel = "Back to question editor",
            Questions = [previewQuestion]
        });
    }

    private static string? GetSafePreviewImagePath(string? value)
    {
        var path = NormalizeOptionalText(value);
        if (path is null)
        {
            return null;
        }

        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) &&
            (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            return path;
        }

        return path.StartsWith('/') && !path.StartsWith("//") && !path.Contains('\\') ? path : null;
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
            ScreeningQuestionType.ListeningResponse when NormalizeResponseType(model.ListeningResponseType) != "multiple-choice" => answer,
            ScreeningQuestionType.ListeningResponse => ParseLines(model.ChoiceOptionsText)
                .FirstOrDefault(option => string.Equals(option, answer, StringComparison.OrdinalIgnoreCase)),
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
            ChoiceOptions = (model.QuestionType is ScreeningQuestionType.MultipleChoice or
                ScreeningQuestionType.PhonologicalChoice or
                ScreeningQuestionType.TimedMatching or
                ScreeningQuestionType.PatternReasoning) ||
                model.QuestionType == ScreeningQuestionType.ListeningResponse &&
                model.ListeningComposerEnabled && NormalizeResponseType(model.ListeningResponseType) == "multiple-choice"
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
            AudioSource = model.QuestionType == ScreeningQuestionType.ListeningResponse && model.ListeningComposerEnabled
                ? NormalizeOptionalText(model.AudioSourceMode)
                : null,
            AudioDurationSeconds = model.QuestionType == ScreeningQuestionType.ListeningResponse && model.ListeningComposerEnabled
                ? model.AudioDurationSeconds
                : null,
            Transcript = model.QuestionType == ScreeningQuestionType.ListeningResponse && model.ListeningComposerEnabled
                ? NormalizeOptionalText(model.Transcript)
                : null,
            ResponseType = model.QuestionType == ScreeningQuestionType.ListeningResponse && model.ListeningComposerEnabled
                ? NormalizeResponseType(model.ListeningResponseType)
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
               metadata.AudioSource is null && metadata.AudioDurationSeconds is null && metadata.Transcript is null && metadata.ResponseType is null &&
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
            Weight = question.Weight,
            CorrectAnswer = question.CorrectAnswer,
            AcceptedAnswersText = acceptedAnswers is null ? null : string.Join(Environment.NewLine, acceptedAnswers),
            ChoiceOptionsText = metadata.ChoiceOptions is null ? null : string.Join(Environment.NewLine, metadata.ChoiceOptions),
            AnswerOptions = metadata.ChoiceOptions is null || metadata.ChoiceOptions.Count == 0
                ? [string.Empty, string.Empty]
                : metadata.ChoiceOptions.ToList(),
            PassageText = metadata.PassageText,
            ReferenceWordCount = metadata.ReferenceWordCount,
            TargetWordsPerMinute = metadata.TargetWordsPerMinute,
            MaxAudioReplays = metadata.MaxAudioReplays,
            AudioSourceMode = metadata.AudioSource ?? (question.AudioPath is null ? "upload" : "existing"),
            AudioDurationSeconds = metadata.AudioDurationSeconds,
            Transcript = metadata.Transcript,
            ListeningResponseType = metadata.ResponseType ?? "multiple-choice",
            ListeningComposerEnabled = question.QuestionType == ScreeningQuestionType.ListeningResponse,
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

    private static string? SerializeMetadata(ScreeningQuestionMetadata metadata) =>
        metadata.ChoiceOptions is null && metadata.PassageText is null && metadata.ReferenceWordCount is null &&
        metadata.TargetWordsPerMinute is null && metadata.MaxAudioReplays is null && metadata.AudioSource is null &&
        metadata.AudioDurationSeconds is null && metadata.Transcript is null && metadata.ResponseType is null &&
        metadata.TimeLimitSeconds is null && metadata.TimedMatchingReferenceMapping is null && metadata.TimedMatchingItems is null &&
        metadata.TimedNamingItems is null && metadata.WorkingMemorySequence is null && metadata.MemoryDisplayDurationMilliseconds is null &&
        metadata.PatternDifficultyLevel is null
            ? null
            : JsonSerializer.Serialize(metadata, MetadataJsonOptions);

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

    private static string NormalizeResponseType(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "text-response" => "text-response",
        "audio-response" => "audio-response",
        _ => "multiple-choice"
    };

    private static string GetQuestionTypeDisplayName(ScreeningQuestionType questionType) =>
        typeof(ScreeningQuestionType)
            .GetMember(questionType.ToString())
            .FirstOrDefault()?
            .GetCustomAttribute<DisplayAttribute>()?
            .GetName() ?? questionType.ToString();

    private IActionResult RedirectAfterQuestionEdit(int testId, bool returnToBuilder, int returnPage) =>
        returnToBuilder
            ? RedirectToAction("Index", "AdminAssessmentBuilder", new { testId, page = Math.Max(returnPage, 1) })
            : RedirectToAction(nameof(Index), new { testId });

    private void SetTestContext(ScreeningTest screeningTest, bool returnToBuilder = false, int returnPage = 1)
    {
        ViewData["ScreeningTestId"] = screeningTest.Id;
        ViewData["ScreeningTestName"] = screeningTest.Name;
        ViewData["ReturnToBuilder"] = returnToBuilder;
        ViewData["ReturnPage"] = Math.Max(returnPage, 1);
        ViewData["OpenAiConfigured"] = draftGenerator.IsConfigured;
    }
}
