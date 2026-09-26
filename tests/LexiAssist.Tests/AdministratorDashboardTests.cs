using System.Net;
using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Activities;
using LexiAssist.Web.Models.Identity;
using LexiAssist.Web.Models.Notifications;
using LexiAssist.Web.Models.Referrals;
using LexiAssist.Web.Models.Reports;
using LexiAssist.Web.Models.Screening;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LexiAssist.Tests;

public sealed class AdministratorDashboardTests
{
    [Fact]
    public async Task Administrator_dashboard_displays_real_aggregates_and_recent_alerts()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        var (adminEmail, adminPassword) = await factory.CreateUserAsync(ApplicationRoles.Administrator);
        var (firstLearnerEmail, _) = await factory.CreateUserAsync(ApplicationRoles.Learner);
        var (secondLearnerEmail, _) = await factory.CreateUserAsync(ApplicationRoles.Learner);
        var (psychologistEmail, _) = await factory.CreateUserAsync(ApplicationRoles.Psychologist);
        var firstLearnerId = await GetUserIdAsync(factory, firstLearnerEmail);
        var secondLearnerId = await GetUserIdAsync(factory, secondLearnerEmail);
        var psychologistId = await GetUserIdAsync(factory, psychologistEmail);
        var adminId = await GetUserIdAsync(factory, adminEmail);
        var activeActivityCount = await GetActiveActivityCountAsync(factory);

