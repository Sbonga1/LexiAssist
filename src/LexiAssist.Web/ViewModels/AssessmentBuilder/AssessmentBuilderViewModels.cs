using System.ComponentModel.DataAnnotations;
using LexiAssist.Web.ModelBinding;
using LexiAssist.Web.Models.Screening;
using Microsoft.AspNetCore.Mvc;

namespace LexiAssist.Web.ViewModels.AssessmentBuilder;

public sealed class AssessmentBuilderViewModel
{
    public int ScreeningTestId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Instructions { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public bool OpenAiConfigured { get; init; }
    public int QuestionCount { get; init; }
    public int ActiveQuestionCount { get; init; }
    public IReadOnlyList<AssessmentBuilderQuestionItem> Questions { get; init; } = [];
    public int CurrentPage { get; init; }
    public int TotalPages { get; init; }
    public string? ImportMessage { get; init; }
    public IReadOnlyList<string> ImportErrors { get; init; } = [];
    public Guid? ResumableDraftId { get; init; }
    public int ResumableDraftCount { get; init; }
    public Guid GenerationRequestId { get; init; }
}

public sealed record AssessmentBuilderQuestionItem(
    int Id,
    string Prompt,
    string QuestionTypeName,
    int SortOrder,
    bool IsActive,
    bool AllowsAudioRecording,
    bool HasImage);

public sealed class AssessmentQuestionBankViewModel
{
    public int ScreeningTestId { get; init; }
    public string ScreeningTestName { get; init; } = string.Empty;
    public string Search { get; init; } = string.Empty;
    public IReadOnlyList<AssessmentQuestionBankItem> Questions { get; init; } = [];
}

public sealed record AssessmentQuestionBankItem(
    int Id,
    string AssessmentName,
    string Prompt,
    string QuestionTypeName,
    bool IsActive);

public sealed class AssessmentPreviewViewModel
{
    public int ScreeningTestId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Instructions { get; init; } = string.Empty;
    public string? ReturnUrl { get; init; }
    public string ReturnLabel { get; init; } = "Back to builder";
    public IReadOnlyList<AssessmentPreviewQuestion> Questions { get; init; } = [];
}

public sealed record AssessmentPreviewQuestion(
    string Prompt,
    string QuestionTypeName,
    IReadOnlyList<string> Choices,
    string? PassageText,
    bool AllowsAudioRecording,
    string? ImagePath = null,
    string? AudioPath = null,
    int? MaxAudioReplays = null,
    string? Transcript = null,
    ScreeningQuestionType QuestionType = ScreeningQuestionType.TextResponse,
    string? TargetText = null,
    string? ResponseType = null,
    IReadOnlyList<string>? ActivityItems = null,
    IReadOnlyDictionary<string, string>? ReferenceMapping = null,
    int? TimeLimitSeconds = null,
    int? MemoryDisplayDurationMilliseconds = null,
    int? ReferenceWordCount = null,
    int? TargetWordsPerMinute = null,
    int? PatternDifficultyLevel = null);

public sealed class AssessmentDraftReviewViewModel
{
    public Guid DraftId { get; init; }
    public int ScreeningTestId { get; init; }
    public string ScreeningTestName { get; init; } = string.Empty;
    public string ModelName { get; init; } = "gpt-6-astra";
    public bool IsCsvImport { get; init; }
    public bool OpenAiConfigured { get; init; }
    public int Version { get; init; }
    public int ActiveQuestionIndex { get; init; }
    public string DifficultyMix { get; init; } = "Balanced";
    public IReadOnlyList<int> SelectedIndexes { get; init; } = [];
    public IReadOnlyList<int> AddedIndexes { get; init; } = [];
    public bool IsDiscarded { get; init; }
    public string? SuccessMessage { get; init; }
    public string? ErrorMessage { get; init; }
    public IReadOnlyDictionary<int, IReadOnlyList<string>> QuestionErrors { get; init; } = new Dictionary<int, IReadOnlyList<string>>();
    public List<GeneratedAssessmentQuestionViewModel> Questions { get; init; } = [];
}

public sealed class GeneratedAssessmentQuestionViewModel
{
    public string Prompt { get; set; } = string.Empty;
    public ScreeningQuestionType QuestionType { get; set; }
    public string CorrectAnswer { get; set; } = string.Empty;
    public List<string> Choices { get; set; } = [];
    public string ChoicesText { get; set; } = string.Empty;
    public List<string> AcceptedAnswers { get; set; } = [];
    public string AcceptedAnswersText { get; set; } = string.Empty;
    public List<string> ChoiceIds { get; set; } = [];
    public string? CorrectChoiceId { get; set; }
    public string? PassageText { get; set; }
    public int? ReferenceWordCount { get; set; }
    public int? TargetWordsPerMinute { get; set; }
    public int? TimeLimitSeconds { get; set; }
    public string? TimedMatchingReferenceMappingText { get; set; }
    public string? TimedMatchingItemsText { get; set; }
    public int? PatternDifficultyLevel { get; set; }
    public string? TimedNamingItemsText { get; set; }
    public string? WorkingMemorySequenceText { get; set; }
    public int? MemoryDisplayDurationMilliseconds { get; set; }
    public string? AudioPath { get; set; }
    public string ListeningResponseType { get; set; } = "multiple-choice";
    public string? Transcript { get; set; }
    public int? MaxAudioReplays { get; set; }
    public string? ImagePath { get; set; }
    public bool AllowsAudioRecording { get; set; }
    [ModelBinder(BinderType = typeof(InvariantDoubleModelBinder))]
    public double Weight { get; set; } = 1.0;
}

public class AddGeneratedAssessmentQuestionsViewModel
{
    [Required]
    public int ScreeningTestId { get; set; }

    public List<int> SelectedIndexes { get; set; } = [];

    public List<GeneratedAssessmentQuestionViewModel> Questions { get; set; } = [];
}

public sealed class SaveAssessmentDraftViewModel : AddGeneratedAssessmentQuestionsViewModel
{
    public int Version { get; set; }
    public int ActiveQuestionIndex { get; set; }
    public string DifficultyMix { get; set; } = "Balanced";
}

public sealed class AssessmentDraftRequestViewModel
{
    public Guid RequestId { get; set; } = Guid.NewGuid();

    [Range(1, 20)]
    public int QuestionCount { get; set; } = 10;

    [StringLength(80)]
    public string DifficultyMix { get; set; } = "Balanced";
}
