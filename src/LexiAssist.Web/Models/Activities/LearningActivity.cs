namespace LexiAssist.Web.Models.Activities;

public class LearningActivity
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public LearningActivityType ActivityType { get; set; }

    public string Description { get; set; } = string.Empty;

    public string Instructions { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public bool AllowsAudioRecording { get; set; }

    public ICollection<ActivityAssignment> Assignments { get; set; } = new List<ActivityAssignment>();
}
