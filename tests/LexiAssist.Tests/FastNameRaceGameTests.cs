using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Activities;
using LexiAssist.Web.Models.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LexiAssist.Tests;

public sealed class FastNameRaceGameTests
{
    [Fact]
    public async Task Learner_can_complete_fast_name_race_and_save_neutral_metrics()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        var learnerCredentials = await factory.CreateUserAsync(ApplicationRoles.Learner);
        var otherLearnerCredentials = await factory.CreateUserAsync(ApplicationRoles.Learner);
        var (learnerId, assignmentId) = await CreateAssignmentAsync(factory, learnerCredentials.Email);
        using var client = CreateClient(factory);
        await SignInAsync(client, learnerCredentials.Email, learnerCredentials.Password);

        var list = await client.GetAsync("/activities");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Contains("Open Fast Name Race", await list.Content.ReadAsStringAsync());

        var activityPath = $"/activities/{assignmentId}/fast-name-race";
        var ready = await GetHtmlAsync(client, activityPath);
        Assert.Contains("Accurate naming matters", ready);
        Assert.Contains("60 seconds", ready);
        Assert.Contains("Start practice", ready);

        var start = await client.PostAsync(
            $"/activities/{assignmentId}/fast-name-race/start",
            AntiForgeryForm(ready));
        Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);

        var game = await GetHtmlAsync(client, activityPath);
        Assert.Contains("Pause", game);
        Assert.Contains("Resume", game);
        Assert.Contains("fast-name-race.js", game);
        Assert.Equal(20, Regex.Matches(game, "class=\"fast-name-race-item\"").Count);
        var attemptId = int.Parse(ExtractInputValue(game, "AttemptId"));
        var configuredItems = factory.Services.GetRequiredService<IOptions<FastNameRaceOptions>>().Value.Items;
        var savedForm = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractInputValue(game, "__RequestVerificationToken"),
            ["AttemptId"] = attemptId.ToString(),
            ["SubmittedItemsCompleted"] = "5",
            ["ErrorIndexesJson"] = "[0,2]",
            ["SubmittedActiveDurationMilliseconds"] = "1000"
        };

        var invalidForm = new Dictionary<string, string>(savedForm)
        {
            ["SubmittedItemsCompleted"] = "21"
        };
        var invalidFinish = await client.PostAsync(
            $"/activities/{assignmentId}/fast-name-race/finish",
            new FormUrlEncodedContent(invalidForm));
        Assert.Equal(HttpStatusCode.OK, invalidFinish.StatusCode);
        Assert.Contains("completed item count is outside", await invalidFinish.Content.ReadAsStringAsync());

        var finish = await client.PostAsync(
            $"/activities/{assignmentId}/fast-name-race/finish",
            new FormUrlEncodedContent(savedForm));
        Assert.Equal(HttpStatusCode.Redirect, finish.StatusCode);
        var resultHtml = await GetHtmlAsync(client, activityPath);
        Assert.Contains("Activity complete", resultHtml);
        Assert.Contains("5 of 20", resultHtml);
        Assert.Contains("2", resultHtml);
        Assert.Contains("300", resultHtml);
        Assert.Contains("60%", resultHtml);

        // Replaying the completion POST must not create a second attempt or alter the saved result.
        var repeatedFinish = await client.PostAsync(
            $"/activities/{assignmentId}/fast-name-race/finish",
            new FormUrlEncodedContent(savedForm));
        Assert.Equal(HttpStatusCode.Redirect, repeatedFinish.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var assignment = await dbContext.ActivityAssignments
                .Include(item => item.Attempts)
                .SingleAsync(item => item.Id == assignmentId);
            Assert.Equal(learnerId, assignment.LearnerId);
            Assert.Equal(ActivityAssignmentStatus.Completed, assignment.Status);
            Assert.NotNull(assignment.CompletedAtUtc);

            var attempt = Assert.Single(assignment.Attempts);
            Assert.Equal(attemptId, attempt.Id);
            Assert.NotNull(attempt.CompletedAtUtc);
            Assert.Equal(5d, attempt.Score);
            Assert.Equal(60d, attempt.Accuracy);
            Assert.Equal(1000L, attempt.DurationMilliseconds);

            var savedState = JsonSerializer.Deserialize<FastNameRaceAttemptState>(
                attempt.MetadataJson!,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.NotNull(savedState);
            Assert.Equal("1.0", savedState.ContentVersion);
            Assert.Equal(60, savedState.DurationSeconds);
            Assert.Equal(5, savedState.ItemsCompleted);
            Assert.Equal(new[] { 0, 2 }, savedState.ErrorIndexes);
            Assert.Equal(300d, savedState.ItemsPerMinute);
            Assert.Equal(60d, savedState.Accuracy);
            Assert.Equal(configuredItems.Order(StringComparer.Ordinal), savedState.Items.Order(StringComparer.Ordinal));
        }

        using var otherLearnerClient = CreateClient(factory);
        await SignInAsync(otherLearnerClient, otherLearnerCredentials.Email, otherLearnerCredentials.Password);
        var otherLearnerView = await otherLearnerClient.GetAsync(activityPath);
        Assert.Equal(HttpStatusCode.NotFound, otherLearnerView.StatusCode);
    }

    private static async Task<(string LearnerId, int AssignmentId)> CreateAssignmentAsync(
        LexiAssistWebApplicationFactory factory,
        string learnerEmail)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var learner = await userManager.FindByEmailAsync(learnerEmail);
        Assert.NotNull(learner);

        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var activity = await dbContext.LearningActivities.SingleAsync(item => item.Name == "Fast Name Race");
        var assignment = new ActivityAssignment
        {
            LearnerId = learner.Id,
            LearningActivityId = activity.Id,
            Status = ActivityAssignmentStatus.Assigned,
            AssignedAtUtc = DateTime.UtcNow
        };
        dbContext.ActivityAssignments.Add(assignment);
        await dbContext.SaveChangesAsync();
        return (learner.Id, assignment.Id);
    }

    private static HttpClient CreateClient(LexiAssistWebApplicationFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static async Task SignInAsync(HttpClient client, string email, string password)
    {
        var loginPage = await client.GetAsync("/Account/Login");
        var loginHtml = await loginPage.Content.ReadAsStringAsync();
        var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractInputValue(loginHtml, "__RequestVerificationToken"),
            ["Email"] = email,
            ["Password"] = password,
            ["RememberMe"] = "false"
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private static FormUrlEncodedContent AntiForgeryForm(string html) => new(new Dictionary<string, string>
    {
        ["__RequestVerificationToken"] = ExtractInputValue(html, "__RequestVerificationToken")
    });

    private static async Task<string> GetHtmlAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private static string ExtractInputValue(string html, string name)
    {
        var match = Regex.Match(
            html,
            $"<input[^>]*name=\\\"{Regex.Escape(name)}\\\"[^>]*value=\\\"([^\\\"]*)\\\"",
            RegexOptions.IgnoreCase);
        Assert.True(match.Success, $"The rendered page should include '{name}'.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
}
