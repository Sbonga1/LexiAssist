using LexiAssist.Web.Models.Screening;

namespace LexiAssist.Web.Services;

/// <summary>
/// Makes an inactive assessment question copy while keeping the source record untouched.
/// Shared by the question bank and the authoring workspace duplicate action.
/// </summary>
public static class ScreeningQuestionCopyUtility
{
    public static ScreeningQuestion CopyToTest(ScreeningQuestion source, int targetTestId, int sortOrder) => new()
    {
        ScreeningTestId = targetTestId,
        Prompt = source.Prompt,
        QuestionType = source.QuestionType,
        CorrectAnswer = source.CorrectAnswer,
        AcceptedAnswersJson = source.AcceptedAnswersJson,
        Weight = source.Weight,
        SortOrder = sortOrder,
        IsActive = false,
        AllowsAudioRecording = source.AllowsAudioRecording,
        AudioPath = source.AudioPath,
        ImagePath = source.ImagePath,
        MetadataJson = source.MetadataJson
    };
}