        await SeedDashboardDataAsync(factory, adminId, firstLearnerId, secondLearnerId, psychologistId);

        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        await SignInAsync(client, adminEmail, adminPassword);
        var response = await client.GetAsync("/Dashboard/Administrator");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Administrator dashboard", html);
        Assert.Contains("Active learners", html);
        Assert.Contains("Completed screenings", html);
        Assert.Contains("Pending referrals", html);
        Assert.Contains("Assigned referrals", html);
        Assert.Contains("Active learning activities", html);
        Assert.Contains("Recent system alerts", html);
        Assert.Contains("Referral awaiting assignment", html);
        Assert.Contains($">{activeActivityCount + 1}<", html);
        Assert.Contains("href=\"/admin/users\"", html);
        Assert.Contains("href=\"/admin/screening-reports\"", html);
        Assert.Contains("href=\"/admin/referrals\"", html);
        Assert.Contains("href=\"/admin/screening-tests\"", html);
        Assert.DoesNotContain(firstLearnerEmail, html);
        Assert.DoesNotContain(secondLearnerEmail, html);
    }

    [Fact]
    public async Task Administrator_dashboard_is_protected_from_anonymous_and_non_administrator_users()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        using var anonymousClient = factory.CreateClient(new() { AllowAutoRedirect = false });
        var anonymousResult = await anonymousClient.GetAsync("/Dashboard/Administrator");
        Assert.Equal(HttpStatusCode.Redirect, anonymousResult.StatusCode);
        Assert.StartsWith("/Account/Login", GetLocationPath(anonymousResult));

        var (learnerEmail, learnerPassword) = await factory.CreateUserAsync(ApplicationRoles.Learner);
        using var learnerClient = factory.CreateClient(new() { AllowAutoRedirect = false });
        await SignInAsync(learnerClient, learnerEmail, learnerPassword);
        var learnerResult = await learnerClient.GetAsync("/Dashboard/Administrator");
        Assert.Equal(HttpStatusCode.Redirect, learnerResult.StatusCode);
        Assert.StartsWith("/Account/AccessDenied", GetLocationPath(learnerResult));
    }

    private static async Task SeedDashboardDataAsync(
        LexiAssistWebApplicationFactory factory,
        string administratorId,
        string firstLearnerId,
        string secondLearnerId,
        string psychologistId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var firstReport = await AddCompletedReportAsync(db, firstLearnerId, DateTime.UtcNow.AddDays(-3));
        var secondReport = await AddCompletedReportAsync(db, secondLearnerId, DateTime.UtcNow.AddDays(-2));
        db.ScreeningAttempts.Add(new ScreeningAttempt
        {
            UserId = firstLearnerId,
            StartedAtUtc = DateTime.UtcNow.AddHours(-2),
            Status = ScreeningAttemptStatus.InProgress
        });
        await db.SaveChangesAsync();

        db.Referrals.AddRange(
            new Referral
            {
                ScreeningReportId = firstReport.Id,
                LearnerId = firstLearnerId,
                Status = ReferralStatus.Pending,
                CreatedAtUtc = DateTime.UtcNow.AddDays(-1)
            },
            new Referral
            {
                ScreeningReportId = secondReport.Id,
                LearnerId = secondLearnerId,
                AssignedPsychologistId = psychologistId,
                Status = ReferralStatus.Assigned,
                CreatedAtUtc = DateTime.UtcNow.AddHours(-12)
            });
        db.LearningActivities.Add(new LearningActivity
        {
            Name = "Dashboard activity",
            ActivityType = LearningActivityType.Reading,
            Description = "A configured activity.",
            Instructions = "Complete the activity.",
            IsActive = true
        });
        db.AppNotifications.Add(new AppNotification
        {
            RecipientUserId = administratorId,
            Type = AppNotificationType.ReferralCreated,
            Title = "Referral awaiting assignment",
            Message = "A completed screening referral is waiting for assignment.",
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            RelatedUrl = "/admin/referrals"
        });
        await db.SaveChangesAsync();
    }

    private static async Task<ScreeningReport> AddCompletedReportAsync(
        ApplicationDbContext db,
        string learnerId,
        DateTime generatedAtUtc)
    {
        var attempt = new ScreeningAttempt
        {
            UserId = learnerId,
            StartedAtUtc = generatedAtUtc.AddMinutes(-10),
            CompletedAtUtc = generatedAtUtc.AddMinutes(-1),
            Status = ScreeningAttemptStatus.Completed,
            OverallScore = 70
        };
        db.ScreeningAttempts.Add(attempt);
        await db.SaveChangesAsync();

        var report = new ScreeningReport
        {
            ScreeningAttemptId = attempt.Id,
            UserId = learnerId,
            GeneratedAtUtc = generatedAtUtc,
            RiskTier = nameof(ScreeningSupportTier.Red),
            Summary = "Stored screening summary.",
            FlaggedAreasJson = "[]",
            ScoringVersion = "test-v1",
            IsReferralRecommended = true,
            SkillAreasJson = "[]",
            RecommendedNextAction = "Review support options."
        };
        db.ScreeningReports.Add(report);
        await db.SaveChangesAsync();
        return report;
    }

    private static async Task<int> GetActiveActivityCountAsync(LexiAssistWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.LearningActivities.CountAsync(activity => activity.IsActive);
    }

    private static async Task<string> GetUserIdAsync(LexiAssistWebApplicationFactory factory, string email)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Users.Where(user => user.Email == email).Select(user => user.Id).SingleAsync();
    }

    private static async Task SignInAsync(HttpClient client, string email, string password)
    {
        var loginPage = await client.GetAsync("/Account/Login");
        var html = await loginPage.Content.ReadAsStringAsync();
        var token = System.Text.RegularExpressions.Regex.Match(
            html,
            "<input[^>]*name=\\\"__RequestVerificationToken\\\"[^>]*value=\\\"([^\\\"]+)\\\"",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase).Groups[1].Value;
        Assert.False(string.IsNullOrWhiteSpace(token));
        var result = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token),
            ["Email"] = email,
            ["Password"] = password,
            ["RememberMe"] = "false"
        }));
        Assert.Equal(HttpStatusCode.Redirect, result.StatusCode);
    }

    private static string GetLocationPath(HttpResponseMessage response)
    {
        var location = response.Headers.Location;
        return location is null ? string.Empty : location.IsAbsoluteUri ? location.PathAndQuery : location.OriginalString;
    }
}
