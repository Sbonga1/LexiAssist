using System.ComponentModel.DataAnnotations;
using LexiAssist.Web.Models.Activities;
using LexiAssist.Web.Services.Interfaces;

namespace LexiAssist.Web.ViewModels.Activities;

public sealed class ActivityAssignmentCreateViewModel
{
    public int ReportId { get; init; }

    public string LearnerName { get; init; } = string.Empty;

    public DateTime ReportGeneratedAtUtc { get; init; }

    public IReadOnlyList<string> FlaggedAreas { get; init; } = [];

    public IReadOnlyList<ActivityRecommendation> Recommendations { get; init; } = [];

    public IReadOnlyList<ActivityOptionViewModel> ActiveActivities { get; init; } = [];

    public IReadOnlyList<ActivityAssignmentListItemViewModel> ExistingAssignments { get; init; } = [];

    [Range(1, int.MaxValue, ErrorMessage = "Choose a learning activity.")]
    public int LearningActivityId { get; set; }
}

public sealed record ActivityOptionViewModel(int Id, string Name, LearningActivityType ActivityType);

public sealed record ActivityAssignmentListItemViewModel(
    string ActivityName,
    DateTime AssignedAtUtc,
    ActivityAssignmentStatus Status);
