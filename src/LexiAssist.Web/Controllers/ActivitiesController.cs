using System.Security.Claims;
using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Activities;
using LexiAssist.Web.Models.Identity;
using LexiAssist.Web.Services;
using LexiAssist.Web.Services.Interfaces;
using LexiAssist.Web.ViewModels.Activities;
using LexiAssist.Web.ViewModels.Recordings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LexiAssist.Web.Controllers;

[Authorize(Roles = ApplicationRoles.Learner)]
[Route("activities")]
public sealed class ActivitiesController(
    ApplicationDbContext dbContext,
    ISoundBuilderGameService soundBuilderGameService,
    IFastNameRaceGameService fastNameRaceGameService,
    IWordReaderGameService wordReaderGameService,
    ISyllableSlicerGameService syllableSlicerGameService,
    ISayWriteMatchGameService sayWriteMatchGameService,
    IOptions<LearnerAudioRecordingOptions> audioRecordingOptions) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var learnerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (learnerId is null)
        {
            return Unauthorized();
        }

        var assignments = await dbContext.ActivityAssignments
            .AsNoTracking()
            .Where(assignment => assignment.LearnerId == learnerId)
            .Include(assignment => assignment.LearningActivity)
            .Include(assignment => assignment.Attempts)
            .OrderByDescending(assignment => assignment.AssignedAtUtc)
            .ToListAsync(cancellationToken);

        return View(new LearnerActivitiesIndexViewModel
        {
            Assignments = assignments.Select(assignment =>
            {
                var latestAttempt = assignment.Attempts
                    .OrderByDescending(attempt => attempt.StartedAtUtc)
                    .ThenByDescending(attempt => attempt.Id)
                    .FirstOrDefault();
                var canPlaySoundBuilder = assignment.LearningActivity.IsActive &&
                                          assignment.LearningActivity.ActivityType == LearningActivityType.PhonologicalAwareness &&
                                          string.Equals(assignment.LearningActivity.Name, "Sound Builder", StringComparison.OrdinalIgnoreCase) &&
                                          assignment.Status != ActivityAssignmentStatus.Cancelled;
                var canPlayFastNameRace = assignment.LearningActivity.IsActive &&
                                          assignment.LearningActivity.ActivityType == LearningActivityType.Fluency &&
                                          string.Equals(assignment.LearningActivity.Name, "Fast Name Race", StringComparison.OrdinalIgnoreCase) &&
                                          assignment.Status != ActivityAssignmentStatus.Cancelled;
                var canPlayWordReader = assignment.LearningActivity.IsActive &&
                                        assignment.LearningActivity.ActivityType == LearningActivityType.Reading &&
                                        string.Equals(assignment.LearningActivity.Name, "Word Reader", StringComparison.OrdinalIgnoreCase) &&
                                        assignment.Status != ActivityAssignmentStatus.Cancelled;
                var canPlaySyllableSlicer = assignment.LearningActivity.IsActive &&
                                            assignment.LearningActivity.ActivityType == LearningActivityType.Reading &&
                                            string.Equals(assignment.LearningActivity.Name, "Syllable Slicer", StringComparison.OrdinalIgnoreCase) &&
                                            assignment.Status != ActivityAssignmentStatus.Cancelled;
                var canPlaySayWriteMatch = assignment.LearningActivity.IsActive &&
                                           assignment.LearningActivity.ActivityType == LearningActivityType.Spelling &&
                                           string.Equals(assignment.LearningActivity.Name, "Say-Write-Match", StringComparison.OrdinalIgnoreCase) &&
                                           assignment.Status != ActivityAssignmentStatus.Cancelled;
                return new LearnerActivityAssignmentViewModel(
                    assignment.Id,
                    assignment.LearningActivity.Name,
                    assignment.LearningActivity.Description,
                    assignment.Status,
                    assignment.AssignedAtUtc,
                    assignment.CompletedAtUtc,
                    latestAttempt?.Accuracy,
                    latestAttempt?.DurationMilliseconds,
                    canPlaySoundBuilder,
                    canPlayFastNameRace,
                    canPlayWordReader,
                    canPlaySyllableSlicer,
                    canPlaySayWriteMatch);
            }).ToArray()
        });
    }

    [HttpGet("{assignmentId:int}/sound-builder")]
    public async Task<IActionResult> SoundBuilder(int assignmentId, CancellationToken cancellationToken)
    {
        var learnerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (learnerId is null)
        {
            return Unauthorized();
        }

        var session = await soundBuilderGameService.GetSessionAsync(learnerId, assignmentId, cancellationToken);
        if (session is null)
        {
            return NotFound();
        }

        var viewModel = ToViewModel(session);
        viewModel.AudioRecorder = await CreateAudioRecorderAsync(session.AssignmentId, session.AttemptId, cancellationToken);
        return View(viewModel);
    }

    [HttpPost("{assignmentId:int}/sound-builder/start")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartSoundBuilder(int assignmentId, CancellationToken cancellationToken)
    {
        var learnerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (learnerId is null)
        {
            return Unauthorized();
        }

        var session = await soundBuilderGameService.StartAsync(learnerId, assignmentId, cancellationToken);
        if (session is null)
        {
            return NotFound();
        }

        return RedirectToAction(nameof(SoundBuilder), new { assignmentId });
    }

    [HttpPost("{assignmentId:int}/sound-builder/answer")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Answer(
        int assignmentId,
        SoundBuilderPlayViewModel model,
        CancellationToken cancellationToken)
    {
        var learnerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (learnerId is null)
        {
            return Unauthorized();
        }

        // The attempt and item are server-owned state. Re-read them when a browser
        // drops the hidden state fields during a form post, while still validating
        // the answer fields and letting the game service enforce the current item.
        var current = await soundBuilderGameService.GetSessionAsync(learnerId, assignmentId, cancellationToken);
        var attemptId = model.AttemptId ?? current?.AttemptId;
        var itemId = string.IsNullOrWhiteSpace(model.ItemId)
            ? current?.CurrentQuestion?.ItemId
            : model.ItemId;
        if (string.IsNullOrWhiteSpace(model.ItemId))
        {
            ModelState.Remove(nameof(SoundBuilderPlayViewModel.ItemId));
        }

        if (attemptId is not > 0 || string.IsNullOrWhiteSpace(itemId) || !ModelState.IsValid)
        {
            return current is null ? NotFound() : View("SoundBuilder", ToViewModel(current));
        }

        try
        {
            var session = await soundBuilderGameService.SubmitAsync(
                learnerId,
                assignmentId,
                attemptId.Value,
                itemId,
                model.OrderedChunkIndexes ?? string.Empty,
                model.BlendedWord,
                model.SegmentedChunks,
                cancellationToken);
            return session is null ? NotFound() : RedirectToAction(nameof(SoundBuilder), new { assignmentId });
        }
        catch (SoundBuilderAnswerException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            var currentSession = await soundBuilderGameService.GetSessionAsync(learnerId, assignmentId, cancellationToken);
            if (currentSession is null)
            {
                return NotFound();
            }

            var viewModel = ToViewModel(currentSession);
            viewModel.ItemId = model.ItemId;
            viewModel.OrderedChunkIndexes = model.OrderedChunkIndexes ?? string.Empty;
            viewModel.BlendedWord = model.BlendedWord;
            viewModel.SegmentedChunks = model.SegmentedChunks;
            return View("SoundBuilder", viewModel);
        }
    }

    [HttpGet("{assignmentId:int}/fast-name-race")]
    public async Task<IActionResult> FastNameRace(int assignmentId, CancellationToken cancellationToken)
    {
        var learnerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (learnerId is null)
        {
            return Unauthorized();
        }

        var session = await fastNameRaceGameService.GetSessionAsync(learnerId, assignmentId, cancellationToken);
        if (session is null)
        {
            return NotFound();
        }

        var viewModel = ToViewModel(session);
        viewModel.AudioRecorder = await CreateAudioRecorderAsync(session.AssignmentId, session.AttemptId, cancellationToken);
        return View(viewModel);
    }

    [HttpPost("{assignmentId:int}/fast-name-race/start")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartFastNameRace(int assignmentId, CancellationToken cancellationToken)
    {
        var learnerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (learnerId is null)
        {
            return Unauthorized();
        }

        var session = await fastNameRaceGameService.StartAsync(learnerId, assignmentId, cancellationToken);
        if (session is null)
        {
            return NotFound();
        }

        if (!session.CanStart && session.AttemptId is null && !string.IsNullOrWhiteSpace(session.Message))
        {
            TempData["ErrorMessage"] = session.Message;
        }

        return RedirectToAction(nameof(FastNameRace), new { assignmentId });
    }

    [HttpPost("{assignmentId:int}/fast-name-race/finish")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> FinishFastNameRace(
        int assignmentId,
        FastNameRaceViewModel model,
        CancellationToken cancellationToken)
    {
        var learnerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (learnerId is null)
        {
            return Unauthorized();
        }

        if (model.AttemptId is not > 0)
        {
            return BadRequest();
        }

        try
        {
            var session = await fastNameRaceGameService.CompleteAsync(
                learnerId,
                assignmentId,
                model.AttemptId.Value,
                model.SubmittedItemsCompleted,
                model.ErrorIndexesJson,
                model.SubmittedActiveDurationMilliseconds,
                cancellationToken);
            return session is null ? NotFound() : RedirectToAction(nameof(FastNameRace), new { assignmentId });
        }
        catch (FastNameRaceSubmissionException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            var current = await fastNameRaceGameService.GetSessionAsync(learnerId, assignmentId, cancellationToken);
            return current is null ? NotFound() : View(nameof(FastNameRace), ToViewModel(current));
        }
    }

    [HttpGet("{assignmentId:int}/word-reader")]
    public async Task<IActionResult> WordReader(int assignmentId, CancellationToken cancellationToken)
    {
        var learnerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (learnerId is null)
        {
            return Unauthorized();
        }

        var session = await wordReaderGameService.GetSessionAsync(learnerId, assignmentId, cancellationToken);
        if (session is null)
        {
            return NotFound();
        }

        var viewModel = ToViewModel(session);
        viewModel.AudioRecorder = await CreateAudioRecorderAsync(session.AssignmentId, session.AttemptId, cancellationToken);
        return View(viewModel);
    }

    [HttpPost("{assignmentId:int}/word-reader/start")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartWordReader(int assignmentId, CancellationToken cancellationToken)
    {
        var learnerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (learnerId is null)
        {
            return Unauthorized();
        }

        var session = await wordReaderGameService.StartAsync(learnerId, assignmentId, cancellationToken);
        if (session is null)
        {
            return NotFound();
        }

        if (!session.CanStart && session.AttemptId is null && !string.IsNullOrWhiteSpace(session.Message))
        {
            TempData["ErrorMessage"] = session.Message;
        }

        return RedirectToAction(nameof(WordReader), new { assignmentId });
    }

    [HttpPost("{assignmentId:int}/word-reader/answer")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AnswerWordReader(
        int assignmentId,
        WordReaderPlayViewModel model,
        CancellationToken cancellationToken)
    {
        var learnerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (learnerId is null)
        {
            return Unauthorized();
        }

        if (model.AttemptId is not > 0 || string.IsNullOrWhiteSpace(model.ItemId) || !ModelState.IsValid)
        {
            var current = await wordReaderGameService.GetSessionAsync(learnerId, assignmentId, cancellationToken);
            if (current is null)
            {
                return NotFound();
            }

            ModelState.AddModelError(string.Empty, "Choose how this practice try went before continuing.");
            return View(nameof(WordReader), ToViewModel(current));
        }

        try
        {
            var session = await wordReaderGameService.RespondAsync(
                learnerId,
                assignmentId,
                model.AttemptId.Value,
                model.ItemId,
                model.SubmissionId,
                model.Outcome,
                cancellationToken);
            return session is null ? NotFound() : RedirectToAction(nameof(WordReader), new { assignmentId });
        }
        catch (WordReaderAnswerException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            var current = await wordReaderGameService.GetSessionAsync(learnerId, assignmentId, cancellationToken);
            return current is null ? NotFound() : View(nameof(WordReader), ToViewModel(current));
        }
    }

    [HttpGet("{assignmentId:int}/syllable-slicer")]
    public async Task<IActionResult> SyllableSlicer(int assignmentId, CancellationToken cancellationToken)
    {
        var learnerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (learnerId is null)
        {
            return Unauthorized();
        }

        var session = await syllableSlicerGameService.GetSessionAsync(learnerId, assignmentId, cancellationToken);
        if (session is null)
        {
            return NotFound();
        }

        var viewModel = ToViewModel(session);
        viewModel.AudioRecorder = await CreateAudioRecorderAsync(session.AssignmentId, session.AttemptId, cancellationToken);
        return View(viewModel);
    }

    [HttpPost("{assignmentId:int}/syllable-slicer/start")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartSyllableSlicer(int assignmentId, CancellationToken cancellationToken)
    {
        var learnerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (learnerId is null)
        {
            return Unauthorized();
        }

        var session = await syllableSlicerGameService.StartAsync(learnerId, assignmentId, cancellationToken);
        if (session is null)
        {
            return NotFound();
        }

        if (!session.CanStart && session.AttemptId is null && !string.IsNullOrWhiteSpace(session.Message))
        {
            TempData["ErrorMessage"] = session.Message;
        }

        return RedirectToAction(nameof(SyllableSlicer), new { assignmentId });
    }

    [HttpPost("{assignmentId:int}/syllable-slicer/answer")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AnswerSyllableSlicer(
        int assignmentId,
        SyllableSlicerPlayViewModel model,
        CancellationToken cancellationToken)
    {
        var learnerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (learnerId is null)
        {
            return Unauthorized();
        }

        if (model.AttemptId is not > 0 || string.IsNullOrWhiteSpace(model.OrderedSegmentIndexes) || !ModelState.IsValid)
        {
            var current = await syllableSlicerGameService.GetSessionAsync(learnerId, assignmentId, cancellationToken);
            if (current is null)
            {
                return NotFound();
            }

            ModelState.AddModelError(string.Empty, "Arrange every syllable chunk before submitting your answer.");
            var invalidViewModel = ToViewModel(current);
            invalidViewModel.OrderedSegmentIndexes = model.OrderedSegmentIndexes;
            return View(nameof(SyllableSlicer), invalidViewModel);
        }

        try
        {
            var session = await syllableSlicerGameService.SubmitAsync(
                learnerId,
                assignmentId,
                model.AttemptId.Value,
                model.ItemId,
                model.OrderedSegmentIndexes,
                model.SubmissionId,
                cancellationToken);
            return session is null ? NotFound() : RedirectToAction(nameof(SyllableSlicer), new { assignmentId });
        }
        catch (SyllableSlicerAnswerException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            var current = await syllableSlicerGameService.GetSessionAsync(learnerId, assignmentId, cancellationToken);
            return current is null ? NotFound() : View(nameof(SyllableSlicer), ToViewModel(current));
        }
    }

    [HttpGet("{assignmentId:int}/say-write-match")]
    public async Task<IActionResult> SayWriteMatch(int assignmentId, CancellationToken cancellationToken)
    {
        var learnerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (learnerId is null)
        {
            return Unauthorized();
        }

        var session = await sayWriteMatchGameService.GetSessionAsync(learnerId, assignmentId, cancellationToken);
        if (session is null)
        {
            return NotFound();
        }

        var viewModel = ToViewModel(session);
        viewModel.AudioRecorder = await CreateAudioRecorderAsync(session.AssignmentId, session.AttemptId, cancellationToken);
        return View(viewModel);
    }

    private async Task<LearnerAudioRecorderViewModel?> CreateAudioRecorderAsync(
        int assignmentId,
        int? attemptId,
        CancellationToken cancellationToken)
    {
        if (attemptId is not int activityAttemptId)
        {
            return null;
        }

        var recordingContext = await dbContext.ActivityAttempts
            .AsNoTracking()
            .Where(attempt => attempt.Id == activityAttemptId &&
                              attempt.ActivityAssignmentId == assignmentId &&
                              attempt.ActivityAssignment.LearnerId == User.FindFirstValue(ClaimTypes.NameIdentifier) &&
                              attempt.ActivityAssignment.Status != ActivityAssignmentStatus.Cancelled &&
                              attempt.ActivityAssignment.LearningActivity.IsActive &&
                              attempt.ActivityAssignment.LearningActivity.AllowsAudioRecording)
            .Select(attempt => new
            {
                AttemptCompletedAtUtc = attempt.CompletedAtUtc,
                AssignmentCompletedAtUtc = attempt.ActivityAssignment.CompletedAtUtc
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (recordingContext is null)
        {
            return null;
        }

        return new LearnerAudioRecorderViewModel(
            Url.Action("StartForActivity", "LearnerAudioRecordings", new { activityAttemptId })!,
            Url.Action("ForActivity", "LearnerAudioRecordings", new { activityAttemptId })!,
            Math.Clamp(audioRecordingOptions.Value.MaxDurationSeconds, 1, 600),
            recordingContext.AttemptCompletedAtUtc == null && recordingContext.AssignmentCompletedAtUtc == null);
    }

    [HttpPost("{assignmentId:int}/say-write-match/start")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartSayWriteMatch(int assignmentId, CancellationToken cancellationToken)
    {
        var learnerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (learnerId is null)
        {
            return Unauthorized();
        }

        var session = await sayWriteMatchGameService.StartAsync(learnerId, assignmentId, cancellationToken);
        if (session is null)
        {
            return NotFound();
        }

        if (!session.CanStart && session.AttemptId is null && !string.IsNullOrWhiteSpace(session.Message))
        {
            TempData["ErrorMessage"] = session.Message;
        }

        return RedirectToAction(nameof(SayWriteMatch), new { assignmentId });
    }

    [HttpPost("{assignmentId:int}/say-write-match/answer")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AnswerSayWriteMatch(
        int assignmentId,
        SayWriteMatchPlayViewModel model,
        CancellationToken cancellationToken)
    {
        var learnerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (learnerId is null)
        {
            return Unauthorized();
        }

        if (model.AttemptId is not > 0 || string.IsNullOrWhiteSpace(model.OrderedChunkIndexes) || !ModelState.IsValid)
        {
            var current = await sayWriteMatchGameService.GetSessionAsync(learnerId, assignmentId, cancellationToken);
            if (current is null)
            {
                return NotFound();
            }

            ModelState.AddModelError(string.Empty, "Arrange every letter or chunk before checking your answer.");
            var invalidViewModel = ToViewModel(current);
            invalidViewModel.OrderedChunkIndexes = model.OrderedChunkIndexes;
            invalidViewModel.ItemId = model.ItemId;
            return View(nameof(SayWriteMatch), invalidViewModel);
        }

        try
        {
            var session = await sayWriteMatchGameService.SubmitAsync(
                learnerId,
                assignmentId,
                model.AttemptId.Value,
                model.ItemId,
                model.OrderedChunkIndexes,
                model.SubmissionId,
                cancellationToken);
            return session is null ? NotFound() : RedirectToAction(nameof(SayWriteMatch), new { assignmentId });
        }
        catch (SayWriteMatchAnswerException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            var current = await sayWriteMatchGameService.GetSessionAsync(learnerId, assignmentId, cancellationToken);
            return current is null ? NotFound() : View(nameof(SayWriteMatch), ToViewModel(current));
        }
    }

    private static SoundBuilderPlayViewModel ToViewModel(SoundBuilderSession session) => new()
    {
        AssignmentId = session.AssignmentId,
        AttemptId = session.AttemptId,
        CanStart = session.CanStart,
        IsCompleted = session.IsCompleted,
        CompletedItems = session.CompletedItems,
        TotalItems = session.TotalItems,
        CorrectItems = session.CorrectItems,
        Accuracy = session.Accuracy,
        DurationMilliseconds = session.DurationMilliseconds,
        Feedback = session.Feedback,
        Message = session.Message,
        CurrentQuestion = session.CurrentQuestion,
        ItemId = session.CurrentQuestion?.ItemId ?? string.Empty,
        OrderedChunkIndexes = session.CurrentQuestion is null
            ? string.Empty
            : string.Join(',', session.CurrentQuestion.Chunks.Select(chunk => chunk.Index))
    };

    private static FastNameRaceViewModel ToViewModel(FastNameRaceSession session) => new()
    {
        AssignmentId = session.AssignmentId,
        AttemptId = session.AttemptId,
        CanStart = session.CanStart,
        IsCompleted = session.IsCompleted,
        DurationSeconds = session.DurationSeconds,
        Items = session.Items,
        ItemsCompleted = session.ItemsCompleted,
        ErrorCount = session.ErrorCount,
        ActiveDurationMilliseconds = session.ActiveDurationMilliseconds,
        ItemsPerMinute = session.ItemsPerMinute,
        Accuracy = session.Accuracy,
        Message = session.Message
    };

    private static WordReaderPlayViewModel ToViewModel(WordReaderSession session) => new()
    {
        AssignmentId = session.AssignmentId,
        AttemptId = session.AttemptId,
        SkillArea = session.SkillArea,
        CanStart = session.CanStart,
        IsCompleted = session.IsCompleted,
        ItemsCompleted = session.ItemsCompleted,
        TotalItems = session.TotalItems,
        ResponseCount = session.ResponseCount,
        RetryCount = session.RetryCount,
        SupportedPracticeCount = session.SupportedPracticeCount,
        DurationMilliseconds = session.DurationMilliseconds,
        CurrentItem = session.CurrentItem,
        ItemId = session.CurrentItem?.Id ?? string.Empty,
        Feedback = session.Feedback,
        Message = session.Message
    };

    private static SyllableSlicerPlayViewModel ToViewModel(SyllableSlicerSession session) => new()
    {
        AssignmentId = session.AssignmentId,
        AttemptId = session.AttemptId,
        CanStart = session.CanStart,
        IsCompleted = session.IsCompleted,
        ItemsCompleted = session.ItemsCompleted,
        TotalItems = session.TotalItems,
        AttemptsCount = session.AttemptsCount,
        CorrectCount = session.CorrectCount,
        Accuracy = session.Accuracy,
        DurationMilliseconds = session.DurationMilliseconds,
        CurrentQuestion = session.CurrentQuestion,
        OrderedSegmentIndexes = session.OrderedSegmentIndexes,
        ItemId = session.CurrentQuestion?.ItemId ?? string.Empty,
        Feedback = session.Feedback,
        Message = session.Message
    };

    private static SayWriteMatchPlayViewModel ToViewModel(SayWriteMatchSession session) => new()
    {
        AssignmentId = session.AssignmentId,
        AttemptId = session.AttemptId,
        CanStart = session.CanStart,
        IsCompleted = session.IsCompleted,
        ItemsCompleted = session.ItemsCompleted,
        TotalItems = session.TotalItems,
        ResponsesCount = session.ResponsesCount,
        CorrectCount = session.CorrectCount,
        Accuracy = session.Accuracy,
        DurationMilliseconds = session.DurationMilliseconds,
        CurrentQuestion = session.CurrentQuestion,
        ItemId = session.CurrentQuestion?.ItemId ?? string.Empty,
        OrderedChunkIndexes = session.OrderedChunkIndexes,
        Feedback = session.Feedback,
        Message = session.Message
    };
}
