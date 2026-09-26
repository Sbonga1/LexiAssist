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

public sealed class SyllableSlicerGameTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Learner_can_complete_syllable_practice_and_responses_are_saved_once()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        var credentials = await factory.CreateUserAsync(ApplicationRoles.Learner);
        var (learnerId, assignmentId) = await CreateAssignmentAsync(factory, credentials.Email);
        var configuredItems = factory.Services.GetRequiredService<IOptions<SyllableSlicerOptions>>().Value.Items;
        using var client = CreateClient(factory);
        await SignInAsync(client, credentials.Email, credentials.Password);

        var activityList = await GetHtmlAsync(client, "/activities");
        Assert.Contains("Open Syllable Slicer", activityList);

        var path = $"/activities/{assignmentId}/syllable-slicer";
        var ready = await GetHtmlAsync(client, path);
        Assert.Contains("This round has 3 practice words", ready);
        Assert.Contains("Start practice", ready);
        var start = await client.PostAsync($"/activities/{assignmentId}/syllable-slicer/start", AntiForgeryForm(ready));
        Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);

        var game = await GetHtmlAsync(client, path);
        Assert.Contains("deconstruct", game);
        Assert.Contains("de", game);
        Assert.Contains("con", game);
        Assert.Contains("struct", game);
        Assert.Contains("Move earlier", game);
        Assert.Contains("Move later", game);
        Assert.Contains("syllable-slicer.js", game);

        var wrongAnswer = BuildAnswerForm(game, "2,1,0");
        var wrongResponse = await client.PostAsync(
            $"/activities/{assignmentId}/syllable-slicer/answer",
            new FormUrlEncodedContent(wrongAnswer));
        Assert.Equal(HttpStatusCode.Redirect, wrongResponse.StatusCode);
        var afterFirst = await GetHtmlAsync(client, path);
        Assert.Contains("Good effort", afterFirst);
        Assert.Contains(configuredItems[0].Word, afterFirst);

        // Reposting the same submission token must not add another saved response.
        var repeatedPost = await client.PostAsync(
            $"/activities/{assignmentId}/syllable-slicer/answer",
            new FormUrlEncodedContent(wrongAnswer));
        Assert.Equal(HttpStatusCode.Redirect, repeatedPost.StatusCode);
        game = await GetHtmlAsync(client, path);

        for (var itemIndex = 1; itemIndex < configuredItems.Count; itemIndex++)
        {
            Assert.Contains(configuredItems[itemIndex].Word, game);
            var correctOrder = string.Join(',', Enumerable.Range(0, configuredItems[itemIndex].Segments.Count));
            var response = await client.PostAsync(
                $"/activities/{assignmentId}/syllable-slicer/answer",
                new FormUrlEncodedContent(BuildAnswerForm(game, correctOrder)));
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            game = await GetHtmlAsync(client, path);
        }

        Assert.Contains("Practice round complete", game);
        Assert.Contains("2 of 3", game);
        Assert.Contains("Practice accuracy", game);

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
            Assert.NotNull(attempt.CompletedAtUtc);
            Assert.Equal(2d, attempt.Score);
            Assert.Equal(66.67d, attempt.Accuracy);
            Assert.NotNull(attempt.DurationMilliseconds);

            var state = JsonSerializer.Deserialize<SyllableSlicerAttemptState>(attempt.MetadataJson!, JsonOptions);
            Assert.NotNull(state);
            Assert.Equal("1.0", state.ContentVersion);
            Assert.Equal(3, state.CurrentItemIndex);
            Assert.Equal(3, state.Responses.Count);
            Assert.Equal("deconstruct", state.Responses[0].TargetWord);
            Assert.False(state.Responses[0].IsCorrect);
            Assert.True(state.Responses[0].ResponseTimeMilliseconds >= 0);
            Assert.False(Enumerable.Range(0, state.Items[0].Segments.Count).SequenceEqual(state.Items[0].PresentedSegmentIndexes));
            Assert.All(state.Responses.Skip(1), response => Assert.True(response.IsCorrect));
            Assert.All(state.Responses, response =>
            {
                Assert.Equal(3, response.OrderedSegmentIndexes.Count);
                Assert.NotEqual(Guid.Empty, response.SubmissionId);
            });
        }

        var otherCredentials = await factory.CreateUserAsync(ApplicationRoles.Learner);
        using var otherClient = CreateClient(factory);
        await SignInAsync(otherClient, otherCredentials.Email, otherCredentials.Password);
        var unauthorized = await otherClient.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, unauthorized.StatusCode);
    }

    [Fact]
    public async Task Invalid_segment_content_does_not_allow_an_attempt_to_start()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        var credentials = await factory.CreateUserAsync(ApplicationRoles.Learner);
        var (_, assignmentId) = await CreateAssignmentAsync(factory, credentials.Email);
        var configuredItems = factory.Services.GetRequiredService<IOptions<SyllableSlicerOptions>>().Value.Items;
        configuredItems[0].Segments = ["de", "con"];
        using var client = CreateClient(factory);
        await SignInAsync(client, credentials.Email, credentials.Password);

        var html = await GetHtmlAsync(client, $"/activities/{assignmentId}/syllable-slicer");
        Assert.Contains("No valid word and syllable content is configured", html);
        Assert.DoesNotContain("Start practice", html);
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
        var activity = await dbContext.LearningActivities.SingleAsync(item => item.Name == "Syllable Slicer");
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

    private static Dictionary<string, string> BuildAnswerForm(string html, string order) => new()
    {
        ["__RequestVerificationToken"] = ExtractInputValue(html, "__RequestVerificationToken"),
        ["AttemptId"] = ExtractInputValue(html, "AttemptId"),
        ["ItemId"] = ExtractInputValue(html, "ItemId"),
        ["SubmissionId"] = ExtractInputValue(html, "SubmissionId"),
        ["OrderedSegmentIndexes"] = order
    };

    private static HttpClient CreateClient(LexiAssistWebApplicationFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static async Task SignInAsync(HttpClient client, string email, string password)
    {
        var loginPage = await client.GetAsync("/Account/Login");
        var html = await loginPage.Content.ReadAsStringAsync();
        var result = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractInputValue(html, "__RequestVerificationToken"),
            ["Email"] = email,
            ["Password"] = password,
            ["RememberMe"] = "false"
        }));
        Assert.Equal(HttpStatusCode.Redirect, result.StatusCode);
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
