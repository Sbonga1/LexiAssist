using System.Net;
using System.Text.RegularExpressions;
using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Activities;
using LexiAssist.Web.Models.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LexiAssist.Tests;

public sealed class LearnerReminderPreferencesTests
{
    [Fact]
    public async Task Reminder_settings_are_saved_and_dashboard_reports_today_practice()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        var (email, password) = await factory.CreateUserAsync(ApplicationRoles.Learner);
        using var client = CreateClient(factory);
        await SignInAsync(client, email, password);

        var settings = await client.GetAsync("/Settings/Accessibility");
        var settingsHtml = await settings.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, settings.StatusCode);
        Assert.Contains("Practice reminders", settingsHtml);
        Assert.Contains("value=\"5\"", settingsHtml);

        var saved = await client.PostAsync(
            "/Settings/Accessibility",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiForgeryToken(settingsHtml),
                ["BackgroundTheme"] = "standard-light",
                ["FontScale"] = "1",
                ["RemindersEnabled"] = "true",
                ["PreferredReminderTime"] = "19:30",
                ["DailyPracticeTargetMinutes"] = "10"
            }));
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var learner = await userManager.FindByEmailAsync(email);
            Assert.NotNull(learner);

            var preference = await dbContext.LearnerReminderPreferences.SingleAsync(item => item.UserId == learner.Id);
            Assert.True(preference.RemindersEnabled);
            Assert.Equal(new TimeOnly(19, 30), preference.PreferredReminderTime);
            Assert.Equal(10, preference.DailyPracticeTargetMinutes);

            var activity = await dbContext.LearningActivities.SingleAsync(item => item.Name == "Sound Builder");
            var assignment = new ActivityAssignment
            {
                LearnerId = learner.Id,
                LearningActivityId = activity.Id,
                AssignedAtUtc = DateTime.UtcNow.Date,
                Status = ActivityAssignmentStatus.Completed,
                CompletedAtUtc = DateTime.UtcNow
            };
            dbContext.ActivityAssignments.Add(assignment);
            await dbContext.SaveChangesAsync();

            dbContext.ActivityAttempts.Add(new ActivityAttempt
            {
                ActivityAssignmentId = assignment.Id,
                StartedAtUtc = DateTime.UtcNow.Date.AddHours(8),
                CompletedAtUtc = DateTime.UtcNow.Date.AddHours(8).AddMinutes(3),
                DurationMilliseconds = 180_000
            });
            await dbContext.SaveChangesAsync();
        }

        var dashboard = await client.GetAsync("/Dashboard/Learner");
        var dashboardHtml = await dashboard.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, dashboard.StatusCode);
        Assert.Contains("Today's practice", dashboardHtml);
        Assert.Contains("3 of 10 minutes", dashboardHtml);
        Assert.Contains("19:30", dashboardHtml);
        Assert.DoesNotContain("Daily practice reminders are not set up yet", dashboardHtml);
    }

    [Fact]
    public async Task Invalid_daily_target_is_rejected_without_persisting_reminder_settings()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        var (email, password) = await factory.CreateUserAsync(ApplicationRoles.Learner);
        using var client = CreateClient(factory);
        await SignInAsync(client, email, password);

        var settings = await client.GetAsync("/Settings/Accessibility");
        var settingsHtml = await settings.Content.ReadAsStringAsync();
        var result = await client.PostAsync(
            "/Settings/Accessibility",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiForgeryToken(settingsHtml),
                ["BackgroundTheme"] = "standard-light",
                ["FontScale"] = "1",
                ["RemindersEnabled"] = "true",
                ["PreferredReminderTime"] = "07:30",
                ["DailyPracticeTargetMinutes"] = "0"
            }));

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Contains("Choose a daily practice target between 1 and 120 minutes", await result.Content.ReadAsStringAsync());

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var learner = await userManager.FindByEmailAsync(email);
        Assert.NotNull(learner);
        Assert.Empty(await dbContext.LearnerReminderPreferences
            .Where(item => item.UserId == learner.Id)
            .ToListAsync());
    }

    private static HttpClient CreateClient(LexiAssistWebApplicationFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static async Task SignInAsync(HttpClient client, string email, string password)
    {
        var loginPage = await client.GetAsync("/Account/Login");
        var loginHtml = await loginPage.Content.ReadAsStringAsync();
        var response = await client.PostAsync(
            "/Account/Login",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiForgeryToken(loginHtml),
                ["Email"] = email,
                ["Password"] = password,
                ["RememberMe"] = "true"
            }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Dashboard/Learner", response.Headers.Location?.OriginalString);
    }

    private static string ExtractAntiForgeryToken(string html)
    {
        var match = Regex.Match(
            html,
            "<input[^>]*name=\\\"__RequestVerificationToken\\\"[^>]*value=\\\"([^\\\"]+)\\\"",
            RegexOptions.IgnoreCase);
        Assert.True(match.Success, "The rendered form should include an antiforgery token.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
}
