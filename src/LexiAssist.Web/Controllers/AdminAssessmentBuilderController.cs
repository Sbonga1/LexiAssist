using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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
[Route("admin/screening-tests/{testId:int}/builder")]
public sealed class AdminAssessmentBuilderController(
    ApplicationDbContext dbContext,
    IAuditService auditService,
    IAssessmentDraftGenerator draftGenerator,
    IAssessmentReadinessService readinessService,
    ILogger<AdminAssessmentBuilderController> logger) : Controller
{
    private const int QuestionPageSize = 8;
    private const int MaxCsvBytes = 8 * 1024 * 1024;
    private static readonly Regex BearerCredentialPattern = new(@"\bBearer\s+[^\s,;]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex OpenAiKeyPattern = new(@"\bsk-[^\s,;]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly JsonSerializerOptions MetadataOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [HttpGet]
    public async Task<IActionResult> Index(int testId, int page = 1, Guid? resumableDraftId = null)
    {
        var model = await BuildViewModelAsync(testId, page, resumableDraftId: resumableDraftId);
        return model is null ? NotFound() : View(model);
    }

    [HttpGet("details")]
    public async Task<IActionResult> Details(int testId, CancellationToken cancellationToken)
    {
        var test = await dbContext.ScreeningTests.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == testId, cancellationToken);
        if (test is null)
        {
            return NotFound();
        }

        return View(new AdminScreeningTestFormViewModel
        {
            Id = test.Id,
            Name = test.Name,
            Slug = test.Slug,
            Description = test.Description,
            Instructions = test.Instructions
        });
    }

    [HttpGet("configure")]
    public async Task<IActionResult> Configure(int testId, CancellationToken cancellationToken)
    {
        var readiness = await readinessService.GetReadinessAsync(testId, cancellationToken);
        return readiness is null ? NotFound() : View(readiness);
    }

    [HttpGet("review")]
    public async Task<IActionResult> Review(int testId, CancellationToken cancellationToken)
    {
        var readiness = await readinessService.GetReadinessAsync(testId, cancellationToken);
        return readiness is null ? NotFound() : View(readiness);
    }

    [HttpGet("preview")]
    public async Task<IActionResult> Preview(int testId, bool returnToReview = false)
    {
        var test = await dbContext.ScreeningTests.AsNoTracking().SingleOrDefaultAsync(item => item.Id == testId);
        if (test is null)
        {
            return NotFound();
        }

        var questions = await dbContext.ScreeningQuestions.AsNoTracking()
            .Where(question => question.ScreeningTestId == testId && question.IsActive)
            .OrderBy(question => question.SortOrder)
            .ThenBy(question => question.Id)
            .ToListAsync();

        return View(new AssessmentPreviewViewModel
        {
            ScreeningTestId = test.Id,
            Name = test.Name,
            Description = test.Description,
            Instructions = test.Instructions,
            ReturnUrl = returnToReview
                ? Url.Action(nameof(Review), new { testId })
                : Url.Action(nameof(Index), new { testId }),
            ReturnLabel = returnToReview ? "Back to review" : "Back to builder",
            Questions = questions.Select(question =>
            {
                var metadata = DeserializeMetadata(question.MetadataJson);
                var audioPreviewUrl = question.QuestionType == ScreeningQuestionType.ListeningResponse &&
                                      !string.IsNullOrWhiteSpace(question.AudioPath)
                    ? Url.Action("AudioPreview", "AdminScreeningQuestions", new { testId, fileName = question.AudioPath })
                    : null;
                return new AssessmentPreviewQuestion(
                    question.Prompt,
                    GetQuestionTypeName(question.QuestionType),
                    metadata.ChoiceOptions ?? [],
                    metadata.PassageText,
                    question.AllowsAudioRecording,
                    GetSafePreviewImagePath(question.ImagePath),
                    audioPreviewUrl,
                    metadata.MaxAudioReplays,
                    metadata.Transcript,
                    question.QuestionType,
                    question.CorrectAnswer,
                    metadata.ResponseType,
                    question.QuestionType switch
                    {
                        ScreeningQuestionType.TimedNaming => metadata.TimedNamingItems,
                        ScreeningQuestionType.WorkingMemorySequence => metadata.WorkingMemorySequence,
                        ScreeningQuestionType.TimedMatching => metadata.TimedMatchingItems,
                        _ => null
                    },
                    metadata.TimedMatchingReferenceMapping,
                    metadata.TimeLimitSeconds,
                    metadata.MemoryDisplayDurationMilliseconds,
                    metadata.ReferenceWordCount,
                    metadata.TargetWordsPerMinute,
                    metadata.PatternDifficultyLevel);
            }).ToList()
        });
    }

    [HttpGet("question-bank")]
    public async Task<IActionResult> QuestionBank(int testId, string? search)
    {
        var test = await dbContext.ScreeningTests.AsNoTracking()
            .Where(item => item.Id == testId)
            .Select(item => new { item.Id, item.Name })
            .SingleOrDefaultAsync();
        if (test is null)
        {
            return NotFound();
        }

        search = search?.Trim() ?? string.Empty;
        var query = dbContext.ScreeningQuestions.AsNoTracking()
            .Where(question => question.ScreeningTestId != testId && question.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(question => EF.Functions.Like(question.Prompt, $"%{search}%") ||
                                            EF.Functions.Like(question.ScreeningTest.Name, $"%{search}%"));
        }

        var questions = await query
            .OrderBy(question => question.ScreeningTest.Name)
            .ThenBy(question => question.SortOrder)
            .ThenBy(question => question.Id)
            .Take(50)
            .Select(question => new AssessmentQuestionBankItem(
                question.Id,
                question.ScreeningTest.Name,
                question.Prompt,
                GetQuestionTypeName(question.QuestionType),
                question.IsActive))
            .ToListAsync();

        return View(new AssessmentQuestionBankViewModel
        {
            ScreeningTestId = test.Id,
            ScreeningTestName = test.Name,
            Search = search,
            Questions = questions
        });
    }

    [HttpPost("question-bank/{questionId:int}/copy")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CopyQuestion(int testId, int questionId)
    {
        if (!await dbContext.ScreeningTests.AnyAsync(item => item.Id == testId))
        {
            return NotFound();
        }

        var source = await dbContext.ScreeningQuestions.AsNoTracking()
            .SingleOrDefaultAsync(question => question.Id == questionId && question.ScreeningTestId != testId && question.IsActive);
        if (source is null)
        {
            return NotFound();
        }

        var copy = ScreeningQuestionCopyUtility.CopyToTest(source, testId, await GetNextQuestionOrderAsync(testId));
        dbContext.ScreeningQuestions.Add(copy);
        await dbContext.SaveChangesAsync();
        await auditService.RecordAsync(AuditActions.ScreeningQuestionCopied, AuditEntityTypes.ScreeningQuestion, copy.Id.ToString(), true,
            new { copy.ScreeningTestId, SourceQuestionId = source.Id, QuestionType = copy.QuestionType.ToString() });
        TempData["StatusMessage"] = "The question was copied as inactive. Review it and activate the copy when it is ready; the original remains unchanged.";
        return RedirectToAction(nameof(Index), new { testId });
    }

    [HttpGet("import-template")]
    public async Task<IActionResult> ImportTemplate(int testId, CancellationToken cancellationToken)
    {
        if (!await dbContext.ScreeningTests.AnyAsync(item => item.Id == testId, cancellationToken))
        {
            return NotFound();
        }

        const string template = "QuestionType,Prompt,CorrectAnswer,Choices,AcceptedAnswers\r\n";
        return File(System.Text.Encoding.UTF8.GetBytes(template), "text/csv", "assessment-questions-template.csv");
    }

    [HttpPost("import")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxCsvBytes + 64 * 1024)]
    public async Task<IActionResult> Import(int testId, IFormFile? csvFile, bool csvCompressed, CancellationToken cancellationToken)
    {
        if (csvFile is null || csvFile.Length == 0)
        {
            return await IndexWithImportResultAsync(testId, ["Choose a CSV file to import."], cancellationToken);
        }

        if (csvFile.Length > MaxCsvBytes)
        {
            return await IndexWithImportResultAsync(testId, ["The CSV upload must be 8 MB or smaller."], cancellationToken);
        }

        if (!string.Equals(Path.GetExtension(Path.GetFileName(csvFile.FileName)), ".csv", StringComparison.OrdinalIgnoreCase))
        {
            return await IndexWithImportResultAsync(testId, ["Choose a .csv file. XLSX files are not supported."], cancellationToken);
        }

        var testExists = await dbContext.ScreeningTests.AnyAsync(item => item.Id == testId, cancellationToken);
        if (!testExists)
        {
            return NotFound();
        }

        string csvText;
        List<List<string>> rows = [];
        string? csvReadError = null;
        try
        {
            await using var uploadStream = csvFile.OpenReadStream();
            await using Stream contentStream = csvCompressed
                ? new System.IO.Compression.GZipStream(uploadStream, System.IO.Compression.CompressionMode.Decompress, leaveOpen: false)
                : uploadStream;
            await using var expandedStream = new MemoryStream();
            var buffer = new byte[81920];
            int bytesRead;
            while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                if (expandedStream.Length + bytesRead > MaxCsvBytes)
                {
                    return await IndexWithImportResultAsync(testId, ["The CSV is larger than the 8 MB limit after decompression."], cancellationToken);
                }

                await expandedStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            }

            expandedStream.Position = 0;
            using var reader = new StreamReader(expandedStream, detectEncodingFromByteOrderMarks: true);
            csvText = await reader.ReadToEndAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or ArgumentException)
        {
            csvText = string.Empty;
            csvReadError = exception is InvalidDataException ? exception.Message : "The CSV could not be read. Check that it is a valid CSV file.";
        }

        if (csvReadError is null)
        {
            try
            {
                rows = ParseCsv(csvText);
            }
            catch (InvalidDataException exception)
            {
                csvReadError = exception.Message;
            }
        }

        var parsedQuestions = csvReadError is null
            ? ValidateCsvRows(rows)
            : new CsvValidationResult([], [csvReadError]);
        if (parsedQuestions.Errors.Count > 0)
        {
            if (!draftGenerator.IsConfigured || string.IsNullOrWhiteSpace(csvText))
            {
                var errors = parsedQuestions.Errors.ToList();
                if (!draftGenerator.IsConfigured)
                {
                    errors.Add("OpenAI is not configured, so this CSV layout cannot be interpreted automatically.");
                }
                return await IndexWithImportResultAsync(testId, errors, cancellationToken);
            }

            try
            {
                // Transport compression is lossless; preserve cell content for interpretation too.
                var interpreted = await draftGenerator.InterpretCsvAsync(new CsvQuestionInterpretationInput(csvText), cancellationToken);
                var screeningTest = await dbContext.ScreeningTests.AsNoTracking()
                    .SingleAsync(item => item.Id == testId, cancellationToken);

                var draftId = await CreateDraftAsync(testId, true, "Balanced", interpreted.Select(question => new GeneratedAssessmentQuestionViewModel
                    {
                        Prompt = question.Prompt,
                        QuestionType = question.QuestionType,
                        CorrectAnswer = question.CorrectAnswer,
                        Choices = question.Choices.ToList(),
                        AcceptedAnswers = question.AcceptedAnswers.ToList()
                    }).ToList(), cancellationToken);
                return RedirectToAction(nameof(DraftReview), new { testId, draftId });
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("AI CSV interpretation timed out for assessment {AssessmentId}.", testId);
                TempData["ClientConsoleError"] = "TaskCanceledException: OpenAI CSV interpretation timed out.";
                return await IndexWithImportResultAsync(testId, ["AI interpretation timed out. Try a smaller CSV or use the template format."], cancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or JsonException)
            {
                var safeDetails = GetSafeExceptionDetails(exception);
                logger.LogError(
                    "AI CSV interpretation failed for assessment {AssessmentId}. Exception chain: {ExceptionDetails}",
                    testId,
                    safeDetails);
                TempData["ClientConsoleError"] = safeDetails;
                var userMessage = exception is InvalidOperationException && exception.Message.Contains("48,000", StringComparison.Ordinal)
                    ? exception.Message
                    : "OpenAI could not confidently interpret every question in this CSV. Check the format, answers, and OpenAI configuration, then try again.";
                return await IndexWithImportResultAsync(testId, [userMessage], cancellationToken);
            }
        }

        var currentOrder = await GetNextQuestionOrderAsync(testId, cancellationToken) - 1;
        var additions = parsedQuestions.Questions.Select((question, index) => new ScreeningQuestion
        {
            ScreeningTestId = testId,
            Prompt = question.Prompt,
            QuestionType = question.Type,
            CorrectAnswer = question.CorrectAnswer,
            AcceptedAnswersJson = question.AcceptedAnswers.Count == 0 ? null : JsonSerializer.Serialize(question.AcceptedAnswers),
            Weight = 1.0,
            SortOrder = currentOrder + index + 1,
            IsActive = false,
            MetadataJson = question.Type == ScreeningQuestionType.MultipleChoice
                ? JsonSerializer.Serialize(new ScreeningQuestionMetadata { ChoiceOptions = question.Choices }, MetadataOptions)
                : null
        }).ToList();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        dbContext.ScreeningQuestions.AddRange(additions);
        await dbContext.SaveChangesAsync(cancellationToken);
        foreach (var question in additions)
        {
            await auditService.RecordAsync(AuditActions.ScreeningQuestionCreated, AuditEntityTypes.ScreeningQuestion, question.Id.ToString(), true,
                new { question.ScreeningTestId, QuestionType = question.QuestionType.ToString(), Source = "CSVImport" }, cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);

        return await IndexWithImportResultAsync(testId, [], cancellationToken,
            $"Imported {additions.Count} question{(additions.Count == 1 ? string.Empty : "s")}. All rows were validated before any were saved.");
    }

    [HttpPost("generate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Generate(int testId, AssessmentDraftRequestViewModel request, CancellationToken cancellationToken)
    {
        var test = await dbContext.ScreeningTests.AsNoTracking().SingleOrDefaultAsync(item => item.Id == testId, cancellationToken);
        if (test is null)
        {
            return NotFound();
        }

        if (!draftGenerator.IsConfigured)
        {
            logger.LogWarning("AI draft generation was requested for assessment {AssessmentId}, but OpenAI is not configured.", testId);
            TempData["ClientConsoleError"] = "ConfigurationError: OpenAI is not configured. Set OPENAI_API_KEY or OpenAI:ApiKey.";
            TempData["ErrorMessage"] = "AI question generation is not configured. Set OPENAI_API_KEY for the application and try again.";
            return RedirectToAction(nameof(Index), new { testId });
        }

        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = "Choose a question count from 1 to 20 before generating a draft.";
            return RedirectToAction(nameof(Index), new { testId });
        }

        if (request.RequestId == Guid.Empty)
        {
            request.RequestId = Guid.NewGuid();
        }

        var priorRequest = await dbContext.AssessmentDrafts.AsNoTracking()
            .SingleOrDefaultAsync(draft => draft.Id == request.RequestId, cancellationToken);
        if (priorRequest is not null)
        {
            var priorContent = DeserializeDraftContent(priorRequest.ContentJson);
            if (priorRequest.ScreeningTestId != testId || priorRequest.IsCsvImport || priorContent.GenerationRequestId != request.RequestId)
            {
                return Conflict("This generation request identifier is already in use. Reload the assessment builder and try again.");
            }

            TempData["StatusMessage"] = "These questions were already generated and are available in the builder. No duplicates were created.";
            var existingCount = await dbContext.ScreeningQuestions.CountAsync(question => question.ScreeningTestId == testId, cancellationToken);
            var existingPage = Math.Max(1, (int)Math.Ceiling(existingCount / (double)QuestionPageSize));
            return RedirectToAction(nameof(Index), new { testId, page = existingPage });
        }

        try
        {
            var existingPrompts = await dbContext.ScreeningQuestions.AsNoTracking()
                .Where(question => question.ScreeningTestId == testId)
                .OrderBy(question => question.SortOrder)
                .Select(question => question.Prompt)
                .ToListAsync(cancellationToken);
            var draft = await draftGenerator.GenerateAsync(new AssessmentDraftGenerationInput(
                test.Name,
                test.Description,
                test.Instructions,
                request.QuestionCount,
                request.DifficultyMix,
                existingPrompts), cancellationToken);

            if (draft.Count != request.QuestionCount)
            {
                TempData["ErrorMessage"] = "The AI returned an unexpected number of questions. Try generating them again.";
                return RedirectToAction(nameof(Index), new { testId });
            }

            var generatedQuestions = draft.Select(ToGeneratedQuestion).ToList();
            var validationErrors = generatedQuestions
                .Select((question, index) => (Index: index, Errors: ValidateDraftQuestion(question)))
                .Where(result => result.Errors.Count > 0)
                .ToList();
            if (validationErrors.Count > 0)
            {
                logger.LogWarning("AI returned {InvalidQuestionCount} invalid question drafts for assessment {AssessmentId}.", validationErrors.Count, testId);
                TempData["ErrorMessage"] = "The AI returned one or more incomplete questions. Nothing was added; try generating them again.";
                return RedirectToAction(nameof(Index), new { testId });
            }

            var nextOrder = await GetNextQuestionOrderAsync(testId, cancellationToken);
            var additions = generatedQuestions
                .Select((question, index) => ToScreeningQuestion(testId, question, nextOrder + index))
                .ToList();
            var generationReceipt = new AssessmentDraft
            {
                Id = request.RequestId,
                ScreeningTestId = testId,
                IsCsvImport = false,
                ContentJson = SerializeDraftContent(new AssessmentDraftContent
                {
                    Questions = generatedQuestions,
                    AddedIndexes = Enumerable.Range(0, generatedQuestions.Count).ToHashSet(),
                    GenerationRequestId = request.RequestId,
                    DifficultyMix = NormalizeDifficultyMix(request.DifficultyMix)
                })
            };

            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            dbContext.ScreeningQuestions.AddRange(additions);
            dbContext.AssessmentDrafts.Add(generationReceipt);
            await dbContext.SaveChangesAsync(cancellationToken);
            foreach (var question in additions)
            {
                await auditService.RecordAsync(AuditActions.ScreeningQuestionCreated, AuditEntityTypes.ScreeningQuestion, question.Id.ToString(), true,
                    new { question.ScreeningTestId, QuestionType = question.QuestionType.ToString(), Source = "OpenAI generation" }, cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);

            var totalQuestions = await dbContext.ScreeningQuestions.CountAsync(question => question.ScreeningTestId == testId, cancellationToken);
            var page = Math.Max(1, (int)Math.Ceiling(totalQuestions / (double)QuestionPageSize));
            TempData["StatusMessage"] = $"Generated {additions.Count} question{(additions.Count == 1 ? string.Empty : "s")} in the builder as inactive. Review and edit them before activating the assessment questions.";
            return RedirectToAction(nameof(Index), new { testId, page });
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("AI draft generation timed out for assessment {AssessmentId}.", testId);
            TempData["ClientConsoleError"] = "TaskCanceledException: OpenAI question generation timed out.";
            TempData["ErrorMessage"] = "AI question generation timed out. Please try again.";
            return RedirectToAction(nameof(Index), new { testId });
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or JsonException)
        {
            var safeDetails = GetSafeExceptionDetails(exception);
            logger.LogError(
                "AI draft generation failed for assessment {AssessmentId}. Exception chain: {ExceptionDetails}",
                testId,
                safeDetails);
            TempData["ClientConsoleError"] = safeDetails;
            TempData["ErrorMessage"] = "The AI draft could not be generated. Check the OpenAI configuration and try again.";
            return RedirectToAction(nameof(Index), new { testId });
        }
    }

    [HttpGet("drafts/{draftId:guid}")]
    public async Task<IActionResult> DraftReview(int testId, Guid draftId, string? type)
    {
        var draft = await dbContext.AssessmentDrafts.AsNoTracking()
            .Include(item => item.ScreeningTest)
            .SingleOrDefaultAsync(item => item.Id == draftId && item.ScreeningTestId == testId);
        if (draft is null || draft.IsDiscarded)
        {
            return NotFound();
        }

        var content = DeserializeDraftContent(draft.ContentJson);
        var model = ToDraftReviewViewModel(draft, content);
        ViewData["DraftTypeFilter"] = type ?? string.Empty;
        return View("DraftReview", model);
    }

    [HttpGet("drafts/{draftId:guid}/questions/{index:int}/preview")]
    public async Task<IActionResult> DraftQuestionPreview(int testId, Guid draftId, int index)
    {
        var draft = await dbContext.AssessmentDrafts.AsNoTracking()
            .Include(item => item.ScreeningTest)
            .SingleOrDefaultAsync(item => item.Id == draftId && item.ScreeningTestId == testId && !item.IsDiscarded);
        if (draft is null)
        {
            return NotFound();
        }

        var content = DeserializeDraftContent(draft.ContentJson);
        if (index < 0 || index >= content.Questions.Count)
        {
            return NotFound();
        }

        var question = content.Questions[index];
        var previewQuestion = new AssessmentPreviewQuestion(
            question.Prompt,
            GetQuestionTypeName(question.QuestionType),
            question.Choices.ToList(),
            question.PassageText,
            question.AllowsAudioRecording,
            GetSafePreviewImagePath(question.ImagePath),
            string.IsNullOrWhiteSpace(question.AudioPath)
                ? null
                : Url.Action("AudioPreview", "AdminScreeningQuestions", new { testId, fileName = question.AudioPath }),
            question.MaxAudioReplays,
            question.Transcript,
            question.QuestionType,
            question.CorrectAnswer,
            question.ListeningResponseType,
            question.QuestionType switch
            {
                ScreeningQuestionType.TimedNaming => ParseLines(question.TimedNamingItemsText),
                ScreeningQuestionType.WorkingMemorySequence => ParseLines(question.WorkingMemorySequenceText),
                ScreeningQuestionType.TimedMatching => ParseLines(question.TimedMatchingItemsText),
                _ => null
            },
            ParseReferenceMapping(question.TimedMatchingReferenceMappingText),
            question.TimeLimitSeconds,
            question.MemoryDisplayDurationMilliseconds,
            question.ReferenceWordCount,
            question.TargetWordsPerMinute,
            question.PatternDifficultyLevel);
        return View("Preview", new AssessmentPreviewViewModel
        {
            ScreeningTestId = testId,
            Name = "Learner preview",
            Description = string.Empty,
            Instructions = string.Empty,
            ReturnUrl = Url.Action(nameof(DraftReview), new { testId, draftId }),
            ReturnLabel = "Back to draft review",
            Questions = [previewQuestion]
        });
    }

    [HttpPost("drafts/{draftId:guid}/save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveDraft(int testId, Guid draftId, SaveAssessmentDraftViewModel request, CancellationToken cancellationToken)
    {
        var draft = await dbContext.AssessmentDrafts.Include(item => item.ScreeningTest)
            .SingleOrDefaultAsync(item => item.Id == draftId && item.ScreeningTestId == testId, cancellationToken);
        if (draft is null || draft.IsDiscarded)
        {
            return NotFound();
        }

        var current = DeserializeDraftContent(draft.ContentJson);
        if (request.Version != draft.Version)
        {
            return DraftConflict(draft, current, "This draft changed in another tab. Reload it to avoid overwriting those changes.");
        }

        if (!TryCreateDraftContent(request, draft, current, out var content, out var message))
        {
            return DraftValidationFailure(draft, current, message);
        }

        draft.ContentJson = SerializeDraftContent(content);
        draft.Version++;
        draft.UpdatedAtUtc = DateTime.UtcNow;
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
            var latest = await dbContext.AssessmentDrafts.AsNoTracking().SingleOrDefaultAsync(item => item.Id == draftId, cancellationToken);
            return latest is null ? NotFound() : DraftConflict(latest, DeserializeDraftContent(latest.ContentJson), "This draft changed in another tab. Reload it to avoid overwriting those changes.");
        }

        if (IsAjaxRequest())
        {
            return Json(new { saved = true, version = draft.Version, message = "Draft changes saved." });
        }

        TempData["DraftSaved"] = "Draft changes saved.";
        return RedirectToAction(nameof(DraftReview), new { testId, draftId });
    }

    [HttpPost("drafts/{draftId:guid}/add-selected")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddDraftQuestions(int testId, Guid draftId, SaveAssessmentDraftViewModel request, CancellationToken cancellationToken)
    {
        var draft = await dbContext.AssessmentDrafts.Include(item => item.ScreeningTest)
            .SingleOrDefaultAsync(item => item.Id == draftId && item.ScreeningTestId == testId, cancellationToken);
        if (draft is null || draft.IsDiscarded)
        {
            return NotFound();
        }

        var current = DeserializeDraftContent(draft.ContentJson);
        if (request.Version != draft.Version)
        {
            var retriedIndexes = request.SelectedIndexes.Distinct().ToHashSet();
            if (retriedIndexes.Count > 0 && retriedIndexes.All(current.AddedIndexes.Contains))
            {
                return DraftAddSuccess(draftId, testId, current, retriedIndexes.Count, "These drafts were already added. No duplicate questions were created.");
            }

            return DraftConflict(draft, current, "This draft changed in another tab. Reload it before adding questions.");
        }

        if (!TryCreateDraftContent(request, draft, current, out var content, out var message))
        {
            return DraftValidationFailure(draft, current, message);
        }

        var selected = content.SelectedIndexes.Where(index => !content.AddedIndexes.Contains(index)).Distinct().Order().ToList();
        if (selected.Count == 0)
        {
            return DraftValidationFailure(draft, content, "Select at least one question that has not already been added.");
        }

        var errors = new Dictionary<int, IReadOnlyList<string>>();
        foreach (var index in selected)
        {
            var questionErrors = ValidateDraftQuestion(content.Questions[index]);
            if (questionErrors.Count > 0)
            {
                errors[index] = questionErrors;
            }
        }

        content.SelectedIndexes = content.SelectedIndexes.Where(index => !content.AddedIndexes.Contains(index)).Distinct().ToList();
        if (errors.Count > 0)
        {
            await PersistDraftContentAsync(draft, content, cancellationToken);
            return DraftValidationFailure(draft, content, "Review the highlighted questions before adding them.", errors);
        }

        var nextOrder = await GetNextQuestionOrderAsync(testId, cancellationToken);
        var additions = selected.Select((index, offset) => ToScreeningQuestion(testId, content.Questions[index], nextOrder + offset)).ToList();
        foreach (var index in selected)
        {
            content.AddedIndexes.Add(index);
            content.SelectedIndexes.Remove(index);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await PersistDraftContentAsync(draft, content, cancellationToken);
            dbContext.ScreeningQuestions.AddRange(additions);
            await dbContext.SaveChangesAsync(cancellationToken);
            foreach (var question in additions)
            {
                await auditService.RecordAsync(AuditActions.ScreeningQuestionCreated, AuditEntityTypes.ScreeningQuestion, question.Id.ToString(), true,
                    new { question.ScreeningTestId, QuestionType = question.QuestionType.ToString(), Source = draft.IsCsvImport ? "Reviewed CSV draft" : "Reviewed OpenAI draft" }, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            var latest = await dbContext.AssessmentDrafts.AsNoTracking().SingleOrDefaultAsync(item => item.Id == draftId, cancellationToken);
            if (latest is null) return NotFound();
            var latestContent = DeserializeDraftContent(latest.ContentJson);
            if (selected.All(latestContent.AddedIndexes.Contains))
            {
                return DraftAddSuccess(draftId, testId, latestContent, selected.Count, "These drafts were already added. No duplicate questions were created.");
            }
            return DraftConflict(latest, latestContent, "This draft changed in another tab. Reload it before adding questions.");
        }

        var remaining = content.Questions.Count - content.AddedIndexes.Count;
        var statusMessage = $"Added {additions.Count} reviewed {(draft.IsCsvImport ? "CSV" : "AI")} draft question{(additions.Count == 1 ? string.Empty : "s")} as inactive questions.";
        TempData["StatusMessage"] = statusMessage;
        TempData.Keep("StatusMessage");
        var redirectUrl = Url.Action(nameof(Index), new { testId, resumableDraftId = remaining > 0 ? draftId : (Guid?)null })!;
        if (IsAjaxRequest())
        {
            return Json(new { added = additions.Count, remaining, redirectUrl, message = statusMessage });
        }

        return Redirect(redirectUrl);
    }

    [HttpPost("drafts/{draftId:guid}/discard")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DiscardDraft(int testId, Guid draftId, int version, CancellationToken cancellationToken)
    {
        var draft = await dbContext.AssessmentDrafts.SingleOrDefaultAsync(item => item.Id == draftId && item.ScreeningTestId == testId, cancellationToken);
        if (draft is null)
        {
            return NotFound();
        }

        if (!draft.IsDiscarded && draft.Version != version)
        {
            return DraftConflict(draft, DeserializeDraftContent(draft.ContentJson), "This draft changed in another tab. Reload it before discarding.");
        }

        if (!draft.IsDiscarded && draft.Version == version)
        {
            draft.IsDiscarded = true;
            draft.Version++;
            draft.UpdatedAtUtc = DateTime.UtcNow;
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                dbContext.ChangeTracker.Clear();
                var latest = await dbContext.AssessmentDrafts.AsNoTracking().SingleOrDefaultAsync(item => item.Id == draftId, cancellationToken);
                if (latest is null) return NotFound();
                if (!latest.IsDiscarded) return DraftConflict(latest, DeserializeDraftContent(latest.ContentJson), "This draft changed in another tab. Reload it before discarding.");
            }
        }

        TempData["StatusMessage"] = "The review drafts were discarded. Existing assessment questions were not changed.";
        return RedirectToAction(nameof(Index), new { testId });
    }

    [HttpPost("drafts/{draftId:guid}/regenerate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegenerateDraftQuestion(int testId, Guid draftId, int index, int version, CancellationToken cancellationToken)
    {
        if (!draftGenerator.IsConfigured)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "AI question generation is not configured." });
        }

        var draft = await dbContext.AssessmentDrafts.AsNoTracking()
            .Include(item => item.ScreeningTest)
            .SingleOrDefaultAsync(item => item.Id == draftId && item.ScreeningTestId == testId && !item.IsDiscarded, cancellationToken);
        if (draft is null) return NotFound();
        var content = DeserializeDraftContent(draft.ContentJson);
        if (version != draft.Version || index < 0 || index >= content.Questions.Count || content.AddedIndexes.Contains(index))
        {
            return Conflict(new { error = "This draft changed or was already added. Reload it before regenerating." });
        }

        try
        {
            var assessmentPrompts = await dbContext.ScreeningQuestions.AsNoTracking()
                .Where(question => question.ScreeningTestId == testId)
                .Select(question => question.Prompt)
                .ToListAsync(cancellationToken);
            var existingPrompts = assessmentPrompts.Concat(content.Questions.Where((_, questionIndex) => questionIndex != index).Select(question => question.Prompt)).ToList();
            var replacements = await draftGenerator.GenerateAsync(new AssessmentDraftGenerationInput(
                draft.ScreeningTest.Name,
                draft.ScreeningTest.Description,
                draft.ScreeningTest.Instructions,
                1,
                content.DifficultyMix,
                existingPrompts), cancellationToken);
            if (replacements.Count != 1)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new { error = "AI returned an unexpected number of replacement questions. Your original draft was kept." });
            }

            var replacement = ToGeneratedQuestion(replacements[0]);
            var questionErrors = ValidateDraftQuestion(replacement);
            if (questionErrors.Count > 0)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new { error = "The replacement question was incomplete. Your original draft was kept." });
            }

            var currentRow = await dbContext.AssessmentDrafts.SingleAsync(item => item.Id == draftId, cancellationToken);
            if (currentRow.Version != version || currentRow.IsDiscarded)
            {
                return Conflict(new { error = "This draft changed while the replacement was being generated. Your original draft was kept." });
            }

            content.Questions[index] = replacement;
            currentRow.ContentJson = SerializeDraftContent(content);
            currentRow.Version++;
            currentRow.UpdatedAtUtc = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            TempData["DraftSaved"] = "The active question was regenerated. Review the replacement before adding it.";
            return Json(new { regenerated = true, version = currentRow.Version, redirectUrl = Url.Action(nameof(DraftReview), new { testId, draftId }) });
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("AI draft regeneration timed out for assessment {AssessmentId}, draft {DraftId}.", testId, draftId);
            return StatusCode(StatusCodes.Status504GatewayTimeout, new { error = "AI regeneration timed out. Your original draft was kept. Try again." });
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or JsonException)
        {
            logger.LogWarning("AI draft regeneration failed for assessment {AssessmentId}, draft {DraftId}: {Error}", testId, draftId, GetSafeExceptionDetails(exception));
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "AI regeneration failed. Your original draft was kept. Check the settings and try again." });
        }
    }

    private static string GetSafeExceptionDetails(Exception exception)
    {
        var details = new List<string>();
        for (Exception? current = exception; current is not null && details.Count < 5; current = current.InnerException)
        {
            var message = BearerCredentialPattern.Replace(current.Message, "Bearer [REDACTED]");
            message = OpenAiKeyPattern.Replace(message, "[REDACTED OPENAI KEY]");
            if (message.Length > 400)
            {
                message = message[..400];
            }

            var statusCode = (current as HttpRequestException)?.StatusCode;
            var statusDetails = statusCode.HasValue ? $" (HTTP {(int)statusCode.Value} {statusCode.Value})" : string.Empty;
            details.Add($"{current.GetType().Name}{statusDetails}: {message}");
        }

        return string.Join(" --> ", details);
    }

    private async Task<Guid> CreateDraftAsync(
        int testId,
        bool isCsvImport,
        string difficultyMix,
        List<GeneratedAssessmentQuestionViewModel> questions,
        CancellationToken cancellationToken)
    {
        foreach (var question in questions)
        {
            EnsureChoiceIdentities(question);
        }

        var draft = new AssessmentDraft
        {
            ScreeningTestId = testId,
            IsCsvImport = isCsvImport,
            ContentJson = SerializeDraftContent(new AssessmentDraftContent
            {
                Questions = questions,
                SelectedIndexes = Enumerable.Range(0, questions.Count).ToList(),
                ActiveQuestionIndex = 0,
                DifficultyMix = NormalizeDifficultyMix(difficultyMix)
            })
        };
        dbContext.AssessmentDrafts.Add(draft);
        await dbContext.SaveChangesAsync(cancellationToken);
        return draft.Id;
    }

    private static GeneratedAssessmentQuestionViewModel ToGeneratedQuestion(AssessmentDraftQuestion question)
    {
        var result = new GeneratedAssessmentQuestionViewModel
        {
            Prompt = question.Prompt,
            QuestionType = question.QuestionType,
            CorrectAnswer = question.CorrectAnswer,
            Choices = question.Choices.ToList(),
            AcceptedAnswers = question.AcceptedAnswers.ToList()
        };
        EnsureChoiceIdentities(result);
        return result;
    }

    private static void EnsureChoiceIdentities(GeneratedAssessmentQuestionViewModel question)
    {
        question.Choices ??= [];
        question.AcceptedAnswers ??= [];
        question.ChoiceIds ??= [];
        while (question.ChoiceIds.Count < question.Choices.Count)
        {
            question.ChoiceIds.Add(Guid.NewGuid().ToString("N"));
        }
        if (question.ChoiceIds.Count > question.Choices.Count)
        {
            question.ChoiceIds = question.ChoiceIds.Take(question.Choices.Count).ToList();
        }

        if (string.IsNullOrWhiteSpace(question.CorrectChoiceId) && !string.IsNullOrWhiteSpace(question.CorrectAnswer))
        {
            var index = question.Choices.FindIndex(choice => string.Equals(choice.Trim(), question.CorrectAnswer.Trim(), StringComparison.OrdinalIgnoreCase));
            if (index >= 0 && index < question.ChoiceIds.Count)
            {
                question.CorrectChoiceId = question.ChoiceIds[index];
            }
        }

        if (!string.IsNullOrWhiteSpace(question.CorrectChoiceId))
        {
            var correctIndex = question.ChoiceIds.FindIndex(id => string.Equals(id, question.CorrectChoiceId, StringComparison.Ordinal));
            if (correctIndex >= 0 && correctIndex < question.Choices.Count)
            {
                question.CorrectAnswer = question.Choices[correctIndex].Trim();
            }
        }
    }

    private bool TryCreateDraftContent(
        SaveAssessmentDraftViewModel request,
        AssessmentDraft draft,
        AssessmentDraftContent current,
        out AssessmentDraftContent content,
        out string message)
    {
        var maximum = draft.IsCsvImport ? 40 : 20;
        if (request.ScreeningTestId != draft.ScreeningTestId || request.Questions is null || request.Questions.Count is < 1 || request.Questions.Count > maximum)
        {
            content = current;
            message = "The draft review data is incomplete or exceeds the supported question limit. Reload the draft and try again.";
            return false;
        }

        var questions = request.Questions.Select(CloneDraftQuestion).ToList();
        foreach (var question in questions)
        {
            if (question.AcceptedAnswersText.Length > 8000)
            {
                content = current;
                message = "Accepted answers are limited to 8,000 characters.";
                return false;
            }
            if (question.AcceptedAnswersText.Length > 0) question.AcceptedAnswers = ParseDraftLines(question.AcceptedAnswersText);
            else if (question.AcceptedAnswers.Count > 0) question.AcceptedAnswersText = string.Join(Environment.NewLine, question.AcceptedAnswers);
            if (question.ChoicesText.Length > 0) question.Choices = ParseDraftLines(question.ChoicesText);
            else if (question.QuestionType == ScreeningQuestionType.TimedMatching && question.Choices.Count > 0) question.ChoicesText = string.Join(Environment.NewLine, question.Choices);
            if (!Enum.IsDefined(question.QuestionType))
            {
                content = current;
                message = "The draft contains an unsupported question type. Reload the draft and try again.";
                return false;
            }
            EnsureChoiceIdentities(question);
        }

        var added = current.AddedIndexes.Where(index => index >= 0 && index < questions.Count).ToHashSet();
        var selected = request.SelectedIndexes?.Where(index => index >= 0 && index < questions.Count && !added.Contains(index)).Distinct().Order().ToList() ?? [];
        content = new AssessmentDraftContent
        {
            Questions = questions,
            SelectedIndexes = selected,
            AddedIndexes = added,
            ActiveQuestionIndex = Math.Clamp(request.ActiveQuestionIndex, 0, questions.Count - 1),
            DifficultyMix = NormalizeDifficultyMix(request.DifficultyMix)
        };
        message = string.Empty;
        return true;
    }

    private static GeneratedAssessmentQuestionViewModel CloneDraftQuestion(GeneratedAssessmentQuestionViewModel question) => new()
    {
        Prompt = question.Prompt?.Trim() ?? string.Empty,
        QuestionType = question.QuestionType,
        CorrectAnswer = question.CorrectAnswer?.Trim() ?? string.Empty,
        Choices = question.Choices?.Select(choice => choice?.Trim() ?? string.Empty).ToList() ?? [],
        ChoicesText = question.ChoicesText ?? string.Empty,
        ChoiceIds = question.ChoiceIds?.Select(id => id?.Trim() ?? string.Empty).ToList() ?? [],
        CorrectChoiceId = question.CorrectChoiceId?.Trim(),
        AcceptedAnswers = question.AcceptedAnswers?.Select(answer => answer?.Trim() ?? string.Empty).ToList() ?? [],
        AcceptedAnswersText = question.AcceptedAnswersText ?? string.Empty,
        PassageText = question.PassageText,
        ReferenceWordCount = question.ReferenceWordCount,
        TargetWordsPerMinute = question.TargetWordsPerMinute,
        TimeLimitSeconds = question.TimeLimitSeconds,
        TimedMatchingReferenceMappingText = question.TimedMatchingReferenceMappingText,
        TimedMatchingItemsText = question.TimedMatchingItemsText,
        PatternDifficultyLevel = question.PatternDifficultyLevel,
        TimedNamingItemsText = question.TimedNamingItemsText,
        WorkingMemorySequenceText = question.WorkingMemorySequenceText,
        MemoryDisplayDurationMilliseconds = question.MemoryDisplayDurationMilliseconds,
        AudioPath = question.AudioPath,
        ListeningResponseType = question.ListeningResponseType,
        Transcript = question.Transcript,
        MaxAudioReplays = question.MaxAudioReplays,
        ImagePath = question.ImagePath,
        AllowsAudioRecording = question.AllowsAudioRecording,
        Weight = question.Weight
    };

    private static List<string> ValidateDraftQuestion(GeneratedAssessmentQuestionViewModel question)
    {
        var errors = new List<string>();
        if (question.Prompt.Trim().Length is < 3 or > 1000)
        {
            errors.Add("Enter a prompt between 3 and 1,000 characters.");
        }
        if (question.Weight is < 0.1 or > 100)
        {
            errors.Add("Enter a question weight between 0.1 and 100.");
        }
        if (question.AcceptedAnswers.Count > 10 || question.AcceptedAnswers.Any(answer => answer.Length > 300))
        {
            errors.Add("Use no more than 10 accepted answers, each 300 characters or fewer.");
        }

        var choiceTypes = question.QuestionType is ScreeningQuestionType.MultipleChoice or ScreeningQuestionType.PhonologicalChoice or ScreeningQuestionType.PatternReasoning ||
                          question.QuestionType == ScreeningQuestionType.ListeningResponse && NormalizeResponseType(question.ListeningResponseType) == "multiple-choice";
        if (choiceTypes)
        {
            var choices = question.Choices.Select(choice => choice.Trim()).Where(choice => choice.Length > 0).ToList();
            if (choices.Count is < 2 or > 10 || choices.Distinct(StringComparer.OrdinalIgnoreCase).Count() != choices.Count)
            {
                errors.Add("Enter 2 to 10 different answer options.");
            }

            var correctIndex = question.ChoiceIds.FindIndex(id => string.Equals(id, question.CorrectChoiceId, StringComparison.Ordinal));
            if (question.ChoiceIds.Distinct(StringComparer.Ordinal).Count() != question.ChoiceIds.Count)
            {
                errors.Add("Each answer option needs its own identity. Remove and re-add any duplicate option.");
            }
            if (correctIndex < 0 || correctIndex >= question.Choices.Count || string.IsNullOrWhiteSpace(question.Choices[correctIndex]))
            {
                errors.Add("Select the correct answer from the options.");
            }
            else
            {
                question.CorrectAnswer = question.Choices[correctIndex].Trim();
            }
        }
        var needsTextAnswer = question.QuestionType is ScreeningQuestionType.TextResponse or ScreeningQuestionType.WordReading or
            ScreeningQuestionType.NonWordReading or ScreeningQuestionType.Spelling or ScreeningQuestionType.ReadingPassage or
            ScreeningQuestionType.ReadingFluency or ScreeningQuestionType.WorkingMemorySequence ||
            question.QuestionType == ScreeningQuestionType.ListeningResponse && NormalizeResponseType(question.ListeningResponseType) == "text-response";
        if (needsTextAnswer && string.IsNullOrWhiteSpace(question.CorrectAnswer) && question.AcceptedAnswers.All(string.IsNullOrWhiteSpace))
        {
            errors.Add("Enter a correct answer or at least one accepted answer.");
        }

        switch (question.QuestionType)
        {
            case ScreeningQuestionType.ReadingPassage:
            case ScreeningQuestionType.ReadingFluency:
                if (string.IsNullOrWhiteSpace(question.PassageText) || question.PassageText.Length > 8000)
                    errors.Add("Enter passage text up to 8,000 characters.");
                if (question.QuestionType == ScreeningQuestionType.ReadingFluency && question.TargetWordsPerMinute is < 1 or > 1000)
                    errors.Add("Enter a target pace from 1 to 1,000 words per minute or leave it blank.");
                break;
            case ScreeningQuestionType.ListeningResponse:
                if (!ScreeningAudioSource.TryGetContentType(question.AudioPath, out _, out _))
                    errors.Add("Choose a supported listening audio file before adding this question.");
                var responseType = NormalizeResponseType(question.ListeningResponseType);
                if (responseType == "audio-response" && !question.AllowsAudioRecording)
                    errors.Add("Allow learner audio recording for an audio response.");
                if (responseType == "text-response" && string.IsNullOrWhiteSpace(question.CorrectAnswer))
                    errors.Add("Enter the expected spoken response.");
                break;
            case ScreeningQuestionType.TimedNaming:
                if (ParseDraftLines(question.TimedNamingItemsText).Count != 20)
                    errors.Add("Enter exactly 20 naming items.");
                if (question.TimeLimitSeconds is null or < 1 or > 600)
                    errors.Add("Enter a time limit from 1 to 600 seconds.");
                break;
            case ScreeningQuestionType.WorkingMemorySequence:
                if (ParseDraftLines(question.WorkingMemorySequenceText).Count is < 2 or > 12)
                    errors.Add("Enter 2 to 12 sequence items.");
                if (question.MemoryDisplayDurationMilliseconds is null or < 1000 or > 30000)
                    errors.Add("Enter a display duration from 1,000 to 30,000 milliseconds.");
                if (string.IsNullOrWhiteSpace(question.CorrectAnswer))
                    question.CorrectAnswer = string.Join(" ", ParseDraftLines(question.WorkingMemorySequenceText));
                break;
            case ScreeningQuestionType.TimedMatching:
                if (question.TimeLimitSeconds is null or < 1 or > 600)
                    errors.Add("Enter a time limit from 1 to 600 seconds.");
                var mapping = ParseDraftMapping(question.TimedMatchingReferenceMappingText);
                if (mapping.Count < 2)
                    errors.Add("Enter at least two reference pairs in item = symbol format.");
                var items = ParseDraftLines(question.TimedMatchingItemsText);
                if (items.Count == 0 || items.Any(item => !mapping.ContainsKey(item)))
                    errors.Add("Enter matching items that appear in the reference mapping.");
                break;
            case ScreeningQuestionType.PatternReasoning:
                if (question.PatternDifficultyLevel is < 1 or > 5)
                    errors.Add("Choose a pattern difficulty from 1 to 5.");
                break;
        }
        if (!string.IsNullOrWhiteSpace(question.ImagePath) && GetSafePreviewImagePath(question.ImagePath) is null)
            errors.Add("Use a safe image URL or an application image path.");
        return errors;
    }

    private static ScreeningQuestion ToScreeningQuestion(int testId, GeneratedAssessmentQuestionViewModel question, int sortOrder)
    {
        EnsureChoiceIdentities(question);
        var metadata = new ScreeningQuestionMetadata
        {
            ChoiceOptions = question.Choices.Count == 0 ? null : question.Choices.Select(value => value.Trim()).Where(value => value.Length > 0).ToList(),
            PassageText = Normalize(question.PassageText),
            ReferenceWordCount = question.ReferenceWordCount,
            TargetWordsPerMinute = question.TargetWordsPerMinute,
            TimeLimitSeconds = question.TimeLimitSeconds,
            MaxAudioReplays = question.MaxAudioReplays,
            AudioDurationSeconds = null,
            Transcript = Normalize(question.Transcript),
            ResponseType = question.QuestionType == ScreeningQuestionType.ListeningResponse ? NormalizeResponseType(question.ListeningResponseType) : null,
            TimedNamingItems = ParseDraftLines(question.TimedNamingItemsText),
            WorkingMemorySequence = ParseDraftLines(question.WorkingMemorySequenceText),
            MemoryDisplayDurationMilliseconds = question.MemoryDisplayDurationMilliseconds,
            TimedMatchingReferenceMapping = ParseDraftMapping(question.TimedMatchingReferenceMappingText),
            TimedMatchingItems = ParseDraftLines(question.TimedMatchingItemsText),
            PatternDifficultyLevel = question.PatternDifficultyLevel
        };
        var metadataJson = JsonSerializer.Serialize(metadata, MetadataOptions);
        if (metadataJson == "{}") metadataJson = null;
        return new ScreeningQuestion
        {
            ScreeningTestId = testId,
            Prompt = question.Prompt.Trim(),
            QuestionType = question.QuestionType,
            CorrectAnswer = Normalize(question.CorrectAnswer),
            AcceptedAnswersJson = question.AcceptedAnswers.Count == 0 ? null : JsonSerializer.Serialize(question.AcceptedAnswers.Select(answer => answer.Trim()).Where(answer => answer.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)),
            Weight = question.Weight,
            SortOrder = sortOrder,
            IsActive = false,
            AllowsAudioRecording = question.AllowsAudioRecording,
            AudioPath = Normalize(question.AudioPath),
            ImagePath = Normalize(question.ImagePath),
            MetadataJson = metadataJson
        };
    }

    private AssessmentDraftReviewViewModel ToDraftReviewViewModel(AssessmentDraft draft, AssessmentDraftContent content, string? error = null, IReadOnlyDictionary<int, IReadOnlyList<string>>? errors = null, string? success = null) => new()
    {
        DraftId = draft.Id,
        ScreeningTestId = draft.ScreeningTestId,
        ScreeningTestName = draft.ScreeningTest.Name,
        IsCsvImport = draft.IsCsvImport,
        OpenAiConfigured = draftGenerator.IsConfigured,
        Version = draft.Version,
        ActiveQuestionIndex = content.ActiveQuestionIndex,
        DifficultyMix = content.DifficultyMix,
        SelectedIndexes = content.SelectedIndexes,
        AddedIndexes = content.AddedIndexes.Order().ToList(),
        Questions = content.Questions,
        ErrorMessage = error,
        QuestionErrors = errors ?? new Dictionary<int, IReadOnlyList<string>>(),
        SuccessMessage = success
    };

    private static AssessmentDraftContent DeserializeDraftContent(string? json)
    {
        try
        {
            var content = string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<AssessmentDraftContent>(json, MetadataOptions);
            return content ?? new AssessmentDraftContent();
        }
        catch (JsonException)
        {
            return new AssessmentDraftContent();
        }
    }

    private static string SerializeDraftContent(AssessmentDraftContent content) => JsonSerializer.Serialize(content, MetadataOptions);

    private async Task PersistDraftContentAsync(AssessmentDraft draft, AssessmentDraftContent content, CancellationToken cancellationToken)
    {
        draft.ContentJson = SerializeDraftContent(content);
        draft.Version++;
        draft.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private IActionResult DraftValidationFailure(AssessmentDraft draft, AssessmentDraftContent content, string message, IReadOnlyDictionary<int, IReadOnlyList<string>>? errors = null)
    {
        if (IsAjaxRequest())
        {
            return UnprocessableEntity(new
            {
                message,
                errors = errors ?? new Dictionary<int, IReadOnlyList<string>>(),
                invalidIndexes = errors?.Keys.Order().ToList() ?? [],
                version = draft.Version
            });
        }

        return View("DraftReview", ToDraftReviewViewModel(draft, content, message, errors));
    }

    private IActionResult DraftConflict(AssessmentDraft draft, AssessmentDraftContent content, string message)
    {
        if (IsAjaxRequest()) return Conflict(new { message, reloadUrl = Url.Action(nameof(DraftReview), new { testId = draft.ScreeningTestId, draftId = draft.Id }) });
        TempData["ErrorMessage"] = message;
        return RedirectToAction(nameof(DraftReview), new { testId = draft.ScreeningTestId, draftId = draft.Id });
    }

    private IActionResult DraftAddSuccess(Guid draftId, int testId, AssessmentDraftContent content, int addedCount, string message)
    {
        var remaining = content.Questions.Count - content.AddedIndexes.Count;
        TempData["StatusMessage"] = message;
        var redirectUrl = Url.Action(nameof(Index), new { testId, resumableDraftId = remaining > 0 ? draftId : (Guid?)null })!;
        if (IsAjaxRequest()) return Json(new { added = addedCount, remaining, duplicate = true, redirectUrl, message });
        return Redirect(redirectUrl);
    }

    private bool IsAjaxRequest() => string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeDifficultyMix(string? value) => value is "Mostly accessible" or "Progressive challenge" ? value : "Balanced";

    private static string NormalizeResponseType(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "text-response" => "text-response",
        "audio-response" => "audio-response",
        _ => "multiple-choice"
    };

    private static List<string> ParseDraftLines(string? value) => (value ?? string.Empty)
        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(line => line.Length > 0)
        .ToList();

    private static Dictionary<string, string> ParseDraftMapping(string? value)
    {
        var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in ParseDraftLines(value))
        {
            var parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && parts[0].Length > 0 && parts[1].Length > 0)
            {
                mapping[parts[0]] = parts[1];
            }
        }
        return mapping;
    }

    private static GeneratedAssessmentQuestionViewModel CloneDraftQuestionFromModel(GeneratedAssessmentQuestionViewModel question) => CloneDraftQuestion(question);

    [HttpPost("generate/add-selected")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddGenerated(int testId, AddGeneratedAssessmentQuestionsViewModel request, CancellationToken cancellationToken)
    {
        if (request.ScreeningTestId != testId || request.Questions.Count is < 1 or > 20 ||
            request.SelectedIndexes.Count is < 1 or > 20 ||
            request.SelectedIndexes.Any(index => index < 0 || index >= request.Questions.Count))
        {
            return BadRequest();
        }

        return await SaveSelectedQuestionsAsync(testId, request, "OpenAI draft reviewed by administrator", true, cancellationToken);
    }

    [HttpPost("import/add-selected")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddInterpretedCsv(int testId, AddGeneratedAssessmentQuestionsViewModel request, CancellationToken cancellationToken)
    {
        if (request.ScreeningTestId != testId || request.Questions.Count is < 1 or > 40 ||
            request.SelectedIndexes.Count is < 1 or > 40 ||
            request.SelectedIndexes.Any(index => index < 0 || index >= request.Questions.Count))
        {
            return BadRequest();
        }

        return await SaveSelectedQuestionsAsync(testId, request, "CSV interpreted by OpenAI and reviewed by administrator", false, cancellationToken);
    }

    private async Task<IActionResult> SaveSelectedQuestionsAsync(
        int testId,
        AddGeneratedAssessmentQuestionsViewModel request,
        string auditSource,
        bool generatedDrafts,
        CancellationToken cancellationToken)
    {

        if (!await dbContext.ScreeningTests.AnyAsync(test => test.Id == testId, cancellationToken))
        {
            return NotFound();
        }

        var selected = request.SelectedIndexes.Distinct().Order().Select(index => request.Questions[index]).ToList();
        if (selected.Any(question => !IsValidGeneratedQuestion(question)))
        {
            TempData["ErrorMessage"] = generatedDrafts
                ? "One or more selected questions were incomplete. Generate a fresh draft and review it again."
                : "One or more selected questions were incomplete. Return to the CSV import and review the questions again.";
            return RedirectToAction(nameof(Index), new { testId });
        }

        var nextOrder = await GetNextQuestionOrderAsync(testId, cancellationToken);
        var additions = selected.Select((question, index) => new ScreeningQuestion
        {
            ScreeningTestId = testId,
            Prompt = question.Prompt.Trim(),
            QuestionType = question.QuestionType,
            CorrectAnswer = Normalize(question.CorrectAnswer),
            AcceptedAnswersJson = question.AcceptedAnswers.Count == 0 ? null : JsonSerializer.Serialize(question.AcceptedAnswers.Select(answer => answer.Trim()).Where(answer => answer.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)),
            Weight = 1.0,
            SortOrder = nextOrder + index,
            IsActive = false,
            MetadataJson = question.QuestionType == ScreeningQuestionType.MultipleChoice
                ? JsonSerializer.Serialize(new ScreeningQuestionMetadata { ChoiceOptions = question.Choices.Select(choice => choice.Trim()).Where(choice => choice.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList() }, MetadataOptions)
                : null
        }).ToList();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        dbContext.ScreeningQuestions.AddRange(additions);
        await dbContext.SaveChangesAsync(cancellationToken);
        foreach (var question in additions)
        {
            await auditService.RecordAsync(AuditActions.ScreeningQuestionCreated, AuditEntityTypes.ScreeningQuestion, question.Id.ToString(), true,
                new { question.ScreeningTestId, QuestionType = question.QuestionType.ToString(), Source = auditSource }, cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);

        TempData["StatusMessage"] = generatedDrafts
            ? $"Added {additions.Count} reviewed AI draft question{(additions.Count == 1 ? string.Empty : "s")} as inactive questions. Activate each one when it is ready."
            : $"Imported {additions.Count} reviewed CSV question{(additions.Count == 1 ? string.Empty : "s")} as inactive questions. Activate each one when it is ready.";
        return RedirectToAction(nameof(Index), new { testId });
    }

    [HttpPost("activate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Activate(int testId)
    {
        var test = await dbContext.ScreeningTests.FindAsync(testId);
        if (test is null)
        {
            return NotFound();
        }

        var readiness = await readinessService.GetReadinessAsync(testId);
        if (readiness is null)
        {
            return NotFound();
        }
        if (!readiness.IsReady)
        {
            TempData["ErrorMessage"] = readiness.ActivationFeedback;
            return RedirectToAction(nameof(Index), new { testId });
        }

        test.IsActive = true;
        await dbContext.SaveChangesAsync();
        await auditService.RecordAsync(AuditActions.ScreeningTestActivated, AuditEntityTypes.ScreeningTest, testId.ToString(), true);
        TempData["StatusMessage"] = "The assessment is active.";
        return RedirectToAction(nameof(Index), new { testId });
    }

    [HttpPost("deactivate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deactivate(int testId)
    {
        var test = await dbContext.ScreeningTests.FindAsync(testId);
        if (test is null)
        {
            return NotFound();
        }

        test.IsActive = false;
        await dbContext.SaveChangesAsync();
        await auditService.RecordAsync(AuditActions.ScreeningTestDeactivated, AuditEntityTypes.ScreeningTest, testId.ToString(), true);
        TempData["StatusMessage"] = "The assessment is inactive. Its questions and related records are preserved.";
        return RedirectToAction(nameof(Index), new { testId });
    }

    private async Task<AssessmentBuilderViewModel?> BuildViewModelAsync(int testId, int page, IReadOnlyList<string>? importErrors = null, string? importMessage = null, Guid? resumableDraftId = null)
    {
        var test = await dbContext.ScreeningTests.AsNoTracking().SingleOrDefaultAsync(item => item.Id == testId);
        if (test is null)
        {
            return null;
        }

        var questionQuery = dbContext.ScreeningQuestions.AsNoTracking().Where(question => question.ScreeningTestId == testId);
        var totalCount = await questionQuery.CountAsync();
        var activeCount = await questionQuery.CountAsync(question => question.IsActive);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)QuestionPageSize));
        var currentPage = Math.Clamp(page, 1, totalPages);
        var questions = await questionQuery
            .OrderBy(question => question.SortOrder)
            .ThenBy(question => question.Id)
            .Skip((currentPage - 1) * QuestionPageSize)
            .Take(QuestionPageSize)
            .Select(question => new AssessmentBuilderQuestionItem(
                question.Id,
                question.Prompt,
                GetQuestionTypeName(question.QuestionType),
                question.SortOrder,
                question.IsActive,
                question.AllowsAudioRecording,
                question.ImagePath != null))
            .ToListAsync();

        var resumableDraft = resumableDraftId is Guid draftId
            ? await dbContext.AssessmentDrafts.AsNoTracking()
                .SingleOrDefaultAsync(draft => draft.Id == draftId && draft.ScreeningTestId == testId && !draft.IsDiscarded)
            : null;
        var remainingDraftCount = resumableDraft is null
            ? 0
            : Math.Max(0, DeserializeDraftContent(resumableDraft.ContentJson).Questions.Count - DeserializeDraftContent(resumableDraft.ContentJson).AddedIndexes.Count);

        return new AssessmentBuilderViewModel
        {
            ScreeningTestId = test.Id,
            Name = test.Name,
            Slug = test.Slug,
            Description = test.Description,
            Instructions = test.Instructions,
            IsActive = test.IsActive,
            OpenAiConfigured = draftGenerator.IsConfigured,
            QuestionCount = totalCount,
            ActiveQuestionCount = activeCount,
            Questions = questions,
            CurrentPage = currentPage,
            TotalPages = totalPages,
            ImportErrors = importErrors ?? [],
            ImportMessage = importMessage,
            ResumableDraftId = remainingDraftCount > 0 ? resumableDraft?.Id : null,
            ResumableDraftCount = remainingDraftCount,
            GenerationRequestId = Guid.NewGuid()
        };
    }

    private async Task<IActionResult> IndexWithImportResultAsync(
        int testId,
        IReadOnlyList<string> errors,
        CancellationToken cancellationToken,
        string? message = null)
    {
        var model = await BuildViewModelAsync(testId, 1, errors, message);
        return model is null ? NotFound() : View(nameof(Index), model);
    }

    private async Task<int> GetNextQuestionOrderAsync(int testId, CancellationToken cancellationToken = default) =>
        (await dbContext.ScreeningQuestions.Where(question => question.ScreeningTestId == testId)
            .MaxAsync(question => (int?)question.SortOrder, cancellationToken)) + 1 ?? 1;

    private static ScreeningQuestionMetadata DeserializeMetadata(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return new ScreeningQuestionMetadata();
        }

        try
        {
            return JsonSerializer.Deserialize<ScreeningQuestionMetadata>(metadataJson, MetadataOptions) ?? new ScreeningQuestionMetadata();
        }
        catch (JsonException)
        {
            return new ScreeningQuestionMetadata();
        }
    }

    private static string GetQuestionTypeName(ScreeningQuestionType type) =>
        typeof(ScreeningQuestionType).GetMember(type.ToString()).FirstOrDefault()?.GetCustomAttribute<DisplayAttribute>()?.GetName() ?? type.ToString();

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static List<string> ParseLines(string? value) => string.IsNullOrWhiteSpace(value)
        ? []
        : value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.Length > 0)
            .ToList();

    private static IReadOnlyDictionary<string, string> ParseReferenceMapping(string? value)
    {
        var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in ParseLines(value))
        {
            var separator = line.IndexOf('=');
            if (separator <= 0 || separator == line.Length - 1)
            {
                continue;
            }

            var item = line[..separator].Trim();
            var symbol = line[(separator + 1)..].Trim();
            if (item.Length > 0 && symbol.Length > 0)
            {
                mapping[item] = symbol;
            }
        }

        return mapping;
    }

    private static string? GetSafePreviewImagePath(string? value)
    {
        var path = Normalize(value);
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

    private static bool IsValidGeneratedQuestion(GeneratedAssessmentQuestionViewModel question)
    {
        if (question.Prompt.Trim().Length is < 3 or > 1000 || question.CorrectAnswer.Trim().Length > 500 ||
            question.AcceptedAnswers.Count > 10 || question.AcceptedAnswers.Any(answer => answer.Trim().Length > 300))
        {
            return false;
        }

        if (question.QuestionType == ScreeningQuestionType.MultipleChoice)
        {
            var choices = question.Choices.Select(choice => choice.Trim()).Where(choice => choice.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return choices.Count is >= 2 and <= 10 && choices.Contains(question.CorrectAnswer.Trim(), StringComparer.OrdinalIgnoreCase);
        }

        return question.QuestionType is ScreeningQuestionType.TextResponse or ScreeningQuestionType.WordReading or
            ScreeningQuestionType.NonWordReading or ScreeningQuestionType.Spelling && question.Choices.Count == 0 &&
            (!string.IsNullOrWhiteSpace(question.CorrectAnswer) || question.AcceptedAnswers.Any(answer => !string.IsNullOrWhiteSpace(answer)));
    }

    private static CsvValidationResult ValidateCsvRows(IReadOnlyList<List<string>> rows)
    {
        if (rows.Count < 2)
        {
            return new CsvValidationResult([], ["The CSV must include a header and at least one question row."]);
        }

        var headers = rows[0].Select(header => header.Trim()).ToList();
        var headerIndexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < headers.Count; index++)
        {
            if (!headerIndexes.TryAdd(headers[index], index))
            {
                return new CsvValidationResult([], [$"The CSV contains a duplicate column: {headers[index]}."]);
            }
        }

        foreach (var required in new[] { "QuestionType", "Prompt", "CorrectAnswer", "Choices", "AcceptedAnswers" })
        {
            if (!headerIndexes.ContainsKey(required))
            {
                return new CsvValidationResult([], [$"The CSV is missing the {required} column. Download the template for the required headers."]);
            }
        }

        var validQuestions = new List<ImportedQuestion>();
        var errors = new List<string>();
        for (var rowIndex = 1; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            if (row.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            if (row.Count != headers.Count)
            {
                errors.Add($"Row {rowIndex + 1}: expected {headers.Count} columns but found {row.Count}.");
                continue;
            }

            string Value(string header) => row[headerIndexes[header]].Trim();
            var typeText = Value("QuestionType");
            var prompt = Value("Prompt");
            var answer = Value("CorrectAnswer");
            var choices = SplitDelimitedValues(Value("Choices"));
            var acceptedAnswers = SplitDelimitedValues(Value("AcceptedAnswers"));
            if (!TryParseSimpleQuestionType(typeText, out var type))
            {
                errors.Add($"Row {rowIndex + 1}: {typeText} is unsupported. Use MultipleChoice, TextResponse, WordReading, NonWordReading, or Spelling.");
                continue;
            }

            if (prompt.Length is < 3 or > 1000 || answer.Length is < 1 or > 500)
            {
                errors.Add($"Row {rowIndex + 1}: enter a prompt (3–1000 characters) and correct answer (1–500 characters).");
                continue;
            }

            if (type == ScreeningQuestionType.MultipleChoice &&
                (choices.Distinct(StringComparer.OrdinalIgnoreCase).Count() is < 2 or > 10 || !choices.Contains(answer, StringComparer.OrdinalIgnoreCase)))
            {
                errors.Add($"Row {rowIndex + 1}: multiple-choice questions need 2–10 different choices and the correct answer must match a choice.");
                continue;
            }

            if (type != ScreeningQuestionType.MultipleChoice && choices.Count > 0)
            {
                errors.Add($"Row {rowIndex + 1}: only MultipleChoice rows may include Choices.");
                continue;
            }

            validQuestions.Add(new ImportedQuestion(type, prompt, answer, choices, acceptedAnswers));
        }

        if (validQuestions.Count == 0 && errors.Count == 0)
        {
            errors.Add("The CSV does not contain any question rows.");
        }

        return errors.Count > 0 ? new CsvValidationResult([], errors) : new CsvValidationResult(validQuestions, []);
    }

    private static List<string> SplitDelimitedValues(string value) => value
        .Split(['|', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(item => item.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static bool TryParseSimpleQuestionType(string value, out ScreeningQuestionType type)
    {
        if (Enum.TryParse(value, ignoreCase: true, out type))
        {
            return IsSimpleQuestionType(type);
        }

        type = value.Trim().ToLowerInvariant() switch
        {
            "multiple choice" => ScreeningQuestionType.MultipleChoice,
            "text response" => ScreeningQuestionType.TextResponse,
            "word reading" => ScreeningQuestionType.WordReading,
            "non-word reading" => ScreeningQuestionType.NonWordReading,
            "spelling" => ScreeningQuestionType.Spelling,
            _ => 0
        };
        return IsSimpleQuestionType(type);
    }

    private static bool IsSimpleQuestionType(ScreeningQuestionType type) => type is ScreeningQuestionType.MultipleChoice or
        ScreeningQuestionType.TextResponse or ScreeningQuestionType.WordReading or ScreeningQuestionType.NonWordReading or ScreeningQuestionType.Spelling;

    private static char DetectCsvDelimiter(string csv)
    {
        var firstRecord = new System.Text.StringBuilder();
        var inQuotes = false;
        for (var index = 0; index < csv.Length; index++)
        {
            var character = csv[index];
            if (character == '"')
            {
                if (inQuotes && index + 1 < csv.Length && csv[index + 1] == '"')
                {
                    firstRecord.Append("\"\"");
                    index++;
                    continue;
                }
                inQuotes = !inQuotes;
            }
            if (!inQuotes && (character is '\r' or '\n'))
            {
                break;
            }
            firstRecord.Append(character);
        }

        var candidate = ',';
        var highestCount = 0;
        foreach (var delimiter in new[] { ',', ';', '\t' })
        {
            var count = CountUnquotedDelimiters(firstRecord.ToString(), delimiter);
            if (count > highestCount)
            {
                candidate = delimiter;
                highestCount = count;
            }
        }
        return candidate;
    }

    private static int CountUnquotedDelimiters(string record, char delimiter)
    {
        var count = 0;
        var inQuotes = false;
        for (var index = 0; index < record.Length; index++)
        {
            if (record[index] == '"')
            {
                if (inQuotes && index + 1 < record.Length && record[index + 1] == '"')
                {
                    index++;
                    continue;
                }
                inQuotes = !inQuotes;
            }
            else if (!inQuotes && record[index] == delimiter)
            {
                count++;
            }
        }
        return count;
    }

    private static List<List<string>> ParseCsv(string csv) => ParseCsv(csv, DetectCsvDelimiter(csv));

    private static List<List<string>> ParseCsv(string csv, char delimiter)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new System.Text.StringBuilder();
        var inQuotes = false;
        var closedQuote = false;

        for (var index = 0; index < csv.Length; index++)
        {
            var character = csv[index];
            if (inQuotes)
            {
                if (character == '"' && index + 1 < csv.Length && csv[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else if (character == '"')
                {
                    inQuotes = false;
                    closedQuote = true;
                }
                else
                {
                    field.Append(character);
                }
                continue;
            }

            if (closedQuote && character != delimiter && character is not '\r' and not '\n')
            {
                throw new InvalidDataException("The CSV contains text after a closing quote.");
            }

            if (character == '"')
            {
                if (field.Length != 0 || closedQuote)
                {
                    throw new InvalidDataException("The CSV contains an unexpected quote.");
                }
                inQuotes = true;
            }
            else if (character == delimiter)
            {
                row.Add(field.ToString());
                field.Clear();
                closedQuote = false;
            }
            else if (character is '\r' or '\n')
            {
                if (character == '\r' && index + 1 < csv.Length && csv[index + 1] == '\n')
                {
                    index++;
                }
                row.Add(field.ToString());
                rows.Add(row);
                row = [];
                field.Clear();
                closedQuote = false;
            }
            else
            {
                field.Append(character);
            }
        }

        if (inQuotes)
        {
            throw new InvalidDataException("The CSV contains an unclosed quoted field.");
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        return rows;
    }

    private sealed class AssessmentDraftContent
    {
        public List<GeneratedAssessmentQuestionViewModel> Questions { get; set; } = [];
        public List<int> SelectedIndexes { get; set; } = [];
        public HashSet<int> AddedIndexes { get; set; } = [];
        public int ActiveQuestionIndex { get; set; }
        public string DifficultyMix { get; set; } = "Balanced";
        public Guid? GenerationRequestId { get; set; }
    }

    private sealed record CsvValidationResult(List<ImportedQuestion> Questions, List<string> Errors);
    private sealed record ImportedQuestion(ScreeningQuestionType Type, string Prompt, string CorrectAnswer, List<string> Choices, List<string> AcceptedAnswers);
}
