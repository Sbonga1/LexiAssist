using LexiAssist.Web.Models.Screening;

namespace LexiAssist.Web.Services.Interfaces;

public interface IAssessmentDraftGenerator
{
    bool IsConfigured { get; }

    Task<IReadOnlyList<AssessmentDraftQuestion>> GenerateAsync(
        AssessmentDraftGenerationInput input,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AssessmentDraftQuestion>> InterpretCsvAsync(
        CsvQuestionInterpretationInput input,
        CancellationToken cancellationToken = default);
}

public sealed record AssessmentDraftGenerationInput(
    string AssessmentName,
    string Objective,
    string Instructions,
    int QuestionCount,
    string DifficultyMix,
    IReadOnlyList<string> ExistingPrompts);

public sealed record CsvQuestionInterpretationInput(string CsvText);

public sealed record AssessmentDraftQuestion(
    string Prompt,
    ScreeningQuestionType QuestionType,
    string CorrectAnswer,
    IReadOnlyList<string> Choices,
    IReadOnlyList<string> AcceptedAnswers);
