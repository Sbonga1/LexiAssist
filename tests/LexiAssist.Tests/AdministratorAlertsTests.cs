using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Activities;
using LexiAssist.Web.Models.Identity;
using LexiAssist.Web.Models.Notifications;
using LexiAssist.Web.Models.Referrals;
using LexiAssist.Web.Models.Reports;
using LexiAssist.Web.Models.Screening;
using LexiAssist.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LexiAssist.Tests;

public sealed class AdministratorAlertsTests
{
    [Fact]
    public async Task Administrator_alert_service_detects_supported_attention_conditions_without_attendance_data()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        var (adminEmail, _) = await factory.CreateUserAsync(ApplicationRoles.Administrator);
        var (learnerEmail, _) = await factory.CreateUserAsync(ApplicationRoles.Learner);
        var learnerId = await GetUserIdAsync(factory, learnerEmail);
        var adminId = await GetUserIdAsync(factory, adminEmail);
        await SeedAttentionConditionsAsync(factory, learnerId, adminId);

        using var scope = factory.Services.CreateScope();
        var alertService = scope.ServiceProvider.GetRequiredService<IAdministratorAlertService>();
        var alerts = await alertService.GetForAdministratorAsync(adminId);
        var kinds = alerts.Select(alert => alert.Kind).ToArray();

        Assert.Contains(AdministratorAlertKind.ScreeningAttention, kinds);
        Assert.Contains(AdministratorAlertKind.ReferralAgeing, kinds);
        Assert.Contains(AdministratorAlertKind.TechnicalFailure, kinds);
        Assert.Contains(AdministratorAlertKind.ContentConfiguration, kinds);
        Assert.DoesNotContain(AdministratorAlertKind.Attendance, kinds);
        Assert.Contains(alerts, alert => alert.Title == "Pending referrals are ageing");
        Assert.DoesNotContain(learnerEmail, alerts.SelectMany(alert => new[] { alert.Title, alert.Message }));
    }

    private static async Task SeedAttentionConditionsAsync(
        LexiAssistWebApplicationFactory factory,
        string learnerId,
        string administratorId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var old = DateTime.UtcNow.AddDays(-4);

        db.ScreeningAttempts.AddRange(
            new ScreeningAttempt
            {
                UserId = learnerId,
                StartedAtUtc = old,
                Status = ScreeningAttemptStatus.InProgress
            },
            new ScreeningAttempt
            {
                UserId = learnerId,
                StartedAtUtc = old.AddHours(1),
                Status = ScreeningAttemptStatus.Abandoned
            });
        await db.SaveChangesAsync();

        var completedAttempt = new ScreeningAttempt
        {
            UserId = learnerId,
            StartedAtUtc = old,
            CompletedAtUtc = old.AddMinutes(10),
            Status = ScreeningAttemptStatus.Completed
        };
        db.ScreeningAttempts.Add(completedAttempt);
        await db.SaveChangesAsync();
        var report = new ScreeningReport
        {
            ScreeningAttemptId = completedAttempt.Id,
            UserId = learnerId,
            GeneratedAtUtc = old.AddMinutes(10),
            RiskTier = nameof(ScreeningSupportTier.Red),
            Summary = "Stored summary.",
            FlaggedAreasJson = "[]",
            ScoringVersion = "test-v1",
            IsReferralRecommended = true,
            SkillAreasJson = "[]",
            RecommendedNextAction = "Review support options."
        };
        db.ScreeningReports.Add(report);
        await db.SaveChangesAsync();
        db.Referrals.Add(new Referral
        {
            ScreeningReportId = report.Id,
            LearnerId = learnerId,
            Status = ReferralStatus.Pending,
            CreatedAtUtc = old
        });

        var activity = new LearningActivity
        {
            Name = "Technical alert activity",
            ActivityType = LearningActivityType.Reading,
            Description = "Configured activity.",
            Instructions = "Complete the activity.",
            IsActive = true
        };
        db.LearningActivities.Add(activity);
        await db.SaveChangesAsync();
        var assignments = Enumerable.Range(0, 3)
            .Select(index => new ActivityAssignment
            {
                LearnerId = learnerId,
                LearningActivityId = activity.Id,
                AssignedAtUtc = DateTime.UtcNow.AddHours(-2 - index),
                Status = ActivityAssignmentStatus.InProgress
            })
            .ToArray();
        db.ActivityAssignments.AddRange(assignments);
        await db.SaveChangesAsync();
        db.ActivityAttempts.AddRange(
            new ActivityAttempt { ActivityAssignmentId = assignments[0].Id, StartedAtUtc = DateTime.UtcNow.AddHours(-3), MetadataJson = "not json" },
            new ActivityAttempt { ActivityAssignmentId = assignments[1].Id, StartedAtUtc = DateTime.UtcNow.AddHours(-2), MetadataJson = "still not json" },
            new ActivityAttempt { ActivityAssignmentId = assignments[2].Id, StartedAtUtc = DateTime.UtcNow.AddHours(-1), MetadataJson = "also not json" });
        await db.SaveChangesAsync();
    }

    private static async Task<string> GetUserIdAsync(LexiAssistWebApplicationFactory factory, string email)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Users.Where(user => user.Email == email).Select(user => user.Id).SingleAsync();
    }
}
