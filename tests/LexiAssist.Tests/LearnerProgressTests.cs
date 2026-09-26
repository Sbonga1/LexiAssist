using System.Net;
using System.Text.Json;
using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Activities;
using LexiAssist.Web.Models.Identity;
using LexiAssist.Web.Models.Reports;
using LexiAssist.Web.Models.Screening;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LexiAssist.Tests;

public sealed class LearnerProgressTests
{
    [Fact]
    public async Task Learner_progress_uses_only_the_current_learners_stored_data()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        var (learnerEmail, learnerPassword) = await factory.CreateUserAsync(ApplicationRoles.Learner);
        var (otherLearnerEmail, otherLearnerPassword) = await factory.CreateUserAsync(ApplicationRoles.Learner);
        var learnerId = await GetUserIdAsync(factory, learnerEmail);
        await SeedProgressDataAsync(factory, learnerId);

        using var learnerClient = factory.CreateClient(new() { AllowAutoRedirect = false });
        await SignInAsync(learnerClient, learnerEmail, learnerPassword);
        var progress = await learnerClient.GetAsync("/progress");
        Assert.Equal(HttpStatusCode.OK, progress.StatusCode);
        var html = await progress.Content.ReadAsStringAsync();
        Assert.Contains("Learner progress", html);
        Assert.Contains("Screening history", html);
        Assert.Contains("Reading practice", html);
        Assert.Contains("72%", html);
        Assert.Contains("80%", html);
        Assert.Contains("Word Reading", html);
        Assert.Contains("Activity frequency", html);
        Assert.Contains("do not diagnose", html, StringComparison.OrdinalIgnoreCase);

        using var otherLearnerClient = factory.CreateClient(new() { AllowAutoRedirect = false });
        await SignInAsync(otherLearnerClient, otherLearnerEmail, otherLearnerPassword);
        var otherProgress = await otherLearnerClient.GetAsync("/progress");
        Assert.Equal(HttpStatusCode.OK, otherProgress.StatusCode);
        var otherHtml = await otherProgress.Content.ReadAsStringAsync();
        Assert.Contains("Progress will appear here as you use LexiAssist", otherHtml);
        Assert.DoesNotContain("Reading practice", otherHtml);
        Assert.DoesNotContain("Word Reading", otherHtml);
    }

    [Fact]
    public async Task Progress_requires_a_learner_role_and_shows_an_empty_state_without_data()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        using var anonymousClient = factory.CreateClient(new() { AllowAutoRedirect = false });
        var anonymousResult = await anonymousClient.GetAsync("/progress");
        Assert.Equal(HttpStatusCode.Redirect, anonymousResult.StatusCode);
        Assert.StartsWith("/Account/Login", GetLocationPath(anonymousResult));

        var (adminEmail, adminPassword) = await factory.CreateUserAsync(ApplicationRoles.Administrator);
        using var adminClient = factory.CreateClient(new() { AllowAutoRedirect = false });
        await SignInAsync(adminClient, adminEmail, adminPassword);
        var adminResult = await adminClient.GetAsync("/progress");
        Assert.Equal(HttpStatusCode.Redirect, adminResult.StatusCode);
        Assert.StartsWith("/Account/AccessDenied", GetLocationPath(adminResult));

        var (learnerEmail, learnerPassword) = await factory.CreateUserAsync(ApplicationRoles.Learner);
        using var learnerClient = factory.CreateClient(new() { AllowAutoRedirect = false });
        await SignInAsync(learnerClient, learnerEmail, learnerPassword);
        var learnerResult = await learnerClient.GetAsync("/Dashboard/Progress");
        Assert.Equal(HttpStatusCode.OK, learnerResult.StatusCode);
        Assert.Contains("No completed screening sessions are available yet", await learnerResult.Content.ReadAsStringAsync());
    }

    private static async Task SeedProgressDataAsync(LexiAssistWebApplicationFactory factory, string learnerId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var screeningTest = await db.ScreeningTests.OrderBy(test => test.Id).FirstAsync();
        var now = DateTime.UtcNow;
        var screeningAttempt = new ScreeningAttempt
        {
            UserId = learnerId,
            StartedAtUtc = now.AddDays(-4),
            CompletedAtUtc = now.AddDays(-4).AddMinutes(12),
            Status = ScreeningAttemptStatus.Completed,
            OverallScore = 72
        };
        db.ScreeningAttempts.Add(screeningAttempt);
        await db.SaveChangesAsync();

        db.ScreeningTestAttempts.Add(new ScreeningTestAttempt
        {
            ScreeningAttemptId = screeningAttempt.Id,
            ScreeningTestId = screeningTest.Id,
            StartedAtUtc = now.AddDays(-4),
            CompletedAtUtc = now.AddDays(-4).AddMinutes(5),
            RawScore = 8,
            PercentageScore = 72
        });
        db.ScreeningReports.Add(new ScreeningReport
        {
            ScreeningAttemptId = screeningAttempt.Id,
            UserId = learnerId,
            GeneratedAtUtc = now.AddDays(-4).AddMinutes(12),
            RiskTier = nameof(ScreeningSupportTier.Amber),
            Summary = "Stored screening summary.",
            FlaggedAreasJson = JsonSerializer.Serialize(new[] { new ScreeningReportFlaggedAreaSnapshot("Word Reading", 72m) }),
            SkillAreasJson = JsonSerializer.Serialize(new[]
            {
                new ScreeningReportSkillAreaSnapshot("Word Reading", 72m, false, true, "Stored area summary.")
            }),
            ScoringVersion = "test-v1",
            IsReferralRecommended = false,
            RecommendedNextAction = "Review available support."
        });

        var activity = new LearningActivity
        {
            Name = "Reading practice",
            ActivityType = LearningActivityType.Reading,
            Description = "A test practice activity.",
            Instructions = "Complete the practice activity.",
            IsActive = true
        };
        db.LearningActivities.Add(activity);
        await db.SaveChangesAsync();

        var assignment = new ActivityAssignment
        {
            LearnerId = learnerId,
            LearningActivityId = activity.Id,
            AssignedAtUtc = now.AddDays(-2),
            Status = ActivityAssignmentStatus.InProgress
        };
        db.ActivityAssignments.Add(assignment);
        await db.SaveChangesAsync();

        db.ActivityAttempts.AddRange(
            new ActivityAttempt
            {
                ActivityAssignmentId = assignment.Id,
                StartedAtUtc = now.AddDays(-2),
                CompletedAtUtc = now.AddDays(-2).AddMinutes(3),
                Accuracy = 80,
                DurationMilliseconds = 180_000
            },
            new ActivityAttempt
            {
                ActivityAssignmentId = assignment.Id,
                StartedAtUtc = now.AddDays(-1),
                CompletedAtUtc = now.AddDays(-1).AddMinutes(2),
                Accuracy = 80,
                DurationMilliseconds = 120_000
            });
        await db.SaveChangesAsync();
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
