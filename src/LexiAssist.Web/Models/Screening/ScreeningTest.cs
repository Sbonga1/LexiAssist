namespace LexiAssist.Web.Models.Screening;

public class ScreeningTest
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Instructions { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }

    public ICollection<ScreeningQuestion> Questions { get; set; } = new List<ScreeningQuestion>();

    public ICollection<ScreeningTestAttempt> TestAttempts { get; set; } = new List<ScreeningTestAttempt>();
}
