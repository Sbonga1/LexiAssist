using LexiAssist.Web.Models.Identity;
using LexiAssist.Web.Models.Audit;
using LexiAssist.Web.Models.Activities;
using LexiAssist.Web.Models.Notifications;
using LexiAssist.Web.Models.Reports;
using LexiAssist.Web.Models.Referrals;
using LexiAssist.Web.Models.Screening;
using LexiAssist.Web.Models.Recordings;
using LexiAssist.Web.Models.Resources;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LexiAssist.Web.Data;

public class ApplicationDbContext(DbContextOptions options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<UserAccessibilityPreference> UserAccessibilityPreferences => Set<UserAccessibilityPreference>();

    public DbSet<LearnerInterest> LearnerInterests => Set<LearnerInterest>();

    public DbSet<LearnerReminderPreference> LearnerReminderPreferences => Set<LearnerReminderPreference>();

    public DbSet<PracticeStory> PracticeStories => Set<PracticeStory>();

    public DbSet<ScreeningTest> ScreeningTests => Set<ScreeningTest>();

    public DbSet<ScreeningQuestion> ScreeningQuestions => Set<ScreeningQuestion>();

    public DbSet<AssessmentDraft> AssessmentDrafts => Set<AssessmentDraft>();

    public DbSet<ScreeningAttempt> ScreeningAttempts => Set<ScreeningAttempt>();

    public DbSet<ScreeningTestAttempt> ScreeningTestAttempts => Set<ScreeningTestAttempt>();

    public DbSet<ScreeningResponse> ScreeningResponses => Set<ScreeningResponse>();

    public DbSet<ScreeningResponseErrorCategory> ScreeningResponseErrorCategories => Set<ScreeningResponseErrorCategory>();

    public DbSet<ScreeningReport> ScreeningReports => Set<ScreeningReport>();

    public DbSet<Referral> Referrals => Set<Referral>();

    public DbSet<AppNotification> AppNotifications => Set<AppNotification>();

    public DbSet<LearningActivity> LearningActivities => Set<LearningActivity>();

    public DbSet<ActivityAssignment> ActivityAssignments => Set<ActivityAssignment>();

    public DbSet<ActivityAttempt> ActivityAttempts => Set<ActivityAttempt>();

    public DbSet<LearnerAudioRecording> LearnerAudioRecordings => Set<LearnerAudioRecording>();

    public DbSet<EducationalResource> EducationalResources => Set<EducationalResource>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<AuditEvent>(entity =>
        {
            entity.Property(audit => audit.Action).HasMaxLength(120).IsRequired();
            entity.Property(audit => audit.EntityType).HasMaxLength(120).IsRequired();
            entity.Property(audit => audit.EntityId).HasMaxLength(200);
            entity.Property(audit => audit.ActorUserId).HasMaxLength(450);
            entity.Property(audit => audit.DetailsJson).HasMaxLength(4000);
            entity.HasIndex(audit => audit.OccurredAtUtc);
            entity.HasIndex(audit => new { audit.ActorUserId, audit.OccurredAtUtc });
            entity.HasIndex(audit => new { audit.Action, audit.EntityType, audit.OccurredAtUtc });
            entity.HasOne(audit => audit.Actor).WithMany().HasForeignKey(audit => audit.ActorUserId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<UserAccessibilityPreference>(entity =>
        {
            entity.HasKey(preference => preference.Id);
            entity.Property(preference => preference.BackgroundTheme)
                .HasMaxLength(32)
                .IsRequired();
            entity.HasIndex(preference => preference.UserId)
                .IsUnique();
            entity.HasOne<ApplicationUser>()
                .WithOne()
                .HasForeignKey<UserAccessibilityPreference>(preference => preference.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<LearnerInterest>(entity =>
        {
            entity.Property(selection => selection.InterestKey)
                .HasMaxLength(100)
                .IsRequired();
            entity.HasIndex(selection => new { selection.UserId, selection.InterestKey })
                .IsUnique();
            entity.HasIndex(selection => selection.UserId);
            entity.HasOne(selection => selection.User)
                .WithMany(user => user.LearnerInterests)
                .HasForeignKey(selection => selection.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<LearnerReminderPreference>(entity =>
        {
            entity.Property(preference => preference.UserId)
                .IsRequired();
            entity.Property(preference => preference.PreferredReminderTime)
                .IsRequired();
            entity.HasIndex(preference => preference.UserId)
                .IsUnique();
            entity.HasOne(preference => preference.User)
                .WithOne(user => user.LearnerReminderPreference)
                .HasForeignKey<LearnerReminderPreference>(preference => preference.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<PracticeStory>(entity =>
        {
            entity.Property(story => story.LearnerId)
                .IsRequired();
            entity.Property(story => story.Theme)
                .HasMaxLength(120)
                .IsRequired();
            entity.Property(story => story.TargetPhoneticPattern)
                .HasMaxLength(120)
                .IsRequired();
            entity.Property(story => story.TargetWordsJson)
                .HasMaxLength(2000)
                .IsRequired();
            entity.Property(story => story.StoryText)
                .HasMaxLength(2000)
                .IsRequired();
            entity.Property(story => story.GenerationMethod)
                .HasMaxLength(80)
                .IsRequired();
            entity.HasIndex(story => new { story.LearnerId, story.GeneratedAtUtc });
            entity.HasOne(story => story.Learner)
                .WithMany(user => user.PracticeStories)
                .HasForeignKey(story => story.LearnerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ScreeningTest>(entity =>
        {
            entity.Property(test => test.Name)
                .HasMaxLength(200)
                .IsRequired();
            entity.Property(test => test.Slug)
                .HasMaxLength(160)
                .IsRequired();
            entity.Property(test => test.Description)
                .HasMaxLength(2000)
                .IsRequired();
            entity.Property(test => test.Instructions)
                .HasMaxLength(4000)
                .IsRequired();
            entity.HasIndex(test => test.Slug)
                .IsUnique();

            entity.HasData(
                new ScreeningTest
                {
                    Id = 1,
                    Name = "Word Reading",
                    Slug = "word-reading",
                    Description = "Category for future educational screening and support content.",
                    Instructions = "No screening items are configured yet.",
                    IsActive = true,
                    SortOrder = 1
                },
                new ScreeningTest
                {
                    Id = 2,
                    Name = "Decoding / Non-word Reading",
                    Slug = "decoding-non-word-reading",
                    Description = "Category for future educational screening and support content.",
                    Instructions = "No screening items are configured yet.",
                    IsActive = true,
                    SortOrder = 2
                },
                new ScreeningTest
                {
                    Id = 3,
                    Name = "Phonological Awareness",
                    Slug = "phonological-awareness",
                    Description = "Category for future educational screening and support content.",
                    Instructions = "No screening items are configured yet.",
                    IsActive = true,
                    SortOrder = 3
                },
                new ScreeningTest
                {
                    Id = 4,
                    Name = "Rapid Naming",
                    Slug = "rapid-naming",
                    Description = "Category for future educational screening and support content.",
                    Instructions = "No screening items are configured yet.",
                    IsActive = true,
                    SortOrder = 4
                },
                new ScreeningTest
                {
                    Id = 5,
                    Name = "Spelling",
                    Slug = "spelling",
                    Description = "Category for future educational screening and support content.",
                    Instructions = "No screening items are configured yet.",
                    IsActive = true,
                    SortOrder = 5
                },
                new ScreeningTest
                {
                    Id = 6,
                    Name = "Reading Fluency",
                    Slug = "reading-fluency",
                    Description = "Category for future educational screening and support content.",
                    Instructions = "No screening items are configured yet.",
                    IsActive = true,
                    SortOrder = 6
                },
                new ScreeningTest
                {
                    Id = 7,
                    Name = "Reading Comprehension",
                    Slug = "reading-comprehension",
                    Description = "Category for future educational screening and support content.",
                    Instructions = "No screening items are configured yet.",
                    IsActive = true,
                    SortOrder = 7
                },
                new ScreeningTest
                {
                    Id = 8,
                    Name = "Oral Language and Listening Comprehension",
                    Slug = "oral-language-listening-comprehension",
                    Description = "Category for future educational screening and support content.",
                    Instructions = "No screening items are configured yet.",
                    IsActive = true,
                    SortOrder = 8
                },
                new ScreeningTest
                {
                    Id = 9,
                    Name = "Memory and Broader Thinking Skills",
                    Slug = "memory-broader-thinking-skills",
                    Description = "Optional category. Disabled pending stakeholder and professional confirmation.",
                    Instructions = "No screening items are configured yet.",
                    IsActive = false,
                    SortOrder = 9
                });
        });

        builder.Entity<AssessmentDraft>(entity =>
        {
            entity.Property(draft => draft.ContentJson).IsRequired();
            entity.Property(draft => draft.Version).IsConcurrencyToken();
            entity.HasIndex(draft => new { draft.ScreeningTestId, draft.IsDiscarded });
            entity.HasOne(draft => draft.ScreeningTest)
                .WithMany()
                .HasForeignKey(draft => draft.ScreeningTestId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ScreeningQuestion>(entity =>
        {
            entity.Property(question => question.Prompt)
                .HasMaxLength(2000)
                .IsRequired();
            entity.Property(question => question.CorrectAnswer)
                .HasMaxLength(2000);
            entity.Property(question => question.AudioPath)
                .HasMaxLength(1024);
            entity.Property(question => question.ImagePath)
                .HasMaxLength(1024);
            entity.HasIndex(question => new { question.ScreeningTestId, question.SortOrder });
            entity.HasOne(question => question.ScreeningTest)
                .WithMany(test => test.Questions)
                .HasForeignKey(question => question.ScreeningTestId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ScreeningAttempt>(entity =>
        {
            entity.Property(attempt => attempt.UserId)
                .HasMaxLength(450)
                .IsRequired();
            entity.Property(attempt => attempt.Status)
                .HasConversion<int>()
                .IsRequired();
            entity.Property(attempt => attempt.RiskTier)
                .HasMaxLength(64);
            entity.Property(attempt => attempt.RiskCalculationVersion)
                .HasMaxLength(64);
            entity.HasIndex(attempt => new { attempt.UserId, attempt.StartedAtUtc });
            entity.HasIndex(attempt => attempt.UserId)
                .IsUnique()
                .HasDatabaseName("IX_ScreeningAttempts_OneInProgressPerUser")
                .HasFilter($"\"Status\" = {(int)ScreeningAttemptStatus.InProgress}");
            entity.HasOne(attempt => attempt.User)
                .WithMany(user => user.ScreeningAttempts)
                .HasForeignKey(attempt => attempt.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(attempt => attempt.CurrentTest)
                .WithMany()
                .HasForeignKey(attempt => attempt.CurrentTestId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ScreeningReport>(entity =>
        {
            entity.Property(report => report.UserId)
                .HasMaxLength(450)
                .IsRequired();
            entity.Property(report => report.RiskTier)
                .HasMaxLength(64);
            entity.Property(report => report.Summary)
                .HasMaxLength(4000)
                .IsRequired();
            entity.Property(report => report.FlaggedAreasJson)
                .HasMaxLength(8000)
                .IsRequired();
            entity.Property(report => report.ScoringVersion)
                .HasMaxLength(64)
                .IsRequired();
            entity.Property(report => report.SkillAreasJson)
                .HasMaxLength(16000)
                .IsRequired();
            entity.Property(report => report.RecommendedNextAction)
                .HasMaxLength(2000)
                .IsRequired();
            entity.HasIndex(report => report.ScreeningAttemptId)
                .IsUnique();
            entity.HasOne(report => report.ScreeningAttempt)
                .WithOne(attempt => attempt.Report)
                .HasForeignKey<ScreeningReport>(report => report.ScreeningAttemptId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(report => report.User)
                .WithMany()
                .HasForeignKey(report => report.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Referral>(entity =>
        {
            entity.Property(referral => referral.LearnerId)
                .HasMaxLength(450)
                .IsRequired();
            entity.Property(referral => referral.AssignedPsychologistId)
                .HasMaxLength(450);
            entity.Property(referral => referral.Status)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();
            entity.Property(referral => referral.Notes)
                .HasMaxLength(4000);
            entity.HasIndex(referral => referral.ScreeningReportId)
                .IsUnique();
            entity.HasIndex(referral => new { referral.LearnerId, referral.CreatedAtUtc });
            entity.HasIndex(referral => new
            {
                referral.AssignedPsychologistId,
                referral.Status,
                referral.CreatedAtUtc
            });
            entity.HasOne(referral => referral.ScreeningReport)
                .WithOne(report => report.Referral)
                .HasForeignKey<Referral>(referral => referral.ScreeningReportId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(referral => referral.Learner)
                .WithMany(user => user.ReferralsAsLearner)
                .HasForeignKey(referral => referral.LearnerId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(referral => referral.AssignedPsychologist)
                .WithMany(user => user.AssignedReferrals)
                .HasForeignKey(referral => referral.AssignedPsychologistId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ScreeningTestAttempt>(entity =>
        {
            entity.HasIndex(attempt => new { attempt.ScreeningAttemptId, attempt.ScreeningTestId })
                .IsUnique();
            entity.HasIndex(attempt => new { attempt.ScreeningAttemptId, attempt.StartedAtUtc });
            entity.HasOne(attempt => attempt.ScreeningAttempt)
                .WithMany(screeningAttempt => screeningAttempt.TestAttempts)
                .HasForeignKey(attempt => attempt.ScreeningAttemptId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(attempt => attempt.ScreeningTest)
                .WithMany(test => test.TestAttempts)
                .HasForeignKey(attempt => attempt.ScreeningTestId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ScreeningResponse>(entity =>
        {
            entity.Property(response => response.TextAnswer)
                .HasMaxLength(8000);
            entity.Property(response => response.SelectedAnswer)
                .HasMaxLength(2000);
            entity.Property(response => response.AudioRecordingPath)
                .HasMaxLength(1024);
            entity.Property(response => response.ResponseDetailsJson)
                .HasMaxLength(16000);
            entity.Property(response => response.ProfessionalAudioNote)
                .HasMaxLength(500);
            entity.HasIndex(response => new { response.ScreeningTestAttemptId, response.ScreeningQuestionId })
                .IsUnique();
            entity.HasOne(response => response.ScreeningTestAttempt)
                .WithMany(testAttempt => testAttempt.Responses)
                .HasForeignKey(response => response.ScreeningTestAttemptId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(response => response.ScreeningQuestion)
                .WithMany(question => question.Responses)
                .HasForeignKey(response => response.ScreeningQuestionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ScreeningResponseErrorCategory>(entity =>
        {
            entity.HasKey(tag => new { tag.ScreeningResponseId, tag.Category });
            entity.Property(tag => tag.Category)
                .HasConversion<string>()
                .HasMaxLength(40);
            entity.HasOne(tag => tag.ScreeningResponse)
                .WithMany(response => response.ErrorCategories)
                .HasForeignKey(tag => tag.ScreeningResponseId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<AppNotification>(entity =>
        {
            entity.Property(notification => notification.Title)
                .HasMaxLength(160)
                .IsRequired();
            entity.Property(notification => notification.Message)
                .HasMaxLength(2000)
                .IsRequired();
            entity.Property(notification => notification.RelatedUrl)
                .HasMaxLength(2048);
            entity.HasIndex(notification => new
            {
                notification.RecipientUserId,
                notification.ReadAtUtc,
                notification.CreatedAtUtc
            });
            entity.HasOne(notification => notification.Recipient)
                .WithMany(user => user.Notifications)
                .HasForeignKey(notification => notification.RecipientUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<EducationalResource>(entity =>
        {
            entity.Property(resource => resource.Title)
                .HasMaxLength(200)
                .IsRequired();
            entity.Property(resource => resource.Summary)
                .HasMaxLength(1000)
                .IsRequired();
            entity.Property(resource => resource.Content)
                .HasMaxLength(12000)
                .IsRequired();
            entity.Property(resource => resource.ResourceType)
                .HasMaxLength(64)
                .IsRequired();
            entity.Property(resource => resource.ExternalUrl)
                .HasMaxLength(2048);
            entity.HasIndex(resource => new { resource.IsPublished, resource.UpdatedAtUtc });
        });

        builder.Entity<LearningActivity>(entity =>
        {
            entity.Property(activity => activity.Name)
                .HasMaxLength(200)
                .IsRequired();
            entity.Property(activity => activity.ActivityType)
                .HasConversion<string>()
                .HasMaxLength(40)
                .IsRequired();
            entity.Property(activity => activity.Description)
                .HasMaxLength(2000)
                .IsRequired();
            entity.Property(activity => activity.Instructions)
                .HasMaxLength(4000)
                .IsRequired();
            entity.HasIndex(activity => new { activity.IsActive, activity.ActivityType });
            entity.HasData(new LearningActivity
            {
                Id = 1,
                Name = "Sound Builder",
                ActivityType = LearningActivityType.PhonologicalAwareness,
                Description = "Practice blending and segmenting sound chunks in short words.",
                Instructions = "Arrange the chunks, then blend or segment the word at your own pace.",
                IsActive = true
            }, new LearningActivity
            {
                Id = 2,
                Name = "Fast Name Race",
                ActivityType = LearningActivityType.Fluency,
                Description = "Practise naming a changing grid of familiar items at a comfortable, steady pace.",
                Instructions = "Name each highlighted item, mark an item if you want to note a correction, and pause whenever you need.",
                IsActive = true
            }, new LearningActivity
            {
                Id = 3,
                Name = "Word Reader",
                ActivityType = LearningActivityType.Reading,
                Description = "Practise reading words selected for a screening support area.",
                Instructions = "Read each displayed word aloud and choose how the practice try went. Speech is not automatically evaluated; optional recording requires your consent.",
                IsActive = true
            }, new LearningActivity
            {
                Id = 4,
                Name = "Syllable Slicer",
                ActivityType = LearningActivityType.Reading,
                Description = "Practise arranging syllable chunks to build longer words.",
                Instructions = "Look at the word, arrange its syllable chunks, then check your answer at a comfortable pace.",
                IsActive = true
            }, new LearningActivity
            {
                Id = 5,
                Name = "Say-Write-Match",
                ActivityType = LearningActivityType.Spelling,
                Description = "Practise matching letter chunks to build a spelled word.",
                Instructions = "Listen to pronunciation audio when available, read the clue, and arrange the chunks to build each word.",
                IsActive = true
            });
        });

        builder.Entity<LearnerAudioRecording>(entity =>
        {
            entity.Property(recording => recording.UserId)
                .HasMaxLength(450)
                .IsRequired();
            entity.Property(recording => recording.ContentType)
                .HasMaxLength(64);
            entity.Property(recording => recording.StorageFileName)
                .HasMaxLength(80);
            entity.Property(recording => recording.ProfessionalNote)
                .HasMaxLength(500);
            entity.Property(recording => recording.ConsentNoticeVersion)
                .HasMaxLength(40)
                .IsRequired();
            entity.Property(recording => recording.Status)
                .HasConversion<int>()
                .IsRequired();
            entity.HasIndex(recording => new { recording.UserId, recording.CreatedAtUtc });
            entity.HasIndex(recording => recording.StorageFileName)
                .IsUnique();
            entity.HasIndex(recording => new { recording.ScreeningTestAttemptId, recording.ScreeningQuestionId })
                .IsUnique()
                .HasFilter("\"Status\" = 0 AND \"ScreeningTestAttemptId\" IS NOT NULL");
            entity.HasIndex(recording => recording.ActivityAttemptId)
                .IsUnique()
                .HasFilter("\"Status\" = 0 AND \"ActivityAttemptId\" IS NOT NULL");
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_LearnerAudioRecordings_OneTarget",
                "(\"ActivityAttemptId\" IS NOT NULL AND \"ScreeningTestAttemptId\" IS NULL AND \"ScreeningQuestionId\" IS NULL) OR " +
                "(\"ActivityAttemptId\" IS NULL AND \"ScreeningTestAttemptId\" IS NOT NULL AND \"ScreeningQuestionId\" IS NOT NULL)"));
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(recording => recording.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(recording => recording.ScreeningTestAttempt)
                .WithMany()
                .HasForeignKey(recording => recording.ScreeningTestAttemptId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(recording => recording.ScreeningQuestion)
                .WithMany()
                .HasForeignKey(recording => recording.ScreeningQuestionId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(recording => recording.ActivityAttempt)
                .WithMany()
                .HasForeignKey(recording => recording.ActivityAttemptId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ActivityAssignment>(entity =>
        {
            entity.Property(assignment => assignment.LearnerId)
                .HasMaxLength(450)
                .IsRequired();
            entity.Property(assignment => assignment.AssignedByUserId)
                .HasMaxLength(450);
            entity.Property(assignment => assignment.Status)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();
            entity.HasIndex(assignment => new
            {
                assignment.LearnerId,
                assignment.Status,
                assignment.AssignedAtUtc
            });
            entity.HasOne(assignment => assignment.Learner)
                .WithMany()
                .HasForeignKey(assignment => assignment.LearnerId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(assignment => assignment.AssignedByUser)
                .WithMany()
                .HasForeignKey(assignment => assignment.AssignedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(assignment => assignment.LearningActivity)
                .WithMany(activity => activity.Assignments)
                .HasForeignKey(assignment => assignment.LearningActivityId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(assignment => assignment.RelatedScreeningReport)
                .WithMany()
                .HasForeignKey(assignment => assignment.RelatedScreeningReportId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<ActivityAttempt>(entity =>
        {
            entity.Property(attempt => attempt.MetadataJson)
                .HasMaxLength(16000);
            entity.HasIndex(attempt => attempt.ActivityAssignmentId)
                .IsUnique()
                .HasDatabaseName("IX_ActivityAttempts_OneInProgressPerAssignment")
                .HasFilter("\"CompletedAtUtc\" IS NULL");
            entity.HasIndex(attempt => new { attempt.ActivityAssignmentId, attempt.StartedAtUtc });
            entity.HasOne(attempt => attempt.ActivityAssignment)
                .WithMany(assignment => assignment.Attempts)
                .HasForeignKey(attempt => attempt.ActivityAssignmentId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
