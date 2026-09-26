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

public sealed class SayWriteMatchGameTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Learner_can_reconstruct_words_when_audio_is_missing_and_answers_are_saved_once()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        var credentials = await factory.CreateUserAsync(ApplicationRoles.Learner);
        var (learnerId, assignmentId) = await CreateAssignmentAsync(factory, credentials.Email);
        var configuredItems = factory.Services.GetRequiredService<IOptions<SayWriteMatchOptions>>().Value.Items;
        configuredItems[0].AudioPath = "/media/say-write-match/missing-pronunciation.mp3";
        using var client = CreateClient(factory);
        await SignInAsync(client, credentials.Email, credentials.Password);

        var path = $"/activities/{assignmentId}/say-write-match";
        var ready = await GetHtmlAsync(client, path);
        Assert.Contains("Audio is optional", ready);
        Assert.Contains("Start practice", ready);
        var start = await client.PostAsync($"/activities/{assignmentId}/say-write-match/start", AntiForgeryForm(ready));
        Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);

        var game = await GetHtmlAsync(client, path);
        Assert.Contains("A large boat that travels on water", game);
        Assert.Contains("Audio playback is unavailable", game);
        Assert.Contains("Move earlier", game);
        Assert.Contains("Move later", game);
        Assert.Contains("say-write-match.js", game);
        Assert.DoesNotContain(">ship<", game);

        var wrongAnswer = BuildAnswerForm(game, "3,2,1,0");
        var wrongResponse = await client.PostAsync(
            $"/activities/{assignmentId}/say-write-match/answer",
            new FormUrlEncodedContent(wrongAnswer));
        Assert.Equal(HttpStatusCode.Redirect, wrongResponse.StatusCode);
        var nextItem = await GetHtmlAsync(client, path);
        Assert.Contains("Good try", nextItem);
        Assert.Contains("ship", nextItem);

        var duplicateResponse = await client.PostAsync(
            $"/activities/{assignmentId}/say-write-match/answer",
            new FormUrlEncodedContent(wrongAnswer));
        Assert.Equal(HttpStatusCode.Redirect, duplicateResponse.StatusCode);
        game = await GetHtmlAsync(client, path);

        for (var index = 1; index < configuredItems.Count; index++)
        {
            var item = configuredItems[index];
            Assert.Contains(item.Prompt, game);
            var correctOrder = string.Join(',', Enumerable.Range(0, item.Chunks.Count));
            var response = await client.PostAsync(
                $"/activities/{assignmentId}/say-write-match/answer",
                new FormUrlEncodedContent(BuildAnswerForm(game, correctOrder)));
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            game = await GetHtmlAsync(client, path);
        }

        Assert.Contains("Practice round complete", game);
        Assert.Contains("3 of 4", game);

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
            Assert.Equal(3d, attempt.Score);
            Assert.Equal(75d, attempt.Accuracy);
            Assert.True(attempt.DurationMilliseconds >= 0);

            var state = JsonSerializer.Deserialize<SayWriteMatchAttemptState>(attempt.MetadataJson!, JsonOptions);
            Assert.NotNull(state);
            Assert.Equal("1.0", state.ContentVersion);
            Assert.Equal(4, state.CurrentItemIndex);
            Assert.Equal(4, state.Responses.Count);
            Assert.Equal("ship", state.Responses[0].TargetWord);
            Assert.False(state.Responses[0].IsCorrect);
            Assert.True(state.Responses[0].ResponseTimeMilliseconds >= 0);
            Assert.True(state.Items[0].AudioUnavailable);
            Assert.All(state.Responses.Skip(1), response => Assert.True(response.IsCorrect));
            Assert.All(state.Responses, response =>
            {
                Assert.NotEqual(Guid.Empty, response.SubmissionId);
                Assert.NotEmpty(response.OrderedChunkIndexes);
            });
        }

        var otherCredentials = await factory.CreateUserAsync(ApplicationRoles.Learner);
        using var otherClient = CreateClient(factory);
        await SignInAsync(otherClient, otherCredentials.Email, otherCredentials.Password);
        var unauthorized = await otherClient.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, unauthorized.StatusCode);
    }

    [Fact]
    public async Task Invalid_word_chunks_prevent_the_practice_round_from_starting()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        var credentials = await factory.CreateUserAsync(ApplicationRoles.Learner);
        var (_, assignmentId) = await CreateAssignmentAsync(factory, credentials.Email);
        var configuredItems = factory.Services.GetRequiredService<IOptions<SayWriteMatchOptions>>().Value.Items;
        configuredItems[0].Chunks = ["sh", "i"];
        using var client = CreateClient(factory);
        await SignInAsync(client, credentials.Email, credentials.Password);

        var html = await GetHtmlAsync(client, $"/activities/{assignmentId}/say-write-match");
        Assert.Contains("No valid spelling content is configured", html);
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
        var activity = await dbContext.LearningActivities.SingleAsync(item => item.Name == "Say-Write-Match");
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
        ["OrderedChunkIndexes"] = order
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
