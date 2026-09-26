using System.Security.Claims;
using LexiAssist.Web.Models.Audit;
using LexiAssist.Web.Models.Identity;
using LexiAssist.Web.Services;
using LexiAssist.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace LexiAssist.Web.Controllers;

[Authorize]
[Route("learner-audio-recordings")]
public sealed class LearnerAudioRecordingsController(
    ILearnerAudioRecordingService recordingService,
    IOptions<LearnerAudioRecordingOptions> options,
    IAuditService auditService) : Controller
{
    private const long UploadRequestLimit = 20 * 1024 * 1024 + 64 * 1024;

    [HttpPost("screening-test-attempts/{testAttemptId:int}/questions/{questionId:int}/sessions")]
    [Authorize(Roles = ApplicationRoles.Learner)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartForScreening(
        int testAttemptId,
        int questionId,
        [FromForm] bool consentAccepted,
        CancellationToken cancellationToken)
    {
        if (!consentAccepted)
        {
            return BadRequest(new { message = "Please read and accept the recording notice before starting." });
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
        {
            return Unauthorized();
        }

        var recording = await recordingService.StartForScreeningAsync(
            userId, testAttemptId, questionId, consentAccepted, cancellationToken);
        return recording is null
            ? NotFound()
            : Ok(new
            {
                recordingId = recording.Id,
                completeUrl = Url.Action(nameof(Complete), new { id = recording.Id }),
                maxDurationSeconds = Math.Clamp(options.Value.MaxDurationSeconds, 1, 600)
            });
    }

    [HttpPost("activity-attempts/{activityAttemptId:int}/sessions")]
    [Authorize(Roles = ApplicationRoles.Learner)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartForActivity(
        int activityAttemptId,
        [FromForm] bool consentAccepted,
        CancellationToken cancellationToken)
    {
        if (!consentAccepted)
        {
            return BadRequest(new { message = "Please read and accept the recording notice before starting." });
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
        {
            return Unauthorized();
        }

        var recording = await recordingService.StartForActivityAsync(
            userId, activityAttemptId, consentAccepted, cancellationToken);
        return recording is null
            ? NotFound()
            : Ok(new
            {
                recordingId = recording.Id,
                completeUrl = Url.Action(nameof(Complete), new { id = recording.Id }),
                maxDurationSeconds = Math.Clamp(options.Value.MaxDurationSeconds, 1, 600)
            });
    }

    [HttpPost("{id:int}/complete")]
    [Authorize(Roles = ApplicationRoles.Learner)]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(UploadRequestLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadRequestLimit)]
    public async Task<IActionResult> Complete(
        int id,
        [FromForm] LearnerAudioRecordingUploadInput input,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
        {
            return Unauthorized();
        }

        if (input.AudioFile is null)
        {
            return BadRequest(new { message = "No recording was received. Please try again." });
        }

        await using var audioContent = input.AudioFile.OpenReadStream();
        var result = await recordingService.CompleteAsync(
            userId,
            id,
            audioContent,
            input.AudioFile.Length,
            input.AudioFile.ContentType,
            input.DurationMilliseconds,
            cancellationToken);
        if (!result.Succeeded)
        {
            return BadRequest(new { message = result.Error });
        }

        Response.Headers.CacheControl = "no-store";
        return Ok(new
        {
            playbackUrl = Url.Action(nameof(Play), new { id }),
            durationMilliseconds = result.Recording!.DurationMilliseconds,
            uploadedAtUtc = result.Recording.UploadedAtUtc
        });
    }

    [HttpGet("screening-test-attempts/{testAttemptId:int}/questions/{questionId:int}")]
    [Authorize(Roles = ApplicationRoles.Learner)]
    public async Task<IActionResult> ForScreening(int testAttemptId, int questionId, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
        {
            return Unauthorized();
        }

        var recordings = await recordingService.GetScreeningRecordingsAsync(
            userId, testAttemptId, questionId, cancellationToken);
        return Ok(recordings.Select(recording => new
        {
            playbackUrl = Url.Action(nameof(Play), new { id = recording.Id }),
            durationMilliseconds = recording.DurationMilliseconds,
            uploadedAtUtc = recording.UploadedAtUtc
        }));
    }

    [HttpGet("activity-attempts/{activityAttemptId:int}")]
    [Authorize(Roles = ApplicationRoles.Learner)]
    public async Task<IActionResult> ForActivity(int activityAttemptId, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
        {
            return Unauthorized();
        }

        var recordings = await recordingService.GetActivityRecordingsAsync(userId, activityAttemptId, cancellationToken);
        return Ok(recordings.Select(recording => new
        {
            playbackUrl = Url.Action(nameof(Play), new { id = recording.Id }),
            durationMilliseconds = recording.DurationMilliseconds,
            uploadedAtUtc = recording.UploadedAtUtc
        }));
    }

    [HttpGet("{id:int}/play")]
    public async Task<IActionResult> Play(int id, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
        {
            return Unauthorized();
        }

        var authorizedFile = await recordingService.GetAuthorizedFileAsync(
            userId,
            User.IsInRole(ApplicationRoles.Administrator),
            User.IsInRole(ApplicationRoles.Psychologist),
            id,
            cancellationToken);
        if (authorizedFile is null)
        {
            return NotFound();
        }

        await auditService.RecordAsync(AuditActions.RecordingAccessed, AuditEntityTypes.Recording, id.ToString(), true,
            new { Access = "Playback" }, cancellationToken);

        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(authorizedFile.Content, authorizedFile.ContentType, enableRangeProcessing: true);
    }
}

public sealed class LearnerAudioRecordingUploadInput
{
    public IFormFile? AudioFile { get; set; }

    public long DurationMilliseconds { get; set; }
}
