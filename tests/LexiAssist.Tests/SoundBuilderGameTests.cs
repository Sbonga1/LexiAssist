using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Activities;
using LexiAssist.Web.Models.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LexiAssist.Tests;

public sealed class SoundBuilderGameTests
{
    [Fact]
    public async Task Learner_can_blend_and_segment_words_and_saved_round_progress_is_owned_and_scored()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        var learnerCredentials = await factory.CreateUserAsync(ApplicationRoles.Learner);
        var otherLearnerCredentials = await factory.CreateUserAsync(ApplicationRoles.Learner);
        var (learnerId, assignmentId) = await CreateSoundBuilderAssignmentAsync(factory, learnerCredentials.Email);

        using var learnerClient = CreateClient(factory);
        await SignInAsync(learnerClient, learnerCredentials.Email, learnerCredentials.Password);
        var activities = await learnerClient.GetAsync("/activities");
        Assert.Equal(HttpStatusCode.OK, activities.StatusCode);
        var activitiesHtml = await activities.Content.ReadAsStringAsync();
        Assert.Contains("Sound Builder", activitiesHtml);

        var gamePath = $"/activities/{assignmentId}/sound-builder";
        var readyResponse = await learnerClient.GetAsync(gamePath);
        Assert.Equal(HttpStatusCode.OK, readyResponse.StatusCode);
        var readyHtml = await readyResponse.Content.ReadAsStringAsync();
        Assert.Contains("blend or segment", readyHtml);
        Assert.Contains("Start practice", readyHtml);

