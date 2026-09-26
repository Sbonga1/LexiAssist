using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Audit;
using LexiAssist.Web.Models.Identity;
using LexiAssist.Web.Models.Notifications;
using LexiAssist.Web.Models.Referrals;
using LexiAssist.Web.Models.Reports;
using LexiAssist.Web.Models.Recordings;
using LexiAssist.Web.Models.Screening;
using LexiAssist.Web.Services;
using LexiAssist.Web.Services.Interfaces;
using LexiAssist.Web.ViewModels.Referrals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LexiAssist.Web.Controllers;

[Authorize(Roles = ApplicationRoles.Psychologist)]
[Route("psychologist/referrals")]
public sealed class PsychologistReferralsController(
    ApplicationDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    TimeProvider timeProvider,
    IWebHostEnvironment webHostEnvironment,
    ScreeningErrorCategoryAnalyticsCalculator errorCategoryAnalyticsCalculator,
    AppNotificationService appNotificationService,
    IAuditService auditService) : Controller
{
    private const int PageSize = 20;
    private const string MemoryAndBroaderThinkingSkillsCategoryName = "Memory and Broader Thinking Skills";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [HttpGet("")]
    public async Task<IActionResult> Index(int page = 1)
    {
        var userId = userManager.GetUserId(User);
        if (userId is null)
        {
            return Forbid();
        }

        var referrals = AssignedReferrals(userId).AsNoTracking();
        var totalCount = await referrals.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)PageSize));
        var pageNumber = Math.Clamp(page, 1, totalPages);
        var referralRows = await referrals
            .OrderByDescending(referral => referral.CreatedAtUtc)
            .ThenByDescending(referral => referral.Id)
            .Skip((pageNumber - 1) * PageSize)
            .Take(PageSize)
            .Select(referral => new
            {
                referral.Id,
                FirstName = referral.Learner.FirstName,
                LastName = referral.Learner.LastName,
                PreferredName = referral.Learner.PreferredName,
                referral.CreatedAtUtc,
                referral.Status
            })
            .ToListAsync();
        var rows = referralRows.Select(referral => new PsychologistReferralListItemViewModel(
            referral.Id,
            LearnerName(referral.FirstName, referral.LastName, referral.PreferredName),
            referral.CreatedAtUtc,
            referral.Status))
            .ToArray();

        return View(new PsychologistReferralsIndexViewModel
        {
            Referrals = rows,
            PageNumber = pageNumber,
            TotalPages = totalPages,
            TotalCount = totalCount
        });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var userId = userManager.GetUserId(User);
        if (userId is null)
        {
            return Forbid();
        }

        var model = await BuildDetailsViewModelAsync(id, userId);
        if (model is null)
        {
            return NotFound();
        }

        await auditService.RecordAsync(AuditActions.PsychologistReportAccessed, AuditEntityTypes.ScreeningReport,
            model.ScreeningReportId.ToString(), true, new { ReferralId = id, Access = "Details" });
        return View(model);
    }

    [HttpGet("{id:int}/data-passport")]
    public async Task<IActionResult> DataPassport(int id, CancellationToken cancellationToken)
    {
        var userId = userManager.GetUserId(User);
        if (userId is null)
        {
            return Forbid();
        }

        var referral = await AssignedReferrals(userId)
            .AsNoTracking()
            .Include(item => item.Learner)
            .Include(item => item.ScreeningReport)
                .ThenInclude(report => report.ScreeningAttempt)
                    .ThenInclude(attempt => attempt.TestAttempts)
                        .ThenInclude(testAttempt => testAttempt.ScreeningTest)
            .Include(item => item.ScreeningReport)
                .ThenInclude(report => report.ScreeningAttempt)
                    .ThenInclude(attempt => attempt.TestAttempts)
                        .ThenInclude(testAttempt => testAttempt.Responses)
                            .ThenInclude(response => response.ScreeningQuestion)
            .Include(item => item.ScreeningReport)
                .ThenInclude(report => report.ScreeningAttempt)
                    .ThenInclude(attempt => attempt.TestAttempts)
                        .ThenInclude(testAttempt => testAttempt.Responses)
                            .ThenInclude(response => response.ErrorCategories)
            .AsSplitQuery()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (referral is null)
        {
            return NotFound();
        }

        var attempt = referral.ScreeningReport.ScreeningAttempt;
        var previousScreeningData = await dbContext.ScreeningAttempts
            .AsNoTracking()
            .Where(previous => previous.UserId == referral.LearnerId &&
                               previous.Id != attempt.Id &&
                               previous.Status == ScreeningAttemptStatus.Completed &&
                               previous.CompletedAtUtc != null)
            .OrderByDescending(previous => previous.CompletedAtUtc)
            .Take(10)
            .Select(previous => new
            {
                ScreeningDateUtc = previous.CompletedAtUtc!.Value,
                OverallPercentage = previous.OverallScore,
                RiskTier = previous.Report != null ? previous.Report.RiskTier : previous.RiskTier,
                SkillAreasJson = previous.Report != null ? previous.Report.SkillAreasJson : null
            })
            .ToListAsync(cancellationToken);

        var categoryScores = ReadSkillAreas(referral.ScreeningReport.SkillAreasJson);
        var optionalCategory = categoryScores.SingleOrDefault(IsMemoryAndBroaderThinkingSkills);
        var generalCategoryScores = categoryScores
            .Where(category => !IsMemoryAndBroaderThinkingSkills(category))
            .ToArray();
        var comparableActivityTypes = optionalCategory?.ActivityMetrics
            .Select(metric => metric.ActivityType)
            .ToHashSet() ?? [];
        var previousOptionalScreenings = previousScreeningData
            .Select(previous =>
            {
                var previousCategory = ReadSkillAreas(previous.SkillAreasJson)
                    .SingleOrDefault(IsMemoryAndBroaderThinkingSkills);
                var comparableMetrics = previousCategory?.ActivityMetrics
                    .Where(metric => comparableActivityTypes.Contains(metric.ActivityType))
                    .ToArray() ?? [];
                return new PassportPreviousOptionalScreeningViewModel(previous.ScreeningDateUtc, comparableMetrics);
            })
            .Where(previous => previous.ActivityMetrics.Count > 0)
            .ToArray();
        var previousScreenings = previousScreeningData
            .Select(previous => new PassportPreviousScreeningViewModel(
                previous.ScreeningDateUtc,
                previous.OverallPercentage,
                previous.RiskTier))
            .ToArray();
        var responseCategories = BuildResponseCategories(attempt.TestAttempts.SelectMany(testAttempt => testAttempt.Responses));
        var recordings = new List<PassportRecordingViewModel>();
        var unavailableRecordingCount = 0;
        foreach (var testAttempt in attempt.TestAttempts)
        {
            foreach (var response in testAttempt.Responses)
            {
                if ((testAttempt.HasFlaggedSkillArea != true && response.ErrorCategories.Count == 0) ||
                    string.IsNullOrWhiteSpace(response.AudioRecordingPath))
                {
                    continue;
                }

                if (ScreeningRecordingSource.TryResolve(
                        response.AudioRecordingPath,
                        webHostEnvironment.ContentRootPath,
                        out _,
                        out var contentType))
                {
                    recordings.Add(new PassportRecordingViewModel(
                        response.Id,
                        DisplayName(response.ScreeningQuestion.QuestionType),
                        contentType,
                        ItemPrompt: response.ScreeningQuestion.Prompt,
                        ErrorCategories: response.ErrorCategories.Select(tag => tag.Category).OrderBy(category => category).ToArray(),
                        ProfessionalNote: response.ProfessionalAudioNote,
                        ProfessionalNoteUpdatedAtUtc: response.ProfessionalAudioNoteUpdatedAtUtc));
                }
                else
                {
                    unavailableRecordingCount++;
                }
            }
        }

        var privateRecordings = await dbContext.LearnerAudioRecordings
            .AsNoTracking()
            .Where(recording => recording.ScreeningTestAttemptId != null &&
                                recording.UserId == referral.LearnerId &&
                                recording.ScreeningTestAttempt!.ScreeningAttemptId == attempt.Id &&
                                recording.Status == LearnerAudioRecordingStatus.Available &&
                                recording.ContentType != null &&
                                recording.ScreeningQuestion!.ScreeningTestId == recording.ScreeningTestAttempt.ScreeningTestId &&
                                (recording.ScreeningTestAttempt.HasFlaggedSkillArea == true ||
                                 recording.ScreeningQuestion.Responses.Any(response =>
                                     response.ScreeningTestAttemptId == recording.ScreeningTestAttemptId &&
                                     response.ErrorCategories.Any())))
            .Select(recording => new
            {
                recording.Id,
                recording.ScreeningTestAttemptId,
                recording.ScreeningQuestionId,
                recording.ScreeningQuestion!.QuestionType,
                recording.ScreeningQuestion.Prompt,
                recording.ContentType,
                recording.DurationMilliseconds,
                recording.ProfessionalNote,
                recording.ProfessionalNoteUpdatedAtUtc
            })
            .ToListAsync(cancellationToken);
        recordings.AddRange(privateRecordings
            .Where(recording => !string.IsNullOrWhiteSpace(recording.ContentType))
            .Select(recording =>
            {
                var response = attempt.TestAttempts
                    .SelectMany(testAttempt => testAttempt.Responses)
                    .FirstOrDefault(item => item.ScreeningTestAttemptId == recording.ScreeningTestAttemptId &&
                                            item.ScreeningQuestionId == recording.ScreeningQuestionId);
                return new PassportRecordingViewModel(
                    0,
                    DisplayName(recording.QuestionType),
                    recording.ContentType!,
                    recording.Id,
                    recording.DurationMilliseconds,
                    recording.Prompt,
                    response?.ErrorCategories.Select(tag => tag.Category).OrderBy(category => category).ToArray() ?? [],
                    recording.ProfessionalNote ?? response?.ProfessionalAudioNote,
                    recording.ProfessionalNoteUpdatedAtUtc ?? response?.ProfessionalAudioNoteUpdatedAtUtc);
            }));

        await auditService.RecordAsync(AuditActions.PsychologistReportAccessed, AuditEntityTypes.ScreeningReport,
            referral.ScreeningReportId.ToString(), true, new { ReferralId = id, Access = "DataPassport" });

        return View(new PsychologistDataPassportViewModel
        {
            ReferralId = referral.Id,
            ScreeningAttemptId = attempt.Id,
            LearnerName = LearnerName(referral.Learner),
            LearnerEmail = referral.Learner.Email ?? string.Empty,
            DateOfBirth = referral.Learner.DateOfBirth,
            ScreeningStartedAtUtc = attempt.StartedAtUtc,
            ScreeningCompletedAtUtc = attempt.CompletedAtUtc!.Value,
            ReferralCreatedAtUtc = referral.CreatedAtUtc,
            RiskTier = referral.ScreeningReport.RiskTier,
            ScreeningSummary = referral.ScreeningReport.Summary,
            ProfessionalNotes = referral.Notes,
            CategoryScores = generalCategoryScores,
            MemoryAndBroaderThinkingSkills = optionalCategory,
            FlaggedSkillAreas = generalCategoryScores.Where(area => area.IsFlaggedForSupport).ToArray(),
            TestTimings = attempt.TestAttempts
                .OrderBy(testAttempt => testAttempt.StartedAtUtc)
                .Select(testAttempt => new PassportTestTimingViewModel(
                    testAttempt.ScreeningTest.Name,
                    testAttempt.DurationMilliseconds,
                    testAttempt.StartedAtUtc,
                    testAttempt.CompletedAtUtc))
                .ToArray(),
            ResponseCategories = responseCategories,
            AvailableErrorCategories = Enum.GetValues<ScreeningErrorCategory>(),
            ResponsesForTagging = attempt.TestAttempts
                .SelectMany(testAttempt => testAttempt.Responses)
                .OrderBy(response => response.CreatedAtUtc)
                .Select(response => new PassportResponseTaggingViewModel(
                    response.Id,
                    DisplayName(response.ScreeningQuestion.QuestionType),
                    response.ScreeningQuestion.Prompt,
                    response.SelectedAnswer ?? response.TextAnswer,
                    response.ErrorCategories.Select(tag => tag.Category).OrderBy(category => category).ToArray()))
                .ToArray(),
            Recordings = recordings,
            UnavailableRecordingCount = unavailableRecordingCount,
            PreviousScreenings = previousScreenings,
            PreviousMemoryAndBroaderThinkingSkills = previousOptionalScreenings
        });
    }

    [HttpPost("{id:int}/recordings/{recordingId:int}/note")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveRecordingNote(
        int id,
        int recordingId,
        UpdateAudioSnippetNoteViewModel model,
        CancellationToken cancellationToken)
    {
        var userId = userManager.GetUserId(User);
        if (userId is null)
        {
            return Forbid();
        }

        if (!ModelState.IsValid)
        {
            return BadRequest("A snippet note must be 500 characters or fewer.");
        }

        var recording = await dbContext.LearnerAudioRecordings
            .Where(item => item.Id == recordingId &&
                           item.Status == LearnerAudioRecordingStatus.Available &&
                           item.UserId != null &&
                           item.ScreeningTestAttemptId != null &&
                           (item.ScreeningTestAttempt!.HasFlaggedSkillArea == true ||
                            item.ScreeningQuestion!.Responses.Any(response =>
                                response.ScreeningTestAttemptId == item.ScreeningTestAttemptId &&
                                response.ErrorCategories.Any())) &&
                           item.ScreeningQuestion!.ScreeningTestId == item.ScreeningTestAttempt.ScreeningTestId &&
                           AssignedReferrals(userId).Any(referral =>
                               referral.Id == id &&
                               referral.LearnerId == item.UserId &&
                               referral.ScreeningReport.ScreeningAttemptId == item.ScreeningTestAttempt.ScreeningAttemptId))
            .SingleOrDefaultAsync(cancellationToken);

        if (recording is null)
        {
            return NotFound();
        }

        recording.ProfessionalNote = string.IsNullOrWhiteSpace(model.ProfessionalNote)
            ? null
            : model.ProfessionalNote.Trim();
        recording.ProfessionalNoteUpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync(AuditActions.ProfessionalNoteChanged, AuditEntityTypes.ProfessionalNote,
            recording.Id.ToString(), true, new { RecordingId = recording.Id, NotePresent = recording.ProfessionalNote is not null });

        TempData["StatusMessage"] = "The audio snippet note was saved.";
        return RedirectToAction(nameof(DataPassport), new { id });
    }

    [HttpPost("{id:int}/recordings/responses/{responseId:int}/note")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveResponseRecordingNote(
        int id,
        int responseId,
        UpdateAudioSnippetNoteViewModel model,
        CancellationToken cancellationToken)
    {
        var userId = userManager.GetUserId(User);
        if (userId is null)
        {
            return Forbid();
        }

        if (!ModelState.IsValid)
        {
            return BadRequest("A snippet note must be 500 characters or fewer.");
        }

        var response = await dbContext.ScreeningResponses
            .Where(item => item.Id == responseId &&
                           !string.IsNullOrWhiteSpace(item.AudioRecordingPath) &&
                           (item.ScreeningTestAttempt.HasFlaggedSkillArea == true || item.ErrorCategories.Any()) &&
                           AssignedReferrals(userId).Any(referral =>
                               referral.Id == id &&
                               referral.LearnerId == item.ScreeningTestAttempt.ScreeningAttempt.UserId &&
                               referral.ScreeningReport.ScreeningAttemptId == item.ScreeningTestAttempt.ScreeningAttemptId))
            .SingleOrDefaultAsync(cancellationToken);

        if (response is null)
        {
            return NotFound();
        }

        response.ProfessionalAudioNote = string.IsNullOrWhiteSpace(model.ProfessionalNote)
            ? null
            : model.ProfessionalNote.Trim();
        response.ProfessionalAudioNoteUpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync(AuditActions.ProfessionalNoteChanged, AuditEntityTypes.ProfessionalNote,
            response.Id.ToString(), true, new { ResponseId = response.Id, NotePresent = response.ProfessionalAudioNote is not null });

        TempData["StatusMessage"] = "The audio snippet note was saved.";
        return RedirectToAction(nameof(DataPassport), new { id });
    }

    [HttpPost("{id:int}/responses/{responseId:int}/error-categories")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveErrorCategories(
        int id,
        int responseId,
        UpdateResponseErrorCategoriesViewModel model,
        CancellationToken cancellationToken)
    {
        var userId = userManager.GetUserId(User);
        if (userId is null)
        {
            return Forbid();
        }

        var response = await AssignedReferrals(userId)
            .Where(referral => referral.Id == id)
            .SelectMany(referral => referral.ScreeningReport.ScreeningAttempt.TestAttempts)
            .SelectMany(testAttempt => testAttempt.Responses)
            .Include(item => item.ErrorCategories)
            .SingleOrDefaultAsync(item => item.Id == responseId, cancellationToken);

        if (response is null)
        {
            return NotFound();
        }

        var selectedCategories = model.ErrorCategories?.Distinct().ToArray() ?? [];
        if (!ModelState.IsValid || selectedCategories.Length > Enum.GetValues<ScreeningErrorCategory>().Length ||
            selectedCategories.Any(category => !Enum.IsDefined(category)))
        {
            return BadRequest("Choose valid screening error categories.");
        }

        response.ErrorCategories.Clear();
        foreach (var category in selectedCategories)
        {
            response.ErrorCategories.Add(new ScreeningResponseErrorCategory
            {
                ScreeningResponseId = response.Id,
                Category = category
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        TempData["StatusMessage"] = "Response error categories were saved.";
        return RedirectToAction(nameof(DataPassport), new { id });
    }

    [HttpGet("error-analytics")]
    public async Task<IActionResult> ErrorAnalytics(CancellationToken cancellationToken)
    {
        var userId = userManager.GetUserId(User);
        if (userId is null)
        {
            return Forbid();
        }

        var taggedCategories = await AssignedReferrals(userId)
            .AsNoTracking()
            .SelectMany(referral => referral.ScreeningReport.ScreeningAttempt.TestAttempts)
            .SelectMany(testAttempt => testAttempt.Responses)
            .SelectMany(response => response.ErrorCategories)
            .ToListAsync(cancellationToken);
        var categories = errorCategoryAnalyticsCalculator.Calculate(taggedCategories);

        return View(new PsychologistErrorAnalyticsViewModel
        {
            ClassifiedResponseCount = categories.FirstOrDefault()?.ClassifiedResponseCount ?? 0,
            Categories = categories
        });
    }

    [HttpGet("{id:int}/recordings/{responseId:int}")]
    public async Task<IActionResult> Recording(int id, int responseId, CancellationToken cancellationToken)
    {
        var userId = userManager.GetUserId(User);
        if (userId is null)
        {
            return Forbid();
        }

        var storedPath = await AssignedReferrals(userId)
            .Where(referral => referral.Id == id)
            .SelectMany(referral => referral.ScreeningReport.ScreeningAttempt.TestAttempts)
            .SelectMany(testAttempt => testAttempt.Responses)
            .Where(response => response.Id == responseId)
            .Select(response => response.AudioRecordingPath)
            .SingleOrDefaultAsync(cancellationToken);

        if (!ScreeningRecordingSource.TryResolve(
                storedPath,
                webHostEnvironment.ContentRootPath,
                out var filePath,
                out var contentType))
        {
            return NotFound("The recording is unavailable.");
        }

        try
        {
            var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
            await auditService.RecordAsync(AuditActions.RecordingAccessed, AuditEntityTypes.Recording, responseId.ToString(), true,
                new { ReferralId = id, Source = "ScreeningResponse" }, cancellationToken);
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            Response.Headers["Cache-Control"] = "private, no-store";
            return File(stream, contentType, enableRangeProcessing: true);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException or UnauthorizedAccessException or IOException)
        {
            return NotFound("The recording is unavailable.");
        }
    }

    [HttpPost("{id:int}/review")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Review(
        int id,
        PsychologistReferralUpdateViewModel model,
        CancellationToken cancellationToken)
    {
        var userId = userManager.GetUserId(User);
        if (userId is null)
        {
            return Forbid();
        }

        var referral = await AssignedReferrals(userId)
            .Include(item => item.ScreeningReport)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (referral is null)
        {
            return NotFound();
        }

        if (!ModelState.IsValid || !IsAllowedTransition(referral.Status, model.Status))
        {
            if (ModelState.IsValid)
            {
                ModelState.AddModelError(nameof(model.Status), "That status change is not available from the current stage.");
            }

            var viewModel = await BuildDetailsViewModelAsync(id, userId, cancellationToken);
            return viewModel is null ? NotFound() : View("Details", viewModel);
        }

        var followUpRequested = referral.Status != ReferralStatus.FollowUpRequested &&
                                model.Status == ReferralStatus.FollowUpRequested;
        var previousNotes = referral.Notes;
        referral.Status = model.Status;
        referral.Notes = string.IsNullOrWhiteSpace(model.Notes) ? null : model.Notes.Trim();
        var notesChanged = !string.Equals(previousNotes, referral.Notes, StringComparison.Ordinal);
        if (referral.ReviewedAtUtc is null && model.Status is ReferralStatus.UnderReview or ReferralStatus.FollowUpRequested or ReferralStatus.Completed)
        {
            referral.ReviewedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        }

        if (followUpRequested)
        {
            appNotificationService.Add(
                referral.LearnerId,
                AppNotificationType.ProfessionalFollowUpRequested,
                "Professional follow-up requested",
                "Your assigned Psychologist requested a follow-up discussion. Review your screening summary and contact a trusted support person if you need help planning next steps.",
                $"/screening/results/{referral.ScreeningReport.ScreeningAttemptId}");
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        if (notesChanged)
        {
            await auditService.RecordAsync(AuditActions.ProfessionalNoteChanged, AuditEntityTypes.ProfessionalNote,
                referral.Id.ToString(), true, new { ReferralId = referral.Id, NotesPresent = referral.Notes is not null }, cancellationToken);
        }
        TempData["StatusMessage"] = "The referral review was saved.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private IQueryable<Referral> AssignedReferrals(string psychologistId) => dbContext.Referrals
        .Where(referral => referral.AssignedPsychologistId == psychologistId &&
                           referral.LearnerId == referral.ScreeningReport.UserId &&
                           referral.ScreeningReport.RiskTier == nameof(ScreeningSupportTier.Red) &&
                           referral.ScreeningReport.IsReferralRecommended &&
                           referral.ScreeningReport.ScreeningAttempt.Status == ScreeningAttemptStatus.Completed &&
                           referral.ScreeningReport.ScreeningAttempt.CompletedAtUtc != null);

    private async Task<PsychologistReferralDetailsViewModel?> BuildDetailsViewModelAsync(
        int id,
        string psychologistId,
        CancellationToken cancellationToken = default)
    {
        var referral = await AssignedReferrals(psychologistId)
            .AsNoTracking()
            .Include(item => item.Learner)
            .Include(item => item.ScreeningReport)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (referral is null)
        {
            return null;
        }

        return new PsychologistReferralDetailsViewModel
        {
            Id = referral.Id,
            ScreeningReportId = referral.ScreeningReportId,
            LearnerName = LearnerName(referral.Learner),
            LearnerEmail = referral.Learner.Email ?? string.Empty,
            CreatedAtUtc = referral.CreatedAtUtc,
            ReviewedAtUtc = referral.ReviewedAtUtc,
            Status = referral.Status,
            Notes = referral.Notes,
            RiskTier = referral.ScreeningReport.RiskTier,
            ReportSummary = referral.ScreeningReport.Summary,
            SkillAreas = ReadSkillAreas(referral.ScreeningReport.SkillAreasJson),
            AllowedStatuses = AllowedNextStatuses(referral.Status)
        };
    }

    private static bool IsAllowedTransition(ReferralStatus current, ReferralStatus next) =>
        current switch
        {
            ReferralStatus.Assigned => next == ReferralStatus.UnderReview,
            ReferralStatus.UnderReview => next is ReferralStatus.UnderReview or ReferralStatus.FollowUpRequested or ReferralStatus.Completed or ReferralStatus.Closed,
            ReferralStatus.FollowUpRequested => next is ReferralStatus.UnderReview or ReferralStatus.FollowUpRequested or ReferralStatus.Completed or ReferralStatus.Closed,
            _ => false
        };

    private static IReadOnlyList<ReferralStatus> AllowedNextStatuses(ReferralStatus current) =>
        Enum.GetValues<ReferralStatus>().Where(status => IsAllowedTransition(current, status)).ToArray();

    private static IReadOnlyList<ScreeningReportSkillAreaSnapshot> ReadSkillAreas(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<ScreeningReportSkillAreaSnapshot>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static bool IsMemoryAndBroaderThinkingSkills(ScreeningReportSkillAreaSnapshot category) =>
        string.Equals(category.Name, MemoryAndBroaderThinkingSkillsCategoryName, StringComparison.Ordinal);

    private static IReadOnlyList<PassportResponseCategoryViewModel> BuildResponseCategories(
        IEnumerable<ScreeningResponse> responses) => responses
        .GroupBy(response => response.ScreeningQuestion.QuestionType)
        .OrderBy(group => group.Key)
        .Select(group =>
        {
            var groupResponses = group.ToArray();
            var errorDetails = groupResponses
                .Select(response => ReadAdditionalErrorCount(group.Key, response.ResponseDetailsJson))
                .Where(count => count.HasValue)
                .Select(count => count!.Value)
                .ToArray();
            var additionalErrorLabel = group.Key switch
            {
                ScreeningQuestionType.TimedNaming => "Self-reported naming errors",
                ScreeningQuestionType.ReadingFluency => "Self-reported reading corrections",
                _ => null
            };
            var responseTimes = groupResponses
                .Where(response => response.ResponseTimeMilliseconds.HasValue)
                .Select(response => (double)response.ResponseTimeMilliseconds!.Value)
                .ToArray();

            return new PassportResponseCategoryViewModel(
                DisplayName(group.Key),
                groupResponses.Length,
                groupResponses.Count(response => response.IsCorrect == true),
                groupResponses.Count(response => response.IsCorrect == false),
                groupResponses.Count(response => response.IsCorrect is null),
                responseTimes.Length == 0 ? null : responseTimes.Average(),
                additionalErrorLabel,
                errorDetails.Length == 0 ? null : errorDetails.Sum());
        })
        .ToArray();

    private static int? ReadAdditionalErrorCount(ScreeningQuestionType questionType, string? detailsJson)
    {
        if (string.IsNullOrWhiteSpace(detailsJson))
        {
            return null;
        }

        var propertyName = questionType switch
        {
            ScreeningQuestionType.TimedNaming => "SelfReportedErrorIndexes",
            ScreeningQuestionType.ReadingFluency => "SelfReportedCorrectionCount",
            _ => null
        };
        if (propertyName is null)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(detailsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !TryGetPropertyIgnoreCase(document.RootElement, propertyName, out var property))
            {
                return null;
            }

            if (questionType == ScreeningQuestionType.TimedNaming && property.ValueKind == JsonValueKind.Array)
            {
                return property.GetArrayLength();
            }

            return property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var count)
                ? Math.Max(0, count)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string DisplayName(ScreeningQuestionType questionType)
    {
        var member = typeof(ScreeningQuestionType).GetMember(questionType.ToString()).FirstOrDefault();
        return member?.GetCustomAttribute<DisplayAttribute>()?.GetName() ?? questionType.ToString();
    }

    private static string LearnerName(ApplicationUser user) =>
        LearnerName(user.FirstName, user.LastName, user.PreferredName);

    private static string LearnerName(string firstName, string lastName, string? preferredName) =>
        string.IsNullOrWhiteSpace(preferredName) ? $"{firstName} {lastName}".Trim() : preferredName;
}
