using Microsoft.EntityFrameworkCore;
using LexiAssist.Web.Models.Activities;
using LexiAssist.Web.Models.Identity;
using LexiAssist.Web.Models.Recordings;
using LexiAssist.Web.Models.Screening;

namespace LexiAssist.Web.Data;

/// <summary>
/// Azure SQL uses its own migration set. The application model is shared with the
/// SQLite development context; provider-specific storage details are normalized here.
/// </summary>
public sealed class AzureSqlApplicationDbContext(DbContextOptions<AzureSqlApplicationDbContext> options)
    : ApplicationDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // SQLite stores TimeOnly in TEXT. Azure SQL has a native time type.
        builder.Entity<LearnerReminderPreference>()
            .Property(preference => preference.PreferredReminderTime)
            .HasColumnType("time");

        // SQL Server filtered-index predicates use bracketed identifiers.
        builder.Entity<ScreeningAttempt>()
            .HasIndex(attempt => attempt.UserId)
            .HasFilter($"[Status] = {(int)ScreeningAttemptStatus.InProgress}");
        builder.Entity<LearnerAudioRecording>()
            .HasIndex(recording => new { recording.ScreeningTestAttemptId, recording.ScreeningQuestionId })
            .HasFilter("[Status] = 0 AND [ScreeningTestAttemptId] IS NOT NULL");
        builder.Entity<LearnerAudioRecording>()
            .HasIndex(recording => recording.ActivityAttemptId)
            .HasFilter("[Status] = 0 AND [ActivityAttemptId] IS NOT NULL");
        builder.Entity<LearnerAudioRecording>().ToTable(table => table.HasCheckConstraint(
            "CK_LearnerAudioRecordings_OneTarget",
            "([ActivityAttemptId] IS NOT NULL AND [ScreeningTestAttemptId] IS NULL AND [ScreeningQuestionId] IS NULL) OR " +
            "([ActivityAttemptId] IS NULL AND [ScreeningTestAttemptId] IS NOT NULL AND [ScreeningQuestionId] IS NOT NULL)"));
        builder.Entity<ActivityAttempt>()
            .HasIndex(attempt => attempt.ActivityAssignmentId)
            .HasFilter("[CompletedAtUtc] IS NULL");
    }
}
