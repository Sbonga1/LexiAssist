namespace LexiAssist.Web.ViewModels.Recordings;

public sealed record LearnerAudioRecorderViewModel(
    string StartUrl,
    string ExistingRecordingsUrl,
    int MaxDurationSeconds,
    bool CanRecord = true);
