using System.ComponentModel.DataAnnotations;
using LexiAssist.Web.Models.Activities;
using LexiAssist.Web.Services.Interfaces;
using LexiAssist.Web.ViewModels.Recordings;

namespace LexiAssist.Web.ViewModels.Activities;

public sealed class LearnerActivitiesIndexViewModel
{
    public IReadOnlyList<LearnerActivityAssignmentViewModel> Assignments { get; init; } = [];
}

public sealed record LearnerActivityAssignmentViewModel(
    int AssignmentId,
    string ActivityName,
    string Description,
    ActivityAssignmentStatus Status,
    DateTime AssignedAtUtc,
    DateTime? CompletedAtUtc,
    double? Accuracy,
    long? DurationMilliseconds,
    bool CanPlaySoundBuilder,
    bool CanPlayFastNameRace,
    bool CanPlayWordReader,
    bool CanPlaySyllableSlicer,
    bool CanPlaySayWriteMatch);

public sealed class SoundBuilderPlayViewModel
{
    public LearnerAudioRecorderViewModel? AudioRecorder { get; set; }
    public int AssignmentId { get; init; }

    public int? AttemptId { get; init; }

    public bool CanStart { get; init; }

    public bool IsCompleted { get; init; }

    public int CompletedItems { get; init; }

    public int TotalItems { get; init; }

    public int CorrectItems { get; init; }

    public double? Accuracy { get; init; }

    public long? DurationMilliseconds { get; init; }

    public string? Feedback { get; init; }

    public string? Message { get; init; }

    public SoundBuilderQuestion? CurrentQuestion { get; init; }

    [Required]
    [StringLength(80)]
    public string ItemId { get; set; } = string.Empty;

    public string? OrderedChunkIndexes { get; set; }

    [StringLength(100)]
    public string? BlendedWord { get; set; }

    [StringLength(100)]
    public string? SegmentedChunks { get; set; }
}

public sealed class FastNameRaceViewModel
{
    public LearnerAudioRecorderViewModel? AudioRecorder { get; set; }
    public int AssignmentId { get; init; }

    public int? AttemptId { get; init; }

    public bool CanStart { get; init; }

    public bool IsCompleted { get; init; }

    public int DurationSeconds { get; init; }

    public IReadOnlyList<string> Items { get; init; } = [];

    public int ItemsCompleted { get; init; }

    public int ErrorCount { get; init; }

    public long? ActiveDurationMilliseconds { get; init; }

    public double? ItemsPerMinute { get; init; }

    public double? Accuracy { get; init; }

    public string? Message { get; init; }

    public int SubmittedItemsCompleted { get; set; }

    public string ErrorIndexesJson { get; set; } = "[]";

    public long SubmittedActiveDurationMilliseconds { get; set; }
}

public sealed class WordReaderPlayViewModel
{
    public LearnerAudioRecorderViewModel? AudioRecorder { get; set; }
    public int AssignmentId { get; init; }

    public int? AttemptId { get; init; }

    public string? SkillArea { get; init; }

    public bool CanStart { get; init; }

    public bool IsCompleted { get; init; }

    public int ItemsCompleted { get; init; }

    public int TotalItems { get; init; }

    public int ResponseCount { get; init; }

    public int RetryCount { get; init; }

    public int SupportedPracticeCount { get; init; }

    public long? DurationMilliseconds { get; init; }

    public WordReaderPracticeItem? CurrentItem { get; init; }

    public string ItemId { get; set; } = string.Empty;

    public string? Feedback { get; init; }

    public string? Message { get; init; }

    public Guid SubmissionId { get; init; } = Guid.NewGuid();

    public WordReaderPracticeOutcome Outcome { get; set; }
}

public sealed class SyllableSlicerPlayViewModel
{
    public LearnerAudioRecorderViewModel? AudioRecorder { get; set; }
    public int AssignmentId { get; init; }

    public int? AttemptId { get; init; }

    public bool CanStart { get; init; }

    public bool IsCompleted { get; init; }

    public int ItemsCompleted { get; init; }

    public int TotalItems { get; init; }

    public int AttemptsCount { get; init; }

    public int CorrectCount { get; init; }

    public double? Accuracy { get; init; }

    public long? DurationMilliseconds { get; init; }

    public SyllableSlicerQuestion? CurrentQuestion { get; init; }

    [StringLength(80)]
    public string OrderedSegmentIndexes { get; set; } = string.Empty;

    [Required]
    [StringLength(80)]
    public string ItemId { get; set; } = string.Empty;

    public string? Feedback { get; init; }

    public string? Message { get; init; }

    public Guid SubmissionId { get; init; } = Guid.NewGuid();
}

public sealed class SayWriteMatchPlayViewModel
{
    public LearnerAudioRecorderViewModel? AudioRecorder { get; set; }
    public int AssignmentId { get; init; }

    public int? AttemptId { get; init; }

    public bool CanStart { get; init; }

    public bool IsCompleted { get; init; }

    public int ItemsCompleted { get; init; }

    public int TotalItems { get; init; }

    public int ResponsesCount { get; init; }

    public int CorrectCount { get; init; }

    public double? Accuracy { get; init; }

    public long? DurationMilliseconds { get; init; }

    public SayWriteMatchQuestion? CurrentQuestion { get; init; }

    [Required]
    [StringLength(80)]
    public string ItemId { get; set; } = string.Empty;

    [StringLength(64)]
    public string OrderedChunkIndexes { get; set; } = string.Empty;

    public string? Feedback { get; init; }

    public string? Message { get; init; }

    public Guid SubmissionId { get; init; } = Guid.NewGuid();
}
