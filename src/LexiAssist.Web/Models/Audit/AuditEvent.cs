using LexiAssist.Web.Models.Identity;

namespace LexiAssist.Web.Models.Audit;

/// <summary>A security-sensitive record of an important action. Details contain safe metadata only.</summary>
public sealed class AuditEvent
{
    public long Id { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string? ActorUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public bool Succeeded { get; set; }
    public string? DetailsJson { get; set; }
    public ApplicationUser? Actor { get; set; }
}

public static class AuditActions
{
    public const string AdministrativeActionFailed = "Administrative.ActionFailed";
    public const string UserCreated = "User.Created";
    public const string UserProfileUpdated = "User.ProfileUpdated";
    public const string RoleChanged = "User.RoleChanged";
    public const string UserActivated = "User.Activated";
    public const string UserDeactivated = "User.Deactivated";
    public const string ScreeningTestCreated = "ScreeningTest.Created";
    public const string ScreeningTestUpdated = "ScreeningTest.Updated";
    public const string ScreeningTestActivated = "ScreeningTest.Activated";
    public const string ScreeningTestDeactivated = "ScreeningTest.Deactivated";
    public const string ScreeningTestReordered = "ScreeningTest.Reordered";
    public const string ScreeningQuestionCreated = "ScreeningQuestion.Created";
    public const string ScreeningQuestionUpdated = "ScreeningQuestion.Updated";
    public const string ScreeningQuestionActivated = "ScreeningQuestion.Activated";
    public const string ScreeningQuestionDeactivated = "ScreeningQuestion.Deactivated";
    public const string ScreeningQuestionReordered = "ScreeningQuestion.Reordered";
    public const string ReferralAssigned = "Referral.Assigned";
    public const string PsychologistReportAccessed = "Psychologist.ReportAccessed";
    public const string RecordingAccessed = "Recording.Accessed";
    public const string ProfessionalNoteChanged = "Professional.NoteChanged";
}

public static class AuditEntityTypes
{
    public const string User = "User";
    public const string ScreeningTest = "ScreeningTest";
    public const string ScreeningQuestion = "ScreeningQuestion";
    public const string Referral = "Referral";
    public const string ScreeningReport = "ScreeningReport";
    public const string Recording = "Recording";
    public const string ProfessionalNote = "ProfessionalNote";
}