        var start = await learnerClient.PostAsync(
            $"/activities/{assignmentId}/sound-builder/start",
            BuildAntiForgeryForm(readyHtml));
        Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);
        Assert.Equal(gamePath, start.Headers.Location?.OriginalString);

        var gameHtml = await GetHtmlAsync(learnerClient, gamePath);
        Assert.Contains("Move earlier", gameHtml);
        Assert.Contains("Move later", gameHtml);
        Assert.Contains("draggable=\"true\"", gameHtml);
        var activityContent = factory.Services.GetRequiredService<IOptions<SoundBuilderOptions>>().Value;
        int? savedAttemptId = null;
        string? firstSubmittedForm = null;
        var submitted = 0;

        while (gameHtml.Contains("Check and continue", StringComparison.Ordinal))
        {
            var itemId = ExtractInputValue(gameHtml, "ItemId");
            var attemptId = int.Parse(ExtractInputValue(gameHtml, "AttemptId"));
            savedAttemptId ??= attemptId;
            Assert.Equal(savedAttemptId, attemptId);

            var item = Assert.Single(activityContent.Items, content => content.Id == itemId);
            var submittedOrder = submitted == 0
                ? string.Join(',', Enumerable.Range(0, item.Chunks.Count).Reverse())
                : string.Join(',', Enumerable.Range(0, item.Chunks.Count));
            var typedWord = item.Mode == SoundBuilderMode.Blend
                ? submitted == 0 ? "incorrect" : item.Word
                : string.Empty;
            var segmentedChunks = item.Mode == SoundBuilderMode.Segment
                ? string.Join(' ', item.Chunks)
                : string.Empty;
            var form = new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractInputValue(gameHtml, "__RequestVerificationToken"),
                ["AttemptId"] = attemptId.ToString(),
                ["ItemId"] = itemId,
                ["OrderedChunkIndexes"] = submittedOrder,
                ["BlendedWord"] = typedWord,
                ["SegmentedChunks"] = segmentedChunks
            };
            firstSubmittedForm ??= JsonSerializer.Serialize(form);

            var answer = await learnerClient.PostAsync(
                $"/activities/{assignmentId}/sound-builder/answer",
                new FormUrlEncodedContent(form));
            Assert.Equal(HttpStatusCode.Redirect, answer.StatusCode);
            gameHtml = await GetHtmlAsync(learnerClient, gamePath);
            submitted++;

            if (submitted == 1)
            {
                Assert.Contains("Good effort", gameHtml);
            }
        }

        Assert.Equal(4, submitted);
        Assert.Contains("Practice round complete", gameHtml);
        Assert.Contains("75%", gameHtml);

        Assert.NotNull(firstSubmittedForm);
        var replayFields = JsonSerializer.Deserialize<Dictionary<string, string>>(firstSubmittedForm);
        Assert.NotNull(replayFields);
        var replay = await learnerClient.PostAsync(
            $"/activities/{assignmentId}/sound-builder/answer",
            new FormUrlEncodedContent(replayFields));
        Assert.Equal(HttpStatusCode.Redirect, replay.StatusCode);

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
            Assert.Equal(savedAttemptId, attempt.Id);
            Assert.NotNull(attempt.CompletedAtUtc);
            Assert.Equal(3d, attempt.Score);
            Assert.Equal(75d, attempt.Accuracy);
            Assert.True(attempt.DurationMilliseconds >= 0);

            var state = JsonSerializer.Deserialize<SoundBuilderAttemptState>(
                attempt.MetadataJson!,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.NotNull(state);
            Assert.Equal("1.0", state.ContentVersion);
            Assert.Equal(4, state.Items.Count);
            Assert.Equal(4, state.Responses.Count);
            Assert.Equal(3, state.Responses.Count(response => response.IsCorrect));
            Assert.All(state.Responses, response => Assert.Equal(response.OrderedChunkIndexes.Count, response.OrderedChunks.Count));
            Assert.Contains(state.Responses, response => response.SegmentedChunks.SequenceEqual(new[] { "sh", "i", "p" }));
        }

        var completedPracticeList = await learnerClient.GetAsync("/activities");
        Assert.Contains("Review Sound Builder", await completedPracticeList.Content.ReadAsStringAsync());

        using var otherLearnerClient = CreateClient(factory);
        await SignInAsync(otherLearnerClient, otherLearnerCredentials.Email, otherLearnerCredentials.Password);
        var otherLearnerGame = await otherLearnerClient.GetAsync(gamePath);
        Assert.Equal(HttpStatusCode.NotFound, otherLearnerGame.StatusCode);
    }

    [Fact]
    public async Task Segment_answer_can_progress_when_blend_only_order_field_is_empty()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        var learnerCredentials = await factory.CreateUserAsync(ApplicationRoles.Learner);
        var (_, assignmentId) = await CreateSoundBuilderAssignmentAsync(factory, learnerCredentials.Email);

        using var learnerClient = CreateClient(factory);
        await SignInAsync(learnerClient, learnerCredentials.Email, learnerCredentials.Password);
        var gamePath = $"/activities/{assignmentId}/sound-builder";
        var readyHtml = await GetHtmlAsync(learnerClient, gamePath);
        var start = await learnerClient.PostAsync(
            $"/activities/{assignmentId}/sound-builder/start",
            BuildAntiForgeryForm(readyHtml));
        Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);

        var blendHtml = await GetHtmlAsync(learnerClient, gamePath);
        var blendForm = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractInputValue(blendHtml, "__RequestVerificationToken"),
            ["AttemptId"] = ExtractInputValue(blendHtml, "AttemptId"),
            ["ItemId"] = ExtractInputValue(blendHtml, "ItemId"),
            ["OrderedChunkIndexes"] = "0,1,2",
            ["BlendedWord"] = "sun",
            ["SegmentedChunks"] = string.Empty
        };
        var blendAnswer = await learnerClient.PostAsync(
            $"/activities/{assignmentId}/sound-builder/answer",
            new FormUrlEncodedContent(blendForm));
        Assert.Equal(HttpStatusCode.Redirect, blendAnswer.StatusCode);

        var segmentHtml = await GetHtmlAsync(learnerClient, gamePath);
        var segmentForm = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractInputValue(segmentHtml, "__RequestVerificationToken"),
            ["AttemptId"] = ExtractInputValue(segmentHtml, "AttemptId"),
            ["ItemId"] = ExtractInputValue(segmentHtml, "ItemId"),
            ["SegmentedChunks"] = "s h i p"
        };
        var segmentAnswer = await learnerClient.PostAsync(
            $"/activities/{assignmentId}/sound-builder/answer",
            new FormUrlEncodedContent(segmentForm));
        Assert.Equal(HttpStatusCode.Redirect, segmentAnswer.StatusCode);

        var nextHtml = await GetHtmlAsync(learnerClient, gamePath);
        Assert.Contains("Good effort", nextHtml);
        Assert.Contains("Item 3 of 4", nextHtml);
        Assert.Contains("Progress", nextHtml, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<(string LearnerId, int AssignmentId)> CreateSoundBuilderAssignmentAsync(
        LexiAssistWebApplicationFactory factory,
        string learnerEmail)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var learner = await userManager.FindByEmailAsync(learnerEmail);
        Assert.NotNull(learner);

        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var activity = await dbContext.LearningActivities.SingleAsync(item =>
            item.Name == "Sound Builder" && item.ActivityType == LearningActivityType.PhonologicalAwareness);
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
        factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

    private static async Task SignInAsync(HttpClient client, string email, string password)
    {
        var loginPage = await client.GetAsync("/Account/Login");
        Assert.Equal(HttpStatusCode.OK, loginPage.StatusCode);
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

    private static FormUrlEncodedContent BuildAntiForgeryForm(string html) =>
        new(new Dictionary<string, string>
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
