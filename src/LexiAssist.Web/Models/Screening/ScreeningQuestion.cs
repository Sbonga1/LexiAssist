namespace LexiAssist.Web.Models.Screening;

public class ScreeningQuestion
{
    public int Id { get; set; }

    public int ScreeningTestId { get; set; }

    public string Prompt { get; set; } = string.Empty;

    public ScreeningQuestionType QuestionType { get; set; }

    public string? CorrectAnswer { get; set; }

    public string? AcceptedAnswersJson { get; set; }

    public double Weight { get; set; } = 1.0;

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public bool AllowsAudioRecording { get; set; }

    public string? AudioPath { get; set; }

    public string? ImagePath { get; set; }

    public string? MetadataJson { get; set; }

    public ScreeningTest ScreeningTest { get; set; } = null!;

    public ICollection<ScreeningResponse> Responses { get; set; } = new List<ScreeningResponse>();
}
