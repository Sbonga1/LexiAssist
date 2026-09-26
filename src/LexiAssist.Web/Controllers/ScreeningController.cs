using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Identity;
using LexiAssist.Web.Models.Reports;
using LexiAssist.Web.Models.Screening;
using LexiAssist.Web.Models.Notifications;
using LexiAssist.Web.Services;
using LexiAssist.Web.Services.Interfaces;
using LexiAssist.Web.ViewModels.Screening;
using LexiAssist.Web.ViewModels.Recordings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LexiAssist.Web.Controllers;

[Authorize(Roles = ApplicationRoles.Learner)]
[Route("screening")]
public sealed class ScreeningController(
    ApplicationDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    IDataProtectionProvider dataProtectionProvider,
    IWebHostEnvironment webHostEnvironment,
    IScreeningScoringService scoringService,
    IScreeningRiskClassificationService riskClassificationService,
    IScreeningReportGenerationService reportGenerationService,
    AppNotificationService appNotificationService,
    IOptions<LearnerAudioRecordingOptions> audioRecordingOptions,
    IOptions<ScreeningScoringOptions> screeningScoringOptions) : Controller
{
    private const string MemoryAndBroaderThinkingSkillsCategoryName = "Memory and Broader Thinking Skills";
    private static readonly JsonSerializerOptions MetadataJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDataProtector _questionTokenProtector =
        dataProtectionProvider.CreateProtector("LexiAssist.Screening.QuestionRunner.v1");
    private readonly IDataProtector _timedNamingTokenProtector =
        dataProtectionProvider.CreateProtector("LexiAssist.Screening.TimedNaming.v1");
    private readonly IDataProtector _readingFluencyTokenProtector =
        dataProtectionProvider.CreateProtector("LexiAssist.Screening.ReadingFluency.v1");
    private readonly ScreeningScoringOptions _screeningScoringOptions = screeningScoringOptions.Value;

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var userId = userManager.GetUserId(User);
        if (userId is null)
        {
            return Unauthorized();
        }

        var incompleteAttempt = await FindIncompleteAttemptAsync(userId, tracked: false);
        var categories = await GetActiveCategoriesAsync();

        return View(new ScreeningLandingViewModel(
            incompleteAttempt is not null,
            incompleteAttempt?.StartedAtUtc,
            await GetSupportedActiveQuestionQuery().AnyAsync(),
            categories));
    }

    [HttpGet("start")]
    public async Task<IActionResult> Start()
    {
        var userId = userManager.GetUserId(User);
        if (userId is null)
        {
            return Unauthorized();
        }

        if (await FindIncompleteAttemptAsync(userId, tracked: false) is not null)
        {
            return RedirectToAction(nameof(Resume));
        }

        var categories = await GetActiveCategoriesAsync();
        var canStart = categories.Count > 0 && await GetSupportedActiveQuestionQuery().AnyAsync();
        return View(new ScreeningStartViewModel(canStart, categories));
    }

    [HttpPost("start")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(CancellationToken cancellationToken)
    {
        var userId = userManager.GetUserId(User);
        if (userId is null)
        {
            return Unauthorized();
        }

        if (await FindIncompleteAttemptAsync(userId, tracked: false) is not null)
        {
            return RedirectToAction(nameof(Resume));
        }

        var firstQuestion = await GetSupportedActiveQuestionQuery().FirstOrDefaultAsync(cancellationToken);
        if (firstQuestion is null)
        {
            TempData["ErrorMessage"] = "Screening cannot start yet because there are no active questions available.";
            return RedirectToAction(nameof(Index));
        }

        var attempt = new ScreeningAttempt
        {
            UserId = userId,
            StartedAtUtc = DateTime.UtcNow,
            Status = ScreeningAttemptStatus.InProgress,
            CurrentTestId = firstQuestion.ScreeningTestId
        };

        dbContext.ScreeningAttempts.Add(attempt);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(attempt).State = EntityState.Detached;
            if (await FindIncompleteAttemptAsync(userId, tracked: false) is not null)
            {
                return RedirectToAction(nameof(Resume));
            }

            throw;
        }

        TempData["StatusMessage"] = "Your screening has been started and saved.";
        return RedirectToAction(nameof(Resume));
    }

    [HttpGet("resume")]
    public async Task<IActionResult> Resume(CancellationToken cancellationToken)
    {
        var userId = userManager.GetUserId(User);
        if (userId is null)
        {
            return Unauthorized();
        }

        var attempt = await FindIncompleteAttemptAsync(userId, tracked: true);
        if (attempt is null)
        {
            return RedirectToAction(nameof(Index));
        }

        var question = await GetNextQuestionAsync(attempt.Id, cancellationToken);
        if (question is null)
        {
            var totalQuestionCount = await GetSupportedQuestionQueryForAttempt(attempt.Id).CountAsync(cancellationToken);
            var responseCount = await dbContext.ScreeningResponses
                .CountAsync(response => response.ScreeningTestAttempt.ScreeningAttemptId == attempt.Id, cancellationToken);

            if (totalQuestionCount == 0 && responseCount == 0)
            {
                return View(new ScreeningRunnerViewModel
                {
                    AttemptStartedAtUtc = attempt.StartedAtUtc,
                    IsUnavailable = true
                });
            }

            var completedAtUtc = DateTime.UtcNow;
            attempt.Status = ScreeningAttemptStatus.Completed;
            attempt.CompletedAtUtc = completedAtUtc;
            attempt.CurrentTestId = null;

            var testAttempts = await dbContext.ScreeningTestAttempts
                .Where(testAttempt => testAttempt.ScreeningAttemptId == attempt.Id && testAttempt.CompletedAtUtc == null)
                .ToListAsync(cancellationToken);
            foreach (var currentTestAttempt in testAttempts)
            {
                currentTestAttempt.CompletedAtUtc = completedAtUtc;
            }

            var startedTestIds = await dbContext.ScreeningTestAttempts
                .Where(testAttempt => testAttempt.ScreeningAttemptId == attempt.Id)
                .Select(testAttempt => testAttempt.ScreeningTestId)
                .ToArrayAsync(cancellationToken);
            var startedTestIdSet = startedTestIds.ToHashSet();
            var activeTests = await dbContext.ScreeningTests
                .AsNoTracking()
                .Where(test => test.IsActive || startedTestIds.Contains(test.Id))
                .Include(test => test.Questions)
                .OrderBy(test => test.SortOrder)
                .ThenBy(test => test.Id)
                .ToListAsync(cancellationToken);
            var screeningResponses = await dbContext.ScreeningResponses
                .AsNoTracking()
                .Include(response => response.ScreeningQuestion)
                .Where(response => response.ScreeningTestAttempt.ScreeningAttemptId == attempt.Id)
                .ToListAsync(cancellationToken);
            var testResults = activeTests.Select(test => scoringService.ScoreTest(
                    test,
                    screeningResponses.Where(response => response.ScreeningQuestion.ScreeningTestId == test.Id),
                    includeInactive: startedTestIdSet.Contains(test.Id)))
                .ToArray();
            var overallResult = scoringService.ScoreOverall(testResults);
            attempt.OverallScore = overallResult.Percentage is { } overallPercentage
                ? (double)overallPercentage
                : null;

            var testAttemptsByTestId = await dbContext.ScreeningTestAttempts
                .Where(testAttempt => testAttempt.ScreeningAttemptId == attempt.Id)
                .ToDictionaryAsync(testAttempt => testAttempt.ScreeningTestId, cancellationToken);
            foreach (var testResult in testResults)
            {
                if (!testAttemptsByTestId.TryGetValue(testResult.ScreeningTestId, out var savedTestAttempt))
                {
                    continue;
                }

                savedTestAttempt.RawScore = testResult.Percentage is null ? null : (double)testResult.RawScore;
                savedTestAttempt.PercentageScore = testResult.Percentage is { } testPercentage
                    ? (double)testPercentage
                    : null;
                savedTestAttempt.HasFlaggedSkillArea = testResult.Percentage is null
                    ? null
                    : testResult.FlaggedSkillAreas.Count > 0;
            }

            var classification = riskClassificationService.Classify(overallResult);
            if (classification is not null)
            {
                attempt.RiskTier = classification.Tier.ToString();
                attempt.RiskCalculationVersion = classification.CalculationVersion;
                attempt.RiskCalculatedAtUtc = classification.CalculatedAtUtc;
            }

            var report = await dbContext.ScreeningReports
                .Include(screeningReport => screeningReport.Referral)
                .SingleOrDefaultAsync(screeningReport => screeningReport.ScreeningAttemptId == attempt.Id, cancellationToken);
            if (report is null)
            {
                report = reportGenerationService.Generate(
                    userId,
                    attempt.Id,
                    overallResult,
                    classification);
                dbContext.ScreeningReports.Add(report);
            }

            appNotificationService.Add(
                userId,
                AppNotificationType.ScreeningCompleted,
                "Screening completed",
                "Your educational screening is complete. You can review the saved summary and suggested next steps.",
                $"/screening/results/{attempt.Id}");

            if (report.Referral is not null)
            {
                appNotificationService.Add(
                    userId,
                    AppNotificationType.ReferralCreated,
                    "Professional follow-up referral created",
                    "A referral was created for professional review based on the screening/support workflow. This is not a diagnosis.",
                    $"/screening/results/{attempt.Id}");

                var administrators = await userManager.GetUsersInRoleAsync(ApplicationRoles.Administrator);
                foreach (var administrator in administrators.Where(account => account.IsActive))
                {
                    appNotificationService.Add(
                        administrator.Id,
                        AppNotificationType.ReferralCreated,
                        "Referral awaiting assignment",
                        "A completed screening referral is waiting for assignment to a Psychologist.",
                        "/admin/referrals");
                }
            }

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                var completedWithReport = await dbContext.ScreeningReports
                    .AsNoTracking()
                    .AnyAsync(report =>
                        report.ScreeningAttemptId == attempt.Id &&
                        report.ScreeningAttempt.Status == ScreeningAttemptStatus.Completed,
                        cancellationToken);
                if (!completedWithReport)
                {
                    throw;
                }
            }

            return RedirectToAction(nameof(Results), new { attemptId = attempt.Id });
        }

        var testAttempt = await GetOrCreateTestAttemptAsync(attempt, question.ScreeningTestId, cancellationToken);
        attempt.CurrentTestId = question.ScreeningTestId;
        await dbContext.SaveChangesAsync(cancellationToken);

        var (answeredCount, totalCount) = await GetProgressAsync(attempt.Id, cancellationToken);
        var questionToken = ProtectQuestionToken(new QuestionTokenPayload(
            userId,
            attempt.Id,
            testAttempt.Id,
            question.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));

        return View(BuildQuestionViewModel(
            attempt,
            testAttempt.Id,
            question,
            questionToken,
            answeredCount,
            totalCount));
    }

    [HttpGet("results/{attemptId:int}")]
    public async Task<IActionResult> Results(int attemptId, CancellationToken cancellationToken)
    {
        var userId = userManager.GetUserId(User);
        if (userId is null)
        {
            return Unauthorized();
        }

        var attempt = await dbContext.ScreeningAttempts
            .AsNoTracking()
            .Include(screeningAttempt => screeningAttempt.Report)
            .Include(screeningAttempt => screeningAttempt.TestAttempts)
            .ThenInclude(testAttempt => testAttempt.ScreeningTest)
            .SingleOrDefaultAsync(screeningAttempt =>
                screeningAttempt.Id == attemptId &&
                screeningAttempt.UserId == userId &&
                screeningAttempt.Status == ScreeningAttemptStatus.Completed,
                cancellationToken);

        if (attempt is null)
        {
            return NotFound();
        }

        var report = attempt.Report;
        var skillAreas = report is null
            ? GetRelativeStrengths(attempt.TestAttempts
                .OrderBy(testAttempt => testAttempt.ScreeningTest.SortOrder)
                .ThenBy(testAttempt => testAttempt.ScreeningTestId)
                .Select(BuildSkillAreaResult)
                .ToArray())
            : DeserializeSkillAreas(report.SkillAreasJson)
                .Select(MapSkillAreaSnapshot)
                .ToArray();
        var optionalSkillArea = skillAreas.SingleOrDefault(IsMemoryAndBroaderThinkingSkills);
        var generalSkillAreas = skillAreas
            .Where(skillArea => !IsMemoryAndBroaderThinkingSkills(skillArea))
            .ToArray();
        var tier = GetStoredSupportTier(report?.RiskTier ?? attempt.RiskTier);
        var tierCopy = report is null ? GetSupportTierCopy(tier) : default;

        return View(new ScreeningResultViewModel(
            report?.GeneratedAtUtc ?? attempt.CompletedAtUtc ?? attempt.StartedAtUtc,
            tier,
            tier is null ? "Support tier not available" : ScreeningRiskClassificationResult.GetTierDescription(tier.Value),
            report?.Summary ?? tierCopy.Explanation,
            report?.RecommendedNextAction ?? tierCopy.RecommendedNextAction,
            report?.ScoringVersion,
            report?.IsReferralRecommended ?? tier == ScreeningSupportTier.Red,
            generalSkillAreas,
            generalSkillAreas.Where(skillArea => skillArea.IsStrength).ToArray(),
            generalSkillAreas.Where(skillArea => skillArea.IsFlaggedForSupport).ToArray())
        {
            MemoryAndBroaderThinkingSkills = optionalSkillArea
        });
    }

    [HttpPost("respond")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Respond(
        ScreeningAnswerInputViewModel model,
        CancellationToken cancellationToken)
    {
        var userId = userManager.GetUserId(User);
        if (userId is null)
        {
            return Unauthorized();
        }

        var attempt = await FindIncompleteAttemptAsync(userId, tracked: true);
        if (attempt is null)
        {
            return RedirectToAction(nameof(Index));
        }

        var question = await GetNextQuestionAsync(attempt.Id, cancellationToken);
        if (question is null || string.IsNullOrWhiteSpace(model.QuestionToken))
        {
            return RedirectToAction(nameof(Resume));
        }

        if (!TryUnprotectQuestionToken(model.QuestionToken, out var tokenPayload) || tokenPayload is null)
        {
            return RedirectToAction(nameof(Resume));
        }

        var testAttempt = await dbContext.ScreeningTestAttempts
            .SingleOrDefaultAsync(testAttempt =>
                testAttempt.ScreeningAttemptId == attempt.Id &&
                testAttempt.ScreeningTestId == question.ScreeningTestId,
                cancellationToken);

        if (tokenPayload is null ||
            tokenPayload.UserId != userId ||
            tokenPayload.AttemptId != attempt.Id ||
            testAttempt is null ||
            tokenPayload.TestAttemptId != testAttempt.Id ||
            tokenPayload.QuestionId != question.Id)
        {
            return RedirectToAction(nameof(Resume));
        }

        if (question.QuestionType is ScreeningQuestionType.TimedNaming or ScreeningQuestionType.ReadingFluency)
        {
            return RedirectToAction(nameof(Resume));
        }

        var createdAtUtc = DateTime.UtcNow;
        var presentedAtUtc = DateTimeOffset.FromUnixTimeMilliseconds(tokenPayload.PresentedAtUnixMilliseconds).UtcDateTime;
        var responseMilliseconds = Math.Max(0L, (long)(createdAtUtc - presentedAtUtc).TotalMilliseconds);
        var timedMatchingTimeLimitSeconds = question.QuestionType == ScreeningQuestionType.TimedMatching
            ? GetTimeLimitSeconds(question.MetadataJson)
            : null;
        var timedOut = question.QuestionType == ScreeningQuestionType.TimedMatching &&
                       timedMatchingTimeLimitSeconds is { } configuredTimeLimit &&
                       responseMilliseconds >= configuredTimeLimit * 1000L;
        var isProcessingSpeedActivity = question.QuestionType == ScreeningQuestionType.TimedMatching &&
            IsProcessingSpeedQuestion(question.MetadataJson);

        ValidateAnswer(model, question, timedOut);
        if (!ModelState.IsValid)
        {
            var (answeredCount, totalCount) = await GetProgressAsync(attempt.Id, cancellationToken);
            return View("Resume", BuildQuestionViewModel(
                attempt,
                testAttempt.Id,
                question,
                model.QuestionToken,
                answeredCount,
                totalCount));
        }

        var timedMatchingAnswers = isProcessingSpeedActivity
            ? ParseTimedMatchingAnswers(model.TimedMatchingAnswersJson)
            : Array.Empty<string>();
        var timedMatchingScore = isProcessingSpeedActivity
            ? ScoreTimedMatchingResponse(question.MetadataJson, timedMatchingAnswers)
            : null;
        var answerKeyScore = question.QuestionType is ScreeningQuestionType.WorkingMemorySequence or ScreeningQuestionType.PatternReasoning
            ? ScoreAnswerKeyResponse(question, question.QuestionType == ScreeningQuestionType.WorkingMemorySequence
                ? model.TextAnswer!
                : model.SelectedAnswer!)
            : null;

        var response = new ScreeningResponse
        {
            ScreeningTestAttemptId = testAttempt.Id,
            ScreeningQuestionId = question.Id,
            TextAnswer = question.QuestionType is ScreeningQuestionType.TextResponse or
                ScreeningQuestionType.WordReading or
                ScreeningQuestionType.NonWordReading or
                ScreeningQuestionType.Spelling or
                ScreeningQuestionType.ReadingPassage or
                ScreeningQuestionType.ListeningResponse or
                ScreeningQuestionType.WorkingMemorySequence
                ? model.TextAnswer!.Trim()
                : null,
            SelectedAnswer = question.QuestionType is ScreeningQuestionType.MultipleChoice or
                ScreeningQuestionType.PhonologicalChoice or
                ScreeningQuestionType.TimedMatching or
                ScreeningQuestionType.PatternReasoning
                ? question.QuestionType == ScreeningQuestionType.TimedMatching && isProcessingSpeedActivity
                    ? null
                    : timedOut ? null : model.SelectedAnswer!.Trim()
                : null,
            IsCorrect = timedMatchingScore is { } matchingScore
                ? matchingScore.CorrectCount == matchingScore.ItemCount && matchingScore.AttemptedCount == matchingScore.ItemCount
                : answerKeyScore?.IsCorrect,
            Score = timedMatchingScore?.Accuracy ?? answerKeyScore?.Score,
            ResponseTimeMilliseconds = responseMilliseconds,
            ResponseDetailsJson = question.QuestionType == ScreeningQuestionType.TimedMatching
                ? JsonSerializer.Serialize(new TimedMatchingResponseDetails(
                    timedMatchingTimeLimitSeconds ?? 0,
                    timedOut,
                    timedMatchingScore?.AttemptedCount,
                    timedMatchingScore?.CorrectCount,
                    timedMatchingScore?.IncorrectCount,
                    timedMatchingScore?.ItemCount), MetadataJsonOptions)
                : null,
            CreatedAtUtc = createdAtUtc
        };

        dbContext.ScreeningResponses.Add(response);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(response).State = EntityState.Detached;
            var alreadyAnswered = await dbContext.ScreeningResponses.AnyAsync(existing =>
                existing.ScreeningTestAttemptId == testAttempt.Id && existing.ScreeningQuestionId == question.Id,
                cancellationToken);
            if (!alreadyAnswered)
            {
                throw;
            }
        }

        return RedirectToAction(nameof(Resume));
    }

    [HttpPost("timed-naming/start")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartTimedNaming(
        TimedNamingStartInputViewModel model,
        CancellationToken cancellationToken)
    {
        var userId = userManager.GetUserId(User);
        if (userId is null)
        {
            return Unauthorized();
        }

        if (!ModelState.IsValid || !TryUnprotectQuestionToken(model.QuestionToken, out var questionToken) || questionToken is null)
        {
            return BadRequest();
        }

        var attempt = await FindIncompleteAttemptAsync(userId, tracked: true);
        if (attempt is null || questionToken.UserId != userId || questionToken.AttemptId != attempt.Id)
        {
            return Conflict(new { message = "This activity is no longer available." });
        }

        var question = await GetNextQuestionAsync(attempt.Id, cancellationToken);
        if (question is null || question.QuestionType != ScreeningQuestionType.TimedNaming || question.Id != questionToken.QuestionId)
        {
            return Conflict(new { message = "This activity is no longer available." });
        }

        var testAttempt = await dbContext.ScreeningTestAttempts.SingleOrDefaultAsync(item =>
            item.Id == questionToken.TestAttemptId &&
            item.ScreeningAttemptId == attempt.Id &&
            item.ScreeningTestId == question.ScreeningTestId,
            cancellationToken);
        if (testAttempt is null)
        {
            return Conflict(new { message = "This activity is no longer available." });
        }

        var configuredItems = GetTimedNamingItems(question.MetadataJson);
        var timeLimitSeconds = GetTimeLimitSeconds(question.MetadataJson);
        if (configuredItems.Count != 20 || timeLimitSeconds is null or < 1 or > 600)
        {
            return Conflict(new { message = "This activity does not have valid item and timing settings." });
        }

        var randomizedItems = configuredItems.ToArray();
        Shuffle(randomizedItems);
        var startedAtUtc = DateTime.UtcNow;
        var runPayload = new TimedNamingRunPayload(
            userId,
            attempt.Id,
            testAttempt.Id,
            question.Id,
            timeLimitSeconds.Value,
            new DateTimeOffset(startedAtUtc).ToUnixTimeMilliseconds(),
            randomizedItems);
        var runToken = _timedNamingTokenProtector.Protect(JsonSerializer.Serialize(runPayload));

        return Json(new
        {
            runToken,
            items = randomizedItems,
            itemCount = randomizedItems.Length,
            timeLimitSeconds
        });
    }

    [HttpPost("timed-naming/finish")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> FinishTimedNaming(
        TimedNamingFinishInputViewModel model,
        CancellationToken cancellationToken)
    {
        var userId = userManager.GetUserId(User);
        if (userId is null)
        {
            return Unauthorized();
        }

        if (!ModelState.IsValid || !TryUnprotectTimedNamingToken(model.RunToken, out var runPayload) || runPayload is null || runPayload.UserId != userId)
        {
            return BadRequest();
        }

        var existingResponse = await dbContext.ScreeningResponses
            .AsNoTracking()
            .AnyAsync(response =>
                response.ScreeningTestAttemptId == runPayload.TestAttemptId &&
                response.ScreeningQuestionId == runPayload.QuestionId &&
                response.ScreeningTestAttempt.ScreeningAttemptId == runPayload.AttemptId &&
                response.ScreeningTestAttempt.ScreeningAttempt.UserId == userId,
                cancellationToken);
        if (existingResponse)
        {
            return TimedNamingSavedResult();
        }

        var attempt = await FindIncompleteAttemptAsync(userId, tracked: true);
        if (attempt is null || attempt.Id != runPayload.AttemptId)
        {
            return Conflict(new { message = "This activity is no longer available." });
        }

        var question = await GetNextQuestionAsync(attempt.Id, cancellationToken);
        if (question is null || question.Id != runPayload.QuestionId || question.QuestionType != ScreeningQuestionType.TimedNaming)
        {
            return Conflict(new { message = "This activity is no longer available." });
        }

        var testAttempt = await dbContext.ScreeningTestAttempts.SingleOrDefaultAsync(item =>
            item.Id == runPayload.TestAttemptId &&
            item.ScreeningAttemptId == attempt.Id &&
            item.ScreeningTestId == question.ScreeningTestId,
            cancellationToken);
        if (testAttempt is null || runPayload.TimeLimitSeconds is < 1 or > 600 ||
            runPayload.PresentationOrder is not { Length: 20 } ||
            runPayload.PresentationOrder.Any(string.IsNullOrWhiteSpace))
        {
            return BadRequest();
        }

        int[]? completedOrder;
        int[]? selfReportedErrorIndexes;
        try
        {
            completedOrder = JsonSerializer.Deserialize<int[]>(model.CompletedOrderJson);
            selfReportedErrorIndexes = JsonSerializer.Deserialize<int[]>(model.SelfReportedErrorIndexesJson);
        }
        catch (JsonException)
        {
            return BadRequest();
        }

        if (completedOrder is null || completedOrder.Length > 20 ||
            !completedOrder.SequenceEqual(Enumerable.Range(0, completedOrder.Length)) ||
            selfReportedErrorIndexes is null ||
            selfReportedErrorIndexes.Distinct().Count() != selfReportedErrorIndexes.Length ||
            selfReportedErrorIndexes.Any(index => index < 0 || index >= completedOrder.Length))
        {
            return BadRequest();
        }

        var completedAtUtc = DateTime.UtcNow;
        var startedAtUtc = DateTimeOffset.FromUnixTimeMilliseconds(runPayload.StartedAtUnixMilliseconds).UtcDateTime;
        var elapsedMilliseconds = Math.Max(0L, (long)(completedAtUtc - startedAtUtc).TotalMilliseconds);
        var maximumDurationMilliseconds = runPayload.TimeLimitSeconds * 1000L;
        var timeLimitReached = elapsedMilliseconds >= maximumDurationMilliseconds;
        if (completedOrder.Length < 20 && !timeLimitReached)
        {
            return BadRequest();
        }

        var durationMilliseconds = Math.Min(elapsedMilliseconds, maximumDurationMilliseconds);
        var response = new ScreeningResponse
        {
            ScreeningTestAttemptId = testAttempt.Id,
            ScreeningQuestionId = question.Id,
            ResponseTimeMilliseconds = durationMilliseconds,
            ResponseDetailsJson = JsonSerializer.Serialize(new TimedNamingResponseDetails(
                runPayload.PresentationOrder,
                completedOrder.Length,
                timeLimitReached,
                selfReportedErrorIndexes.Order().ToArray()), MetadataJsonOptions),
            CreatedAtUtc = completedAtUtc
        };

        dbContext.ScreeningResponses.Add(response);
        if (await GetSupportedActiveQuestionQuery()
            .CountAsync(item => item.ScreeningTestId == question.ScreeningTestId, cancellationToken) == 1)
        {
            testAttempt.DurationMilliseconds = durationMilliseconds;
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(response).State = EntityState.Detached;
            var savedByAnotherRequest = await dbContext.ScreeningResponses.AnyAsync(item =>
                item.ScreeningTestAttemptId == testAttempt.Id && item.ScreeningQuestionId == question.Id,
                cancellationToken);
            if (!savedByAnotherRequest)
            {
                throw;
            }
        }

        return TimedNamingSavedResult();
    }

    [HttpPost("reading-fluency/start")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartReadingFluency(
        ReadingFluencyStartInputViewModel model,
        CancellationToken cancellationToken)
    {
        var userId = userManager.GetUserId(User);
        if (userId is null)
        {
            return Unauthorized();
        }

        if (!ModelState.IsValid || !TryUnprotectQuestionToken(model.QuestionToken, out var questionToken) || questionToken is null)
        {
            return BadRequest();
        }

        var attempt = await FindIncompleteAttemptAsync(userId, tracked: true);
        if (attempt is null || questionToken.UserId != userId || questionToken.AttemptId != attempt.Id)
        {
            return Conflict(new { message = "This activity is no longer available." });
        }

        var question = await GetNextQuestionAsync(attempt.Id, cancellationToken);
        if (question is null || question.QuestionType != ScreeningQuestionType.ReadingFluency || question.Id != questionToken.QuestionId)
        {
            return Conflict(new { message = "This activity is no longer available." });
        }

        var testAttempt = await dbContext.ScreeningTestAttempts.SingleOrDefaultAsync(item =>
            item.Id == questionToken.TestAttemptId &&
            item.ScreeningAttemptId == attempt.Id &&
            item.ScreeningTestId == question.ScreeningTestId,
            cancellationToken);
        var metadata = GetReadingFluencyMetadata(question.MetadataJson);
        if (testAttempt is null || metadata is null || string.IsNullOrWhiteSpace(metadata.PassageText))
        {
            return Conflict(new { message = "This activity does not have valid passage settings." });
        }

        var referenceWordCount = metadata.ReferenceWordCount ?? CountPassageWords(metadata.PassageText);
        if (referenceWordCount is < 1 or > 10000 || metadata.TargetWordsPerMinute is < 1 or > 1000)
        {
            return Conflict(new { message = "This activity does not have valid passage settings." });
        }

        var runPayload = new ReadingFluencyRunPayload(
            userId,
            attempt.Id,
            testAttempt.Id,
            question.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            referenceWordCount,
            metadata.TargetWordsPerMinute);
        var runToken = _readingFluencyTokenProtector.Protect(JsonSerializer.Serialize(runPayload));

        return Json(new { runToken });
    }

    [HttpPost("reading-fluency/finish")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> FinishReadingFluency(
        ReadingFluencyFinishInputViewModel model,
        CancellationToken cancellationToken)
    {
        var userId = userManager.GetUserId(User);
        if (userId is null)
        {
            return Unauthorized();
        }

        if (!ModelState.IsValid || !TryUnprotectReadingFluencyToken(model.RunToken, out var runPayload) ||
            runPayload is null || runPayload.UserId != userId)
        {
            return BadRequest();
        }

        var existingResponse = await dbContext.ScreeningResponses
            .AsNoTracking()
            .AnyAsync(response =>
                response.ScreeningTestAttemptId == runPayload.TestAttemptId &&
                response.ScreeningQuestionId == runPayload.QuestionId &&
                response.ScreeningTestAttempt.ScreeningAttemptId == runPayload.AttemptId &&
                response.ScreeningTestAttempt.ScreeningAttempt.UserId == userId,
                cancellationToken);
        if (existingResponse)
        {
            return ReadingFluencySavedResult();
        }

        var attempt = await FindIncompleteAttemptAsync(userId, tracked: true);
        if (attempt is null || attempt.Id != runPayload.AttemptId)
        {
            return Conflict(new { message = "This activity is no longer available." });
        }

        var question = await GetNextQuestionAsync(attempt.Id, cancellationToken);
        if (question is null || question.Id != runPayload.QuestionId || question.QuestionType != ScreeningQuestionType.ReadingFluency)
        {
            return Conflict(new { message = "This activity is no longer available." });
        }

        var testAttempt = await dbContext.ScreeningTestAttempts.SingleOrDefaultAsync(item =>
            item.Id == runPayload.TestAttemptId &&
            item.ScreeningAttemptId == attempt.Id &&
            item.ScreeningTestId == question.ScreeningTestId,
            cancellationToken);
        if (testAttempt is null || runPayload.ReferenceWordCount is < 1 or > 10000 ||
            runPayload.TargetWordsPerMinute is < 1 or > 1000)
        {
            return BadRequest();
        }

        var completedAtUtc = DateTime.UtcNow;
        var startedAtUtc = DateTimeOffset.FromUnixTimeMilliseconds(runPayload.StartedAtUnixMilliseconds).UtcDateTime;
        var elapsedMilliseconds = Math.Max(0L, (long)(completedAtUtc - startedAtUtc).TotalMilliseconds);
        var wordsPerMinute = elapsedMilliseconds > 0
            ? Math.Round(runPayload.ReferenceWordCount * 60000d / elapsedMilliseconds, 2)
            : (double?)null;
        var response = new ScreeningResponse
        {
            ScreeningTestAttemptId = testAttempt.Id,
            ScreeningQuestionId = question.Id,
            ResponseTimeMilliseconds = elapsedMilliseconds,
            ResponseDetailsJson = JsonSerializer.Serialize(new ReadingFluencyResponseDetails(
                runPayload.ReferenceWordCount,
                runPayload.TargetWordsPerMinute,
                wordsPerMinute,
                model.CorrectionCount), MetadataJsonOptions),
            CreatedAtUtc = completedAtUtc
        };

        dbContext.ScreeningResponses.Add(response);
        if (await GetSupportedActiveQuestionQuery()
            .CountAsync(item => item.ScreeningTestId == question.ScreeningTestId, cancellationToken) == 1)
        {
            testAttempt.DurationMilliseconds = elapsedMilliseconds;
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(response).State = EntityState.Detached;
            var savedByAnotherRequest = await dbContext.ScreeningResponses.AnyAsync(item =>
                item.ScreeningTestAttemptId == testAttempt.Id && item.ScreeningQuestionId == question.Id,
                cancellationToken);
            if (!savedByAnotherRequest)
            {
                throw;
            }
        }

        return ReadingFluencySavedResult();
    }

    private JsonResult TimedNamingSavedResult() => Json(new { continueUrl = Url.Action(nameof(Resume)) });

    private JsonResult ReadingFluencySavedResult() => Json(new { continueUrl = Url.Action(nameof(Resume)) });

    [HttpGet("audio/{questionId:int}")]
    public async Task<IActionResult> Audio(int questionId, CancellationToken cancellationToken)
    {
        var userId = userManager.GetUserId(User);
        if (userId is null)
        {
            return Unauthorized();
        }

        var attempt = await FindIncompleteAttemptAsync(userId, tracked: false);
        var question = attempt is null ? null : await GetNextQuestionAsync(attempt.Id, cancellationToken);
        if (question is null || question.Id != questionId || question.QuestionType != ScreeningQuestionType.ListeningResponse ||
            !ScreeningAudioSource.TryResolve(question.AudioPath, webHostEnvironment.ContentRootPath, out var filePath, out var contentType))
        {
            return NotFound("The configured audio file is unavailable.");
        }

        try
        {
            var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            Response.Headers["Cache-Control"] = "private, no-store";
            return File(stream, contentType, enableRangeProcessing: true);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException or UnauthorizedAccessException or IOException)
        {
            return NotFound("The configured audio file is unavailable.");
        }
    }

    private bool TryUnprotectQuestionToken(string? token, out QuestionTokenPayload? payload)
    {
        payload = null;
        if (string.IsNullOrWhiteSpace(token) || token.Length > 4096)
        {
            return false;
        }

        try
        {
            payload = JsonSerializer.Deserialize<QuestionTokenPayload>(_questionTokenProtector.Unprotect(token));
            return payload is not null;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or ArgumentException)
        {
            return false;
        }
    }

    private bool TryUnprotectTimedNamingToken(string? token, out TimedNamingRunPayload? payload)
    {
        payload = null;
        if (string.IsNullOrWhiteSpace(token) || token.Length > 4096)
        {
            return false;
        }

        try
        {
            payload = JsonSerializer.Deserialize<TimedNamingRunPayload>(_timedNamingTokenProtector.Unprotect(token));
            return payload is not null;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or ArgumentException)
        {
            return false;
        }
    }

    private bool TryUnprotectReadingFluencyToken(string? token, out ReadingFluencyRunPayload? payload)
    {
        payload = null;
        if (string.IsNullOrWhiteSpace(token) || token.Length > 4096)
        {
            return false;
        }

        try
        {
            payload = JsonSerializer.Deserialize<ReadingFluencyRunPayload>(_readingFluencyTokenProtector.Unprotect(token));
            return payload is not null;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or ArgumentException)
        {
            return false;
        }
    }

    private static void Shuffle<T>(T[] items)
    {
        for (var index = items.Length - 1; index > 0; index--)
        {
            var swapIndex = RandomNumberGenerator.GetInt32(index + 1);
            (items[index], items[swapIndex]) = (items[swapIndex], items[index]);
        }
    }

    private static IReadOnlyList<string> GetTimedNamingItems(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return Array.Empty<string>();
        }

        try
        {
            var metadata = JsonSerializer.Deserialize<ScreeningQuestionMetadata>(metadataJson, MetadataJsonOptions);
            return metadata?.TimedNamingItems?.Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .ToArray() ?? Array.Empty<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static int? GetTimeLimitSeconds(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ScreeningQuestionMetadata>(metadataJson, MetadataJsonOptions)?.TimeLimitSeconds;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyList<string> GetWorkingMemorySequence(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return Array.Empty<string>();
        }

        try
        {
            var metadata = JsonSerializer.Deserialize<ScreeningQuestionMetadata>(metadataJson, MetadataJsonOptions);
            return metadata?.WorkingMemorySequence?.Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .ToArray() ?? Array.Empty<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private (bool IsCorrect, double Score)? ScoreAnswerKeyResponse(
        ScreeningQuestion question,
        string submittedAnswer)
    {
        var answerKeys = new List<string>();
        if (!string.IsNullOrWhiteSpace(question.CorrectAnswer))
        {
            answerKeys.Add(question.CorrectAnswer);
        }

        if (!string.IsNullOrWhiteSpace(question.AcceptedAnswersJson))
        {
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
                // A usable CorrectAnswer remains valid if optional aliases are malformed.
            }
        }

        if (answerKeys.Count == 0)
        {
            return null;
        }

        var normalizedAnswer = _screeningScoringOptions.TrimAnswerWhitespace
            ? submittedAnswer.Trim()
            : submittedAnswer;
        var comparison = _screeningScoringOptions.IgnoreAnswerCase
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var isCorrect = answerKeys.Any(answerKey => string.Equals(
            normalizedAnswer,
            _screeningScoringOptions.TrimAnswerWhitespace ? answerKey.Trim() : answerKey,
            comparison));

        return (isCorrect, isCorrect ? 1d : 0d);
    }

    private static int? GetMemoryDisplayDurationMilliseconds(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ScreeningQuestionMetadata>(metadataJson, MetadataJsonOptions)
                ?.MemoryDisplayDurationMilliseconds;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int? GetPatternDifficultyLevel(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ScreeningQuestionMetadata>(metadataJson, MetadataJsonOptions)
                ?.PatternDifficultyLevel;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ScreeningQuestionMetadata? GetReadingFluencyMetadata(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ScreeningQuestionMetadata>(metadataJson, MetadataJsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int CountPassageWords(string passageText) =>
        Regex.Matches(passageText, @"[\p{L}\p{N}]+(?:[’'-][\p{L}\p{N}]+)*", RegexOptions.CultureInvariant).Count;

    private static ScreeningReportSkillAreaSnapshot[] DeserializeSkillAreas(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ScreeningReportSkillAreaSnapshot[]>(json, MetadataJsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static ScreeningSkillAreaResultViewModel MapSkillAreaSnapshot(ScreeningReportSkillAreaSnapshot snapshot) =>
        new(
            snapshot.Name,
            snapshot.PercentageScore is { } percentage ? (double)percentage : null,
            snapshot.IsStrength,
            snapshot.IsFlaggedForSupport,
            snapshot.Summary)
        {
            ActivityMetrics = snapshot.ActivityMetrics
        };

    private static bool IsMemoryAndBroaderThinkingSkills(ScreeningSkillAreaResultViewModel skillArea) =>
        string.Equals(skillArea.Name, MemoryAndBroaderThinkingSkillsCategoryName, StringComparison.Ordinal);

    private static ScreeningSkillAreaResultViewModel[] GetRelativeStrengths(ScreeningSkillAreaResultViewModel[] skillAreas)
    {
        var scoredCandidates = skillAreas
            .Where(skillArea => skillArea.IsStrength && skillArea.PercentageScore is > 0)
            .Select(skillArea => skillArea.PercentageScore!.Value)
            .ToArray();
        var highestMatchRate = scoredCandidates.Length == 0 ? (double?)null : scoredCandidates.Max();

        return skillAreas.Select(skillArea => skillArea with
            {
                IsStrength = highestMatchRate is { } highest &&
                             skillArea.IsStrength &&
                             skillArea.PercentageScore == highest
            })
            .ToArray();
    }

    private static ScreeningSkillAreaResultViewModel BuildSkillAreaResult(ScreeningTestAttempt testAttempt)
    {
        var percentage = testAttempt.PercentageScore;
        var isFlaggedForSupport = testAttempt.HasFlaggedSkillArea == true;
        var isStrength = percentage.HasValue && testAttempt.HasFlaggedSkillArea == false;
        var summary = percentage is { } score
            ? $"Responses matched {score.ToString("0.##", CultureInfo.CurrentCulture)}% of the configured answer-key points. " +
              (isFlaggedForSupport
                  ? "This area met the configured rule for additional support review."
                  : testAttempt.HasFlaggedSkillArea == false
                      ? "The configured review rule did not flag this area for additional support review."
                      : "An additional support flag was not available for this area.")
            : "An objective answer-key summary was not available for this area.";

        return new ScreeningSkillAreaResultViewModel(
            testAttempt.ScreeningTest.Name,
            percentage,
            isStrength,
            isFlaggedForSupport,
            summary);
    }

    private static ScreeningSupportTier? GetStoredSupportTier(string? storedTier) =>
        Enum.TryParse<ScreeningSupportTier>(storedTier, ignoreCase: true, out var tier) && Enum.IsDefined(tier)
            ? tier
            : null;

    private static (string Explanation, string RecommendedNextAction) GetSupportTierCopy(ScreeningSupportTier? tier) => tier switch
    {
        ScreeningSupportTier.Green => (
            "Your responses showed mild indicators under the configured screening rules. Standard learning support may be a useful next step. Screening may also help identify when additional support or a professional assessment could be useful. This result reflects this screening activity only.",
            "Continue with learning supports that work for you. If you or someone supporting you has questions, consider discussing this result with a suitable support professional."),
        ScreeningSupportTier.Amber => (
            "Your responses showed moderate indicators under the configured screening rules. Additional learning support or a professional assessment could be useful.",
            "Consider talking with a trusted adult or suitable support professional about the areas below. A suitable support professional may review this screening according to the LexiAssist support workflow."),
        ScreeningSupportTier.Red => (
            "Your responses showed stronger indicators under the configured screening rules. Additional learning support or a professional assessment could be useful.",
            "Consider discussing next steps with a suitable support professional. A suitable support professional may review this screening according to the LexiAssist support workflow."),
        _ => (
            "There was not enough scored information to calculate a screening/support tier. Your completion is still recorded.",
            "If you would like help planning next steps, consider reviewing the area summaries with a trusted support person or suitable professional.")
    };

    private string? GetListeningAudioError(ScreeningQuestion question, ScreeningQuestionMetadata? metadata)
    {
        if (question.QuestionType != ScreeningQuestionType.ListeningResponse)
        {
            return null;
        }

        if (metadata?.MaxAudioReplays is < 0 or > 20)
        {
            return "The audio replay setting is invalid. Your progress is saved; please contact your support person.";
        }

        if (!ScreeningAudioSource.TryGetContentType(question.AudioPath, out _, out _))
        {
            return "The configured audio source is not supported. Please contact your support person to check the audio file name.";
        }

        if (!ScreeningAudioSource.TryResolve(question.AudioPath, webHostEnvironment.ContentRootPath, out _, out _))
        {
            return "The configured audio file is missing or unavailable. Your progress is saved; please contact your support person.";
        }

        return null;
    }

    private async Task<ScreeningAttempt?> FindIncompleteAttemptAsync(string userId, bool tracked) =>
        await (tracked ? dbContext.ScreeningAttempts : dbContext.ScreeningAttempts.AsNoTracking())
            .Where(attempt => attempt.UserId == userId && attempt.Status == ScreeningAttemptStatus.InProgress)
            .OrderByDescending(attempt => attempt.StartedAtUtc)
            .FirstOrDefaultAsync();

    private IQueryable<ScreeningQuestion> GetSupportedActiveQuestionQuery() => GetSupportedQuestionQuery(includeStartedInactiveCategory: false, attemptId: null);

    private IQueryable<ScreeningQuestion> GetSupportedQuestionQueryForAttempt(int attemptId) =>
        GetSupportedQuestionQuery(includeStartedInactiveCategory: true, attemptId: attemptId);

    private IQueryable<ScreeningQuestion> GetSupportedQuestionQuery(bool includeStartedInactiveCategory, int? attemptId)
    {
        var query = dbContext.ScreeningQuestions
        .AsNoTracking()
        .Where(question => question.IsActive);

        if (includeStartedInactiveCategory && attemptId is { } existingAttemptId)
        {
            var startedTestIds = dbContext.ScreeningTestAttempts
                .Where(testAttempt => testAttempt.ScreeningAttemptId == existingAttemptId)
                .Select(testAttempt => testAttempt.ScreeningTestId);
            query = query.Where(question => question.ScreeningTest.IsActive || startedTestIds.Contains(question.ScreeningTestId));
        }
        else
        {
            query = query.Where(question => question.ScreeningTest.IsActive);
        }

        return query
        .Where(question =>
            question.QuestionType == ScreeningQuestionType.MultipleChoice ||
            question.QuestionType == ScreeningQuestionType.TextResponse ||
            question.QuestionType == ScreeningQuestionType.WordReading ||
            question.QuestionType == ScreeningQuestionType.NonWordReading ||
            question.QuestionType == ScreeningQuestionType.Spelling ||
            question.QuestionType == ScreeningQuestionType.PhonologicalChoice ||
            question.QuestionType == ScreeningQuestionType.TimedNaming ||
            question.QuestionType == ScreeningQuestionType.ReadingFluency ||
            question.QuestionType == ScreeningQuestionType.ReadingPassage ||
            question.QuestionType == ScreeningQuestionType.ListeningResponse ||
            question.QuestionType == ScreeningQuestionType.WorkingMemorySequence ||
            question.QuestionType == ScreeningQuestionType.TimedMatching ||
            question.QuestionType == ScreeningQuestionType.PatternReasoning)
        .OrderBy(question => question.ScreeningTest.SortOrder)
        .ThenBy(question => question.ScreeningTestId)
        .ThenBy(question => question.SortOrder)
        .ThenBy(question => question.Id);
    }

    private IQueryable<ScreeningQuestion> GetUnansweredQuestionQuery(int attemptId)
    {
        var answeredQuestionIds = dbContext.ScreeningResponses
            .Where(response => response.ScreeningTestAttempt.ScreeningAttemptId == attemptId)
            .Select(response => response.ScreeningQuestionId);

        return GetSupportedQuestionQueryForAttempt(attemptId)
            .Where(question => !answeredQuestionIds.Contains(question.Id));
    }

    private Task<ScreeningQuestion?> GetNextQuestionAsync(int attemptId, CancellationToken cancellationToken) =>
        GetUnansweredQuestionQuery(attemptId)
            .Include(question => question.ScreeningTest)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<ScreeningTestAttempt> GetOrCreateTestAttemptAsync(
        ScreeningAttempt attempt,
        int testId,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.ScreeningTestAttempts.SingleOrDefaultAsync(testAttempt =>
            testAttempt.ScreeningAttemptId == attempt.Id && testAttempt.ScreeningTestId == testId,
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var created = new ScreeningTestAttempt
        {
            ScreeningAttemptId = attempt.Id,
            ScreeningTestId = testId,
            StartedAtUtc = DateTime.UtcNow
        };
        dbContext.ScreeningTestAttempts.Add(created);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return created;
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(created).State = EntityState.Detached;
            var concurrent = await dbContext.ScreeningTestAttempts.SingleOrDefaultAsync(testAttempt =>
                testAttempt.ScreeningAttemptId == attempt.Id && testAttempt.ScreeningTestId == testId,
                cancellationToken);
            if (concurrent is null)
            {
                throw;
            }

            return concurrent;
        }
    }

    private async Task<(int Answered, int Total)> GetProgressAsync(int attemptId, CancellationToken cancellationToken)
    {
        var supportedQuestionIds = GetSupportedQuestionQueryForAttempt(attemptId)
            .Select(question => question.Id);
        var total = await supportedQuestionIds.CountAsync(cancellationToken);
        var answered = await dbContext.ScreeningResponses
            .CountAsync(response =>
                response.ScreeningTestAttempt.ScreeningAttemptId == attemptId &&
                supportedQuestionIds.Contains(response.ScreeningQuestionId),
                cancellationToken);
        return (answered, total);
    }

    private ScreeningRunnerViewModel BuildQuestionViewModel(
        ScreeningAttempt attempt,
        int testAttemptId,
        ScreeningQuestion question,
        string questionToken,
        int answeredCount,
        int totalCount)
    {
        var questionNumber = Math.Min(answeredCount + 1, Math.Max(totalCount, 1));
        var namingItems = question.QuestionType == ScreeningQuestionType.TimedNaming
            ? GetTimedNamingItems(question.MetadataJson)
            : Array.Empty<string>();
        var namingTimeLimitSeconds = question.QuestionType == ScreeningQuestionType.TimedNaming
            ? GetTimeLimitSeconds(question.MetadataJson)
            : null;
        var workingMemorySequence = question.QuestionType == ScreeningQuestionType.WorkingMemorySequence
            ? GetWorkingMemorySequence(question.MetadataJson)
            : Array.Empty<string>();
        var memoryDisplayDurationMilliseconds = question.QuestionType == ScreeningQuestionType.WorkingMemorySequence
            ? GetMemoryDisplayDurationMilliseconds(question.MetadataJson)
            : null;
        var timedMatchingTimeLimitSeconds = question.QuestionType == ScreeningQuestionType.TimedMatching
            ? GetTimeLimitSeconds(question.MetadataJson)
            : null;
        var timedMatchingReferenceMapping = question.QuestionType == ScreeningQuestionType.TimedMatching
            ? GetTimedMatchingReferenceMapping(question.MetadataJson)
            : new Dictionary<string, string>(StringComparer.Ordinal);
        var timedMatchingItems = question.QuestionType == ScreeningQuestionType.TimedMatching
            ? GetTimedMatchingItems(question.MetadataJson)
            : Array.Empty<string>();
        var patternDifficultyLevel = question.QuestionType == ScreeningQuestionType.PatternReasoning
            ? GetPatternDifficultyLevel(question.MetadataJson)
            : null;
        var patternImagePath = question.QuestionType == ScreeningQuestionType.PatternReasoning
            ? question.ImagePath
            : null;
        var fluencyMetadata = question.QuestionType is ScreeningQuestionType.ReadingFluency or
            ScreeningQuestionType.ReadingPassage or ScreeningQuestionType.ListeningResponse
            ? GetReadingFluencyMetadata(question.MetadataJson)
            : null;
        var passageText = fluencyMetadata?.PassageText;
        var referenceWordCount = fluencyMetadata?.ReferenceWordCount ??
            (string.IsNullOrWhiteSpace(passageText) ? 0 : CountPassageWords(passageText));
        var choices = question.QuestionType is ScreeningQuestionType.MultipleChoice or
            ScreeningQuestionType.PhonologicalChoice or
            ScreeningQuestionType.TimedMatching or
            ScreeningQuestionType.PatternReasoning
            ? GetChoiceOptions(question.MetadataJson)
            : Array.Empty<string>();
        var malformedChoiceQuestion = question.QuestionType is ScreeningQuestionType.MultipleChoice or
            ScreeningQuestionType.PhonologicalChoice or
            ScreeningQuestionType.TimedMatching or
            ScreeningQuestionType.PatternReasoning
            && choices.Count < 2;

        var malformedTimedNamingQuestion = question.QuestionType == ScreeningQuestionType.TimedNaming &&
            (namingItems.Count != 20 || namingTimeLimitSeconds is null or < 1 or > 600);
        var malformedReadingFluencyQuestion = question.QuestionType == ScreeningQuestionType.ReadingFluency &&
            (string.IsNullOrWhiteSpace(passageText) || referenceWordCount is < 1 or > 10000 ||
             fluencyMetadata?.TargetWordsPerMinute is < 1 or > 1000);
        var malformedReadingComprehensionQuestion = question.QuestionType == ScreeningQuestionType.ReadingPassage &&
            string.IsNullOrWhiteSpace(passageText);
        var listeningAudioError = GetListeningAudioError(question, fluencyMetadata);
        var malformedListeningComprehensionQuestion = question.QuestionType == ScreeningQuestionType.ListeningResponse &&
            listeningAudioError is not null;
        var malformedWorkingMemoryQuestion = question.QuestionType == ScreeningQuestionType.WorkingMemorySequence &&
            (workingMemorySequence.Count is < 2 or > 12 ||
             memoryDisplayDurationMilliseconds is null or < 1000 or > 30000);
        var malformedTimedMatchingQuestion = question.QuestionType == ScreeningQuestionType.TimedMatching &&
            (timedMatchingTimeLimitSeconds is null or < 1 or > 600 ||
             (timedMatchingReferenceMapping.Count > 0 &&
              (timedMatchingReferenceMapping.Count < 2 ||
               timedMatchingItems.Count == 0 ||
               timedMatchingItems.Any(item => !timedMatchingReferenceMapping.ContainsKey(item)) ||
               timedMatchingReferenceMapping.Values.Any(value => !choices.Contains(value, StringComparer.Ordinal)))));
        var malformedPatternReasoningQuestion = question.QuestionType == ScreeningQuestionType.PatternReasoning &&
            patternDifficultyLevel is < 1 or > 5;
        var malformedQuestion = malformedChoiceQuestion || malformedTimedNamingQuestion || malformedReadingFluencyQuestion ||
                                malformedReadingComprehensionQuestion || malformedListeningComprehensionQuestion ||
                                malformedWorkingMemoryQuestion || malformedTimedMatchingQuestion || malformedPatternReasoningQuestion;
        var audioSourceUrl = question.QuestionType == ScreeningQuestionType.ListeningResponse && !malformedListeningComprehensionQuestion
            ? Url.Action(nameof(Audio), new { questionId = question.Id })
            : null;

        return new ScreeningRunnerViewModel
        {
            AttemptStartedAtUtc = attempt.StartedAtUtc,
            IsUnavailable = malformedQuestion,
            IsTimedNaming = question.QuestionType == ScreeningQuestionType.TimedNaming,
            IsReadingFluency = question.QuestionType == ScreeningQuestionType.ReadingFluency,
            IsReadingComprehension = question.QuestionType == ScreeningQuestionType.ReadingPassage,
            IsListeningComprehension = question.QuestionType == ScreeningQuestionType.ListeningResponse,
            IsWorkingMemorySequence = question.QuestionType == ScreeningQuestionType.WorkingMemorySequence,
            IsTimedMatching = question.QuestionType == ScreeningQuestionType.TimedMatching,
            IsPatternReasoning = question.QuestionType == ScreeningQuestionType.PatternReasoning,
            UnavailableMessage = listeningAudioError ?? (malformedReadingComprehensionQuestion
                ? "The passage for this question is missing. Your progress is saved; please contact your support person."
                : malformedQuestion
                    ? "This question is not ready. Your progress is saved; please contact your support person."
                    : null),
            ScreeningTestName = question.ScreeningTest.Name,
            TestInstructions = string.Equals(
                question.ScreeningTest.Instructions,
                "No screening items are configured yet.",
                StringComparison.OrdinalIgnoreCase)
                ? null
                : question.ScreeningTest.Instructions,
            Prompt = malformedQuestion ? null : question.Prompt,
            QuestionType = question.QuestionType,
            QuestionTypeName = question.QuestionType.GetType().GetMember(question.QuestionType.ToString())
                .FirstOrDefault()?.GetCustomAttribute<DisplayAttribute>()?.GetName() ?? question.QuestionType.ToString(),
            AnswerChoices = choices,
            PassageText = malformedQuestion ? null : passageText,
            WorkingMemorySequence = malformedQuestion ? Array.Empty<string>() : workingMemorySequence,
            MemoryDisplayDurationMilliseconds = memoryDisplayDurationMilliseconds ?? 0,
            TimedMatchingTimeLimitSeconds = timedMatchingTimeLimitSeconds ?? 0,
            TimedMatchingReferenceMapping = malformedQuestion
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : timedMatchingReferenceMapping,
            TimedMatchingItems = malformedQuestion ? Array.Empty<string>() : timedMatchingItems,
            ImagePath = malformedQuestion ? null : patternImagePath,
            PatternDifficultyLevel = malformedQuestion ? null : patternDifficultyLevel,
            AudioSourceUrl = audioSourceUrl,
            MaxAudioReplays = fluencyMetadata?.MaxAudioReplays,
            QuestionToken = malformedQuestion ? null : questionToken,
            TimedNamingItemCount = namingItems.Count,
            TimedNamingTimeLimitSeconds = namingTimeLimitSeconds ?? 0,
            QuestionNumber = questionNumber,
            TotalQuestionCount = totalCount,
            ProgressPercentage = totalCount == 0 ? 0 : (int)Math.Ceiling(questionNumber * 100.0 / totalCount),
            AudioRecorder = attempt.Status == ScreeningAttemptStatus.InProgress &&
                            question.AllowsAudioRecording && question.IsActive && question.ScreeningTest.IsActive
                ? new LearnerAudioRecorderViewModel(
                    Url.Action("StartForScreening", "LearnerAudioRecordings", new { testAttemptId, questionId = question.Id })!,
                    Url.Action("ForScreening", "LearnerAudioRecordings", new { testAttemptId, questionId = question.Id })!,
                    Math.Clamp(audioRecordingOptions.Value.MaxDurationSeconds, 1, 600))
                : null
        };
    }

    private static IReadOnlyList<string> GetChoiceOptions(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return Array.Empty<string>();
        }

        try
        {
            var metadata = JsonSerializer.Deserialize<ScreeningQuestionMetadata>(metadataJson, MetadataJsonOptions);
            return metadata?.ChoiceOptions?.Where(option => !string.IsNullOrWhiteSpace(option)).ToArray()
                ?? Array.Empty<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static IReadOnlyDictionary<string, string> GetTimedMatchingReferenceMapping(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        try
        {
            var metadata = JsonSerializer.Deserialize<ScreeningQuestionMetadata>(metadataJson, MetadataJsonOptions);
            return metadata?.TimedMatchingReferenceMapping is { Count: > 0 } mapping
                ? mapping
                    .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                    .ToDictionary(pair => pair.Key.Trim(), pair => pair.Value.Trim(), StringComparer.Ordinal)
                : new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private static IReadOnlyList<string> GetTimedMatchingItems(string? metadataJson)
    {
        var mapping = GetTimedMatchingReferenceMapping(metadataJson);
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return Array.Empty<string>();
        }

        try
        {
            var metadata = JsonSerializer.Deserialize<ScreeningQuestionMetadata>(metadataJson, MetadataJsonOptions);
            var configuredItems = metadata?.TimedMatchingItems?
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .ToArray();
            return configuredItems is { Length: > 0 }
                ? configuredItems
                : mapping.Keys.ToArray();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static bool IsProcessingSpeedQuestion(string? metadataJson) =>
        GetTimedMatchingReferenceMapping(metadataJson).Count >= 2 &&
        GetTimedMatchingItems(metadataJson).Count > 0;

    private static IReadOnlyList<string> ParseTimedMatchingAnswers(string? answersJson)
    {
        if (string.IsNullOrWhiteSpace(answersJson))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(answersJson);
            return document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.EnumerateArray()
                    .Where(value => value.ValueKind == JsonValueKind.String)
                    .Select(value => value.GetString()?.Trim())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value!)
                    .ToArray()
                : Array.Empty<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static TimedMatchingScore ScoreTimedMatchingResponse(
        string? metadataJson,
        IReadOnlyList<string> answers)
    {
        var mapping = GetTimedMatchingReferenceMapping(metadataJson);
        var items = GetTimedMatchingItems(metadataJson);
        var correctCount = answers
            .Select((answer, index) => index < items.Count &&
                mapping.TryGetValue(items[index], out var expected) &&
                string.Equals(answer, expected, StringComparison.Ordinal))
            .Count(isCorrect => isCorrect);
        var attemptedCount = Math.Min(answers.Count, items.Count);
        var incorrectCount = attemptedCount - correctCount;
        var accuracy = items.Count == 0 ? 0d : correctCount / (double)items.Count;
        return new TimedMatchingScore(attemptedCount, correctCount, incorrectCount, items.Count, accuracy);
    }

    private void ValidateAnswer(ScreeningAnswerInputViewModel model, ScreeningQuestion question, bool timedOut)
    {
        if (question.QuestionType == ScreeningQuestionType.TimedMatching &&
            IsProcessingSpeedQuestion(question.MetadataJson))
        {
            if (string.IsNullOrWhiteSpace(model.TimedMatchingAnswersJson))
            {
                if (!timedOut)
                {
                    ModelState.AddModelError(nameof(model.TimedMatchingAnswersJson), "Complete the matching items to continue.");
                }

                return;
            }

            var answers = ParseTimedMatchingAnswers(model.TimedMatchingAnswersJson);
            var items = GetTimedMatchingItems(question.MetadataJson);
            var choices = GetChoiceOptions(question.MetadataJson);
            if (answers.Count > items.Count || (!timedOut && answers.Count != items.Count))
            {
                ModelState.AddModelError(nameof(model.TimedMatchingAnswersJson), "Complete each matching item before saving.");
                return;
            }

            if (answers.Any(answer => !choices.Contains(answer, StringComparer.Ordinal)))
            {
                ModelState.AddModelError(nameof(model.TimedMatchingAnswersJson), "Use one of the listed matching symbols.");
            }

            return;
        }

        if (question.QuestionType is ScreeningQuestionType.MultipleChoice or
            ScreeningQuestionType.PhonologicalChoice or
            ScreeningQuestionType.TimedMatching or
            ScreeningQuestionType.PatternReasoning)
        {
            if (question.QuestionType == ScreeningQuestionType.TimedMatching && timedOut)
            {
                return;
            }

            var choices = GetChoiceOptions(question.MetadataJson);
            if (string.IsNullOrWhiteSpace(model.SelectedAnswer) ||
                !choices.Contains(model.SelectedAnswer.Trim(), StringComparer.Ordinal))
            {
                ModelState.AddModelError(nameof(model.SelectedAnswer), "Choose one of the listed answers.");
            }

            return;
        }

        if ((question.QuestionType is ScreeningQuestionType.TextResponse or
             ScreeningQuestionType.WordReading or
             ScreeningQuestionType.NonWordReading or
             ScreeningQuestionType.Spelling or
             ScreeningQuestionType.ReadingPassage or
             ScreeningQuestionType.ListeningResponse or
             ScreeningQuestionType.WorkingMemorySequence) &&
            string.IsNullOrWhiteSpace(model.TextAnswer))
        {
            ModelState.AddModelError(nameof(model.TextAnswer), "Enter a response to continue.");
        }
    }

    private string ProtectQuestionToken(QuestionTokenPayload tokenPayload) =>
        _questionTokenProtector.Protect(JsonSerializer.Serialize(tokenPayload));

    private Task<List<ScreeningCategoryViewModel>> GetActiveCategoriesAsync() =>
        dbContext.ScreeningTests
            .AsNoTracking()
            .Where(test => test.IsActive)
            .OrderBy(test => test.SortOrder)
            .ThenBy(test => test.Id)
            .Select(test => new ScreeningCategoryViewModel(test.Id, test.Name, test.Description))
            .ToListAsync();

    private sealed record QuestionTokenPayload(
        string UserId,
        int AttemptId,
        int TestAttemptId,
        int QuestionId,
        long PresentedAtUnixMilliseconds);

    private sealed record TimedNamingRunPayload(
        string UserId,
        int AttemptId,
        int TestAttemptId,
        int QuestionId,
        int TimeLimitSeconds,
        long StartedAtUnixMilliseconds,
        string[] PresentationOrder);

    private sealed record TimedNamingResponseDetails(
        string[] PresentationOrder,
        int CompletedItemCount,
        bool TimeLimitReached,
        int[] SelfReportedErrorIndexes);

    private sealed record ReadingFluencyRunPayload(
        string UserId,
        int AttemptId,
        int TestAttemptId,
        int QuestionId,
        long StartedAtUnixMilliseconds,
        int ReferenceWordCount,
        int? TargetWordsPerMinute);

    private sealed record ReadingFluencyResponseDetails(
        int ReferenceWordCount,
        int? TargetWordsPerMinute,
        double? WordsPerMinute,
        int? SelfReportedCorrectionCount);

    private sealed record TimedMatchingResponseDetails(
        int TimeLimitSeconds,
        bool TimeLimitReached,
        int? AttemptedCount = null,
        int? CorrectCount = null,
        int? IncorrectCount = null,
        int? ItemCount = null);

    private sealed record TimedMatchingScore(
        int AttemptedCount,
        int CorrectCount,
        int IncorrectCount,
        int ItemCount,
        double Accuracy);
}
