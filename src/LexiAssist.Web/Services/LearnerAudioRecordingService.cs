using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Activities;
using LexiAssist.Web.Models.Recordings;
using LexiAssist.Web.Models.Screening;
using LexiAssist.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LexiAssist.Web.Services;

public sealed class LearnerAudioRecordingService(
    ApplicationDbContext dbContext,
    IWebHostEnvironment environment,
    IOptions<LearnerAudioRecordingOptions> options,
    TimeProvider timeProvider) : ILearnerAudioRecordingService
{
    public const string ConsentNoticeVersion = "learner-audio-v1";
    private static readonly string[] AllowedContentTypes = ["audio/webm", "audio/ogg", "audio/mp4", "audio/wav"];
    private readonly LearnerAudioRecordingOptions _options = options.Value;

    public async Task<LearnerAudioRecording?> StartForScreeningAsync(
        string userId,
        int screeningTestAttemptId,
        int screeningQuestionId,
        bool consentAccepted,
        CancellationToken cancellationToken = default)
    {
        if (!consentAccepted || string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        var isPermitted = await dbContext.ScreeningTestAttempts.AnyAsync(testAttempt =>
            testAttempt.Id == screeningTestAttemptId &&
            testAttempt.CompletedAtUtc == null &&
            testAttempt.ScreeningAttempt.UserId == userId &&
            testAttempt.ScreeningAttempt.Status == ScreeningAttemptStatus.InProgress &&
            testAttempt.ScreeningAttempt.CompletedAtUtc == null &&
            testAttempt.ScreeningTest.IsActive,
            cancellationToken);

        // Question ownership is checked separately so the posted question cannot be switched to
        // another test or learner's attempt.
        isPermitted = isPermitted && await dbContext.ScreeningQuestions.AnyAsync(question =>
            question.Id == screeningQuestionId &&
            question.IsActive &&
            question.AllowsAudioRecording &&
            question.ScreeningTest.IsActive &&
            question.ScreeningTest.TestAttempts.Any(testAttempt =>
                testAttempt.Id == screeningTestAttemptId &&
                testAttempt.ScreeningAttempt.UserId == userId &&
                testAttempt.ScreeningAttempt.Status == ScreeningAttemptStatus.InProgress &&
                testAttempt.CompletedAtUtc == null),
            cancellationToken);

        if (!isPermitted)
        {
            return null;
        }

        var now = UtcNow();
        var matching = await dbContext.LearnerAudioRecordings
            .Where(recording => recording.UserId == userId &&
                                recording.ScreeningTestAttemptId == screeningTestAttemptId &&
                                recording.ScreeningQuestionId == screeningQuestionId)
            .ToListAsync(cancellationToken);
        var pending = await ExpireOrFindPendingAsync(matching, now, cancellationToken);
        if (pending is not null)
        {
            return pending;
        }

        if (matching.Count(recording => recording.Status == LearnerAudioRecordingStatus.Available) >= MaxRecordingsPerTarget)
        {
            return null;
        }

        return await CreatePendingAsync(userId, now, cancellationToken, recording =>
        {
            recording.ScreeningTestAttemptId = screeningTestAttemptId;
            recording.ScreeningQuestionId = screeningQuestionId;
        });
    }

    public async Task<LearnerAudioRecording?> StartForActivityAsync(
        string userId,
        int activityAttemptId,
        bool consentAccepted,
        CancellationToken cancellationToken = default)
    {
        if (!consentAccepted || string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        var isPermitted = await dbContext.ActivityAttempts.AnyAsync(attempt =>
            attempt.Id == activityAttemptId &&
            attempt.CompletedAtUtc == null &&
            attempt.ActivityAssignment.LearnerId == userId &&
            attempt.ActivityAssignment.CompletedAtUtc == null &&
            attempt.ActivityAssignment.Status != ActivityAssignmentStatus.Cancelled &&
            attempt.ActivityAssignment.LearningActivity.IsActive &&
            attempt.ActivityAssignment.LearningActivity.AllowsAudioRecording,
            cancellationToken);
        if (!isPermitted)
        {
            return null;
        }

        var now = UtcNow();
        var matching = await dbContext.LearnerAudioRecordings
            .Where(recording => recording.UserId == userId && recording.ActivityAttemptId == activityAttemptId)
            .ToListAsync(cancellationToken);
        var pending = await ExpireOrFindPendingAsync(matching, now, cancellationToken);
        if (pending is not null)
        {
            return pending;
        }

        if (matching.Count(recording => recording.Status == LearnerAudioRecordingStatus.Available) >= MaxRecordingsPerTarget)
        {
            return null;
        }

        return await CreatePendingAsync(userId, now, cancellationToken, recording =>
            recording.ActivityAttemptId = activityAttemptId);
    }

    public async Task<LearnerAudioRecordingUploadResult> CompleteAsync(
        string userId,
        int recordingId,
        Stream content,
        long fileSizeBytes,
        string? contentType,
        long durationMilliseconds,
        CancellationToken cancellationToken = default)
    {
        var recording = await dbContext.LearnerAudioRecordings.SingleOrDefaultAsync(item =>
            item.Id == recordingId && item.UserId == userId &&
            item.Status == LearnerAudioRecordingStatus.PendingUpload,
            cancellationToken);
        if (recording is null)
        {
            return Failed("This recording session is unavailable. Start a new recording and try again.");
        }

        if (!await IsStillPermittedAsync(recording, userId, cancellationToken))
        {
            return Failed("Recording is no longer enabled for this activity. The audio was not saved.");
        }

        var now = UtcNow();
        var maxDurationMilliseconds = (long)MaxDurationSeconds * 1000;
        var serverElapsed = Math.Max(0, (long)(now - recording.StartedAtUtc).TotalMilliseconds);
        if (durationMilliseconds < 250 || durationMilliseconds > maxDurationMilliseconds ||
            serverElapsed > maxDurationMilliseconds + (long)UploadGraceSeconds * 1000 ||
            durationMilliseconds > serverElapsed + 5000)
        {
            return Failed($"The recording must be no longer than {MaxDurationSeconds} seconds. Please record it again.");
        }

        if (fileSizeBytes is <= 0 || fileSizeBytes > MaxFileSizeBytes)
        {
            return Failed($"The recording must be between 1 byte and {MaxFileSizeBytes / (1024 * 1024)} MB.");
        }

        var normalizedType = NormalizeContentType(contentType);
        if (normalizedType is null)
        {
            return Failed("This audio format is not supported. Use WebM, Ogg, MP4, or WAV audio.");
        }

        await using var buffer = new MemoryStream((int)Math.Min(fileSizeBytes, MaxFileSizeBytes));
        var copyBuffer = new byte[81920];
        while (true)
        {
            var bytesRead = await content.ReadAsync(copyBuffer, cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            if (buffer.Length + bytesRead > MaxFileSizeBytes)
            {
                return Failed($"The recording must be between 1 byte and {MaxFileSizeBytes / (1024 * 1024)} MB.");
            }

            await buffer.WriteAsync(copyBuffer.AsMemory(0, bytesRead), cancellationToken);
        }

        if (buffer.Length != fileSizeBytes)
        {
            return Failed("The uploaded audio size did not match the recording information.");
        }

        var bytes = buffer.ToArray();
        if (!HasExpectedSignature(bytes, normalizedType))
        {
            return Failed("The uploaded file does not appear to be valid audio in the selected format.");
        }

        var folder = GetPrivateDirectory();
        Directory.CreateDirectory(folder);
        var storageFileName = $"{Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant()}{GetExtension(normalizedType)}";
        var fullPath = Path.GetFullPath(Path.Combine(folder, storageFileName));
        if (!IsWithin(fullPath, folder))
        {
            return Failed("The private recording location is not configured safely.");
        }

        try
        {
            await using (var output = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await output.WriteAsync(bytes, cancellationToken);
            }

            recording.StorageFileName = storageFileName;
            recording.ContentType = normalizedType;
            recording.FileSizeBytes = buffer.Length;
            recording.DurationMilliseconds = durationMilliseconds;
            recording.UploadedAtUtc = now;
            recording.Status = LearnerAudioRecordingStatus.Available;
            await dbContext.SaveChangesAsync(cancellationToken);
            return new LearnerAudioRecordingUploadResult(recording, null);
        }
        catch
        {
            TryDelete(fullPath);
            throw;
        }
    }

    public Task<IReadOnlyList<LearnerAudioRecording>> GetScreeningRecordingsAsync(
        string userId,
        int screeningTestAttemptId,
        int screeningQuestionId,
        CancellationToken cancellationToken = default) =>
        GetRecordingsAsync(userId, recording =>
            recording.ScreeningTestAttemptId == screeningTestAttemptId &&
            recording.ScreeningQuestionId == screeningQuestionId,
            cancellationToken);

    public Task<IReadOnlyList<LearnerAudioRecording>> GetActivityRecordingsAsync(
        string userId,
        int activityAttemptId,
        CancellationToken cancellationToken = default) =>
        GetRecordingsAsync(userId, recording => recording.ActivityAttemptId == activityAttemptId, cancellationToken);

    public async Task<LearnerAudioRecordingFile?> GetAuthorizedFileAsync(
        string userId,
        bool isAdministrator,
        bool isPsychologist,
        int recordingId,
        CancellationToken cancellationToken = default)
    {
        var recording = await dbContext.LearnerAudioRecordings
            .AsNoTracking()
            .Include(item => item.ScreeningTestAttempt)
                .ThenInclude(attempt => attempt!.ScreeningAttempt)
                    .ThenInclude(attempt => attempt!.Report)
                        .ThenInclude(report => report!.Referral)
            .Include(item => item.ActivityAttempt)
                .ThenInclude(attempt => attempt!.ActivityAssignment)
                    .ThenInclude(assignment => assignment.RelatedScreeningReport)
                        .ThenInclude(report => report!.Referral)
            .SingleOrDefaultAsync(item => item.Id == recordingId && item.Status == LearnerAudioRecordingStatus.Available,
                cancellationToken);
        if (recording is null)
        {
            return null;
        }

        var canRead = recording.UserId == userId || isAdministrator;
        if (!canRead && isPsychologist)
        {
            var screeningReferral = recording.ScreeningTestAttempt?.ScreeningAttempt.Report?.Referral;
            var activityReferral = recording.ActivityAttempt?.ActivityAssignment.RelatedScreeningReport?.Referral;
            canRead = (screeningReferral?.AssignedPsychologistId == userId &&
                       screeningReferral.LearnerId == recording.UserId) ||
                      (activityReferral?.AssignedPsychologistId == userId &&
                       activityReferral.LearnerId == recording.UserId);
        }

        if (!canRead || !TryGetSafeStoredPath(recording.StorageFileName, out var path) ||
            recording.ContentType is null || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            return new LearnerAudioRecordingFile(stream, recording.ContentType, stream.Length);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    private async Task<IReadOnlyList<LearnerAudioRecording>> GetRecordingsAsync(
        string userId,
        System.Linq.Expressions.Expression<Func<LearnerAudioRecording, bool>> target,
        CancellationToken cancellationToken)
    {
        var query = dbContext.LearnerAudioRecordings.AsNoTracking().Where(recording => recording.UserId == userId);
        return await query.Where(target)
            .Where(recording => recording.Status == LearnerAudioRecordingStatus.Available)
            .OrderBy(recording => recording.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    private Task<bool> IsStillPermittedAsync(
        LearnerAudioRecording recording,
        string userId,
        CancellationToken cancellationToken)
    {
        if (recording.ScreeningTestAttemptId is int testAttemptId && recording.ScreeningQuestionId is int questionId)
        {
            return dbContext.ScreeningQuestions.AnyAsync(question =>
                question.Id == questionId &&
                question.IsActive &&
                question.AllowsAudioRecording &&
                question.ScreeningTest.IsActive &&
                question.ScreeningTest.TestAttempts.Any(testAttempt =>
                    testAttempt.Id == testAttemptId &&
                    testAttempt.CompletedAtUtc == null &&
                    testAttempt.ScreeningAttempt.UserId == userId &&
                    testAttempt.ScreeningAttempt.Status == ScreeningAttemptStatus.InProgress &&
                    testAttempt.ScreeningAttempt.CompletedAtUtc == null),
                cancellationToken);
        }

        if (recording.ActivityAttemptId is int activityAttemptId)
        {
            return dbContext.ActivityAttempts.AnyAsync(attempt =>
                attempt.Id == activityAttemptId &&
                attempt.CompletedAtUtc == null &&
                attempt.ActivityAssignment.LearnerId == userId &&
                attempt.ActivityAssignment.CompletedAtUtc == null &&
                attempt.ActivityAssignment.Status != ActivityAssignmentStatus.Cancelled &&
                attempt.ActivityAssignment.LearningActivity.IsActive &&
                attempt.ActivityAssignment.LearningActivity.AllowsAudioRecording,
                cancellationToken);
        }

        return Task.FromResult(false);
    }

    private async Task<LearnerAudioRecording?> ExpireOrFindPendingAsync(
        List<LearnerAudioRecording> matching,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var lifetime = TimeSpan.FromMinutes(Math.Clamp(_options.PendingSessionLifetimeMinutes, 1, 60));
        foreach (var expired in matching.Where(recording =>
                     recording.Status == LearnerAudioRecordingStatus.PendingUpload && now - recording.StartedAtUtc > lifetime))
        {
            expired.Status = LearnerAudioRecordingStatus.Expired;
        }

        if (matching.Any(recording => recording.Status == LearnerAudioRecordingStatus.Expired))
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return matching.FirstOrDefault(recording => recording.Status == LearnerAudioRecordingStatus.PendingUpload);
    }

    private async Task<LearnerAudioRecording?> CreatePendingAsync(
        string userId,
        DateTime now,
        CancellationToken cancellationToken,
        Action<LearnerAudioRecording> setTarget)
    {
        var recording = new LearnerAudioRecording
        {
            UserId = userId,
            CreatedAtUtc = now,
            ConsentAcceptedAtUtc = now,
            ConsentNoticeVersion = ConsentNoticeVersion,
            StartedAtUtc = now,
            Status = LearnerAudioRecordingStatus.PendingUpload
        };
        setTarget(recording);
        dbContext.LearnerAudioRecordings.Add(recording);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return recording;
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(recording).State = EntityState.Detached;
            // Repeated starts are safe: return a currently pending session for the same target.
            return await dbContext.LearnerAudioRecordings.FirstOrDefaultAsync(item =>
                item.UserId == userId && item.Status == LearnerAudioRecordingStatus.PendingUpload &&
                item.ScreeningTestAttemptId == recording.ScreeningTestAttemptId &&
                item.ScreeningQuestionId == recording.ScreeningQuestionId &&
                item.ActivityAttemptId == recording.ActivityAttemptId,
                cancellationToken);
        }
    }

    private string GetPrivateDirectory()
    {
        var configuredPath = _options.StorageDirectory;
        var directory = Path.IsPathRooted(configuredPath)
            ? Path.GetFullPath(configuredPath)
            : Path.GetFullPath(Path.Combine(environment.ContentRootPath, configuredPath));
        var webRoot = Path.GetFullPath(environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"));
        if (IsWithin(directory, webRoot))
        {
            throw new InvalidOperationException("Learner audio must be stored outside the public web root.");
        }

        return directory;
    }

    private bool TryGetSafeStoredPath(string? storageFileName, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(storageFileName) ||
            !Regex.IsMatch(storageFileName, @"\A[0-9a-f]{32}\.(webm|ogg|m4a|wav)\z", RegexOptions.CultureInvariant))
        {
            return false;
        }

        var extension = Path.GetExtension(storageFileName).ToLowerInvariant();
        if (extension is not ".webm" and not ".ogg" and not ".m4a" and not ".wav")
        {
            return false;
        }

        var directory = GetPrivateDirectory();
        path = Path.GetFullPath(Path.Combine(directory, storageFileName));
        return IsWithin(path, directory);
    }

    private int MaxDurationSeconds => Math.Clamp(_options.MaxDurationSeconds, 1, 600);
    private int MaxFileSizeBytes => Math.Clamp(_options.MaxFileSizeBytes, 1024, 20 * 1024 * 1024);
    private int UploadGraceSeconds => Math.Clamp(_options.UploadGraceSeconds, 0, 60);
    private int MaxRecordingsPerTarget => Math.Clamp(_options.MaxRecordingsPerTarget, 1, 10);
    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;

    private static string? NormalizeContentType(string? contentType)
    {
        var normalized = contentType?.Split(';', 2)[0].Trim().ToLowerInvariant();
        return normalized is not null && AllowedContentTypes.Contains(normalized, StringComparer.Ordinal)
            ? normalized
            : null;
    }

    private static string GetExtension(string contentType) => contentType switch
    {
        "audio/webm" => ".webm",
        "audio/ogg" => ".ogg",
        "audio/mp4" => ".m4a",
        "audio/wav" => ".wav",
        _ => throw new InvalidOperationException("Unsupported recording type.")
    };

    private static bool HasExpectedSignature(ReadOnlySpan<byte> bytes, string contentType) => contentType switch
    {
        "audio/webm" => bytes.Length >= 4 && bytes[0] == 0x1A && bytes[1] == 0x45 && bytes[2] == 0xDF && bytes[3] == 0xA3,
        "audio/ogg" => bytes.Length >= 4 && Encoding.ASCII.GetString(bytes[..4]) == "OggS",
        "audio/mp4" => bytes.Length >= 12 && Encoding.ASCII.GetString(bytes.Slice(4, 4)) == "ftyp",
        "audio/wav" => bytes.Length >= 12 && Encoding.ASCII.GetString(bytes[..4]) == "RIFF" && Encoding.ASCII.GetString(bytes.Slice(8, 4)) == "WAVE",
        _ => false
    };

    private static bool IsWithin(string path, string directory)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(directory), Path.GetFullPath(path));
        return !Path.IsPathRooted(relative) && relative != ".." &&
               !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
               !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // The metadata remains unavailable and the private file can be removed operationally.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static LearnerAudioRecordingUploadResult Failed(string message) => new(null, message);
}
