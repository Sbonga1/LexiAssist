namespace LexiAssist.Web.Services;

public sealed class LearnerAudioRecordingOptions
{
    public const string SectionName = "LearnerAudioRecording";

    public string StorageDirectory { get; set; } = "App_Data/PrivateAudioRecordings";

    public int MaxDurationSeconds { get; set; } = 120;

    public int MaxFileSizeBytes { get; set; } = 4 * 1024 * 1024;

    public int UploadGraceSeconds { get; set; } = 15;

    public int PendingSessionLifetimeMinutes { get; set; } = 15;

    public int MaxRecordingsPerTarget { get; set; } = 3;
}
