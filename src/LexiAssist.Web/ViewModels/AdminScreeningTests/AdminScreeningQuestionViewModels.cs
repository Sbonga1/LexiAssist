using System.ComponentModel.DataAnnotations;
using LexiAssist.Web.Models.Screening;

namespace LexiAssist.Web.ViewModels.AdminScreeningTests;

public sealed class AdminScreeningQuestionFormViewModel
{
    public int Id { get; set; }

    [Required]
    [StringLength(2000)]
    [Display(Name = "Prompt")]
    public string Prompt { get; set; } = string.Empty;

    [EnumDataType(typeof(ScreeningQuestionType))]
    [Display(Name = "Question type")]
    public ScreeningQuestionType QuestionType { get; set; } = ScreeningQuestionType.MultipleChoice;

    [StringLength(2000)]
    [Display(Name = "Correct answer")]
    public string? CorrectAnswer { get; set; }

    [StringLength(8000)]
    [DataType(DataType.MultilineText)]
    [Display(Name = "Accepted answers (one per line)")]
    public string? AcceptedAnswersText { get; set; }

    [StringLength(8000)]
    [DataType(DataType.MultilineText)]
    [Display(Name = "Answer choices (one per line)")]
    public string? ChoiceOptionsText { get; set; }

    [StringLength(8000)]
    [DataType(DataType.MultilineText)]
    [Display(Name = "Passage text")]
    public string? PassageText { get; set; }

    [Range(1, 10000, ErrorMessage = "Enter a reference word count from 1 to 10,000.")]
    [Display(Name = "Reference word count (optional)")]
    public int? ReferenceWordCount { get; set; }

    [Range(1, 1000, ErrorMessage = "Enter a target pace from 1 to 1,000 words per minute.")]
    [Display(Name = "Optional target pace (words per minute)")]
    public int? TargetWordsPerMinute { get; set; }

    [Range(1, 600, ErrorMessage = "Enter a time limit from 1 to 600 seconds.")]
    [Display(Name = "Time limit (seconds)")]
    public int? TimeLimitSeconds { get; set; }

    [StringLength(8000)]
    [DataType(DataType.MultilineText)]
    [Display(Name = "Reference mapping (one per line: item = symbol)")]
    public string? TimedMatchingReferenceMappingText { get; set; }

    [StringLength(8000)]
    [DataType(DataType.MultilineText)]
    [Display(Name = "Matching items (one per line)")]
    public string? TimedMatchingItemsText { get; set; }

    [Range(1, 5, ErrorMessage = "Enter a difficulty level from 1 to 5, or leave it blank.")]
    [Display(Name = "Pattern difficulty level (optional)")]
    public int? PatternDifficultyLevel { get; set; }

    [StringLength(8000)]
    [DataType(DataType.MultilineText)]
    [Display(Name = "20 naming items (one per line)")]
    public string? TimedNamingItemsText { get; set; }

    [StringLength(8000)]
    [DataType(DataType.MultilineText)]
    [Display(Name = "Sequence items (one per line)")]
    public string? WorkingMemorySequenceText { get; set; }

    [Range(1000, 30000, ErrorMessage = "Enter a display duration from 1,000 to 30,000 milliseconds.")]
    [Display(Name = "Sequence display duration (milliseconds)")]
    public int? MemoryDisplayDurationMilliseconds { get; set; }

    [StringLength(255)]
    [Display(Name = "Audio file name")]
    public string? AudioPath { get; set; }

    [Range(0, 20, ErrorMessage = "Enter zero to 20 additional replays, or leave blank for unlimited replays.")]
    [Display(Name = "Maximum additional audio replays (optional)")]
    public int? MaxAudioReplays { get; set; }

    [StringLength(1024)]
    [Display(Name = "Image path or URL")]
    public string? ImagePath { get; set; }

    [Display(Name = "Permit learner audio recording for this question")]
    public bool AllowsAudioRecording { get; set; }
}

public sealed record AdminScreeningQuestionListItemViewModel(
    int Id,
    string Prompt,
    string QuestionTypeName,
    int SortOrder,
    bool IsActive);

public sealed record AdminScreeningQuestionsIndexViewModel(
    int ScreeningTestId,
    string ScreeningTestName,
    IReadOnlyList<AdminScreeningQuestionListItemViewModel> Questions);
