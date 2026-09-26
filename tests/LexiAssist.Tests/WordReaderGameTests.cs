using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Activities;
using LexiAssist.Web.Models.Identity;
using LexiAssist.Web.Models.Reports;
using LexiAssist.Web.Models.Screening;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LexiAssist.Tests;

public sealed class WordReaderGameTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Learner_can_retry_and_complete_the_assigned_word_set_with_saved_self_reported_progress()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        var learnerCredentials = await factory.CreateUserAsync(ApplicationRoles.Learner);
        var otherLearnerCredentials = await factory.CreateUserAsync(ApplicationRoles.Learner);
        var (learnerId, assignmentId) = await CreateAssignmentAsync(factory, learnerCredentials.Email, "Word Reading");
        var configuredItems = factory.Services.GetRequiredService<IOptions<WordReaderOptions>>().Value
            .SkillSets.Single(set => set.SkillArea == "Word Reading").Items;
        configuredItems[0].AudioPath = "/media/word-reader/missing-model.mp3";
        using var learnerClient = CreateClient(factory);
        await SignInAsync(learnerClient, learnerCredentials.Email, learnerCredentials.Password);

        var activityList = await learnerClient.GetAsync("/activities");
        Assert.Contains("Open Word Reader", await activityList.Content.ReadAsStringAsync());
        var activityPath = $"/activities/{assignmentId}/word-reader";
        var ready = await GetHtmlAsync(learnerClient, activityPath);
        Assert.Contains("Practice focus: Word Reading", ready);
        Assert.Contains("does not listen to or judge pronunciation", ready);

        var start = await learnerClient.PostAsync(
            $"/activities/{assignmentId}/word-reader/start",
            AntiForgeryForm(ready));
        Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);
        var game = await GetHtmlAsync(learnerClient, activityPath);
        Assert.Contains(configuredItems[0].Word, game);
        Assert.Contains("Audio playback is unavailable for this word", game);
        Assert.Contains("I would like to try this word again", game);

        var retryForm = BuildResponseForm(game, WordReaderPracticeOutcome.RetryRequested);
        var retry = await learnerClient.PostAsync(
            $"/activities/{assignmentId}/word-reader/answer",
            new FormUrlEncodedContent(retryForm));
        Assert.Equal(HttpStatusCode.Redirect, retry.StatusCode);
        var retryPage = await GetHtmlAsync(learnerClient, activityPath);
        Assert.Contains(configuredItems[0].Word, retryPage);
        Assert.Contains("Take another look at this word", retryPage);

        // Reposting the same form is idempotent and does not save an extra retry.
        var repeatedRetry = await learnerClient.PostAsync(
            $"/activities/{assignmentId}/word-reader/answer",
            new FormUrlEncodedContent(retryForm));
        Assert.Equal(HttpStatusCode.Redirect, repeatedRetry.StatusCode);
        game = await GetHtmlAsync(learnerClient, activityPath);

        for (var index = 0; index < configuredItems.Count; index++)
        {
            if (index > 0)
            {
                Assert.Contains(configuredItems[index].Word, game);
            }

            var outcome = index == 1
                ? WordReaderPracticeOutcome.PractisedWithSupport
                : WordReaderPracticeOutcome.ReadWithoutHelp;
            var response = await learnerClient.PostAsync(
                $"/activities/{assignmentId}/word-reader/answer",
                new FormUrlEncodedContent(BuildResponseForm(game, outcome)));
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            game = await GetHtmlAsync(learnerClient, activityPath);
        }

        Assert.Contains("Practice set complete", game);
        Assert.Contains("4 of 4 words", game);
        Assert.Contains("did not record or evaluate your speech", game);

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
            Assert.Equal(4d, attempt.Score);
            Assert.Null(attempt.Accuracy);
            Assert.NotNull(attempt.DurationMilliseconds);

            var state = JsonSerializer.Deserialize<WordReaderAttemptState>(attempt.MetadataJson!, JsonOptions);
            Assert.NotNull(state);
            Assert.Equal("Word Reading", state.SkillArea);
            Assert.Equal("1.0", state.ContentVersion);
            Assert.Equal(4, state.CurrentItemIndex);
            Assert.Equal(5, state.Responses.Count);
            Assert.Equal(1, state.Responses.Count(response => response.Outcome == WordReaderPracticeOutcome.RetryRequested));
            Assert.Equal(1, state.Responses.Count(response => response.Outcome == WordReaderPracticeOutcome.PractisedWithSupport));
            Assert.All(state.Responses, response =>
            {
                Assert.Null(response.IsCorrect);
                Assert.Equal("SelfReportedOnly", response.EvaluationMethod);
            });
        }

        using var otherLearnerClient = CreateClient(factory);
        await SignInAsync(otherLearnerClient, otherLearnerCredentials.Email, otherLearnerCredentials.Password);
        var unauthorized = await otherLearnerClient.GetAsync(activityPath);
        Assert.Equal(HttpStatusCode.NotFound, unauthorized.StatusCode);
    }

    [Fact]
    public async Task Word_reader_uses_matching_target_skill_and_has_no_fallback_for_unconfigured_skill()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        var credentials = await factory.CreateUserAsync(ApplicationRoles.Learner);
        var (_, decodingAssignmentId) = await CreateAssignmentAsync(factory, credentials.Email, "Decoding / Non-word Reading");
        var (_, unmatchedAssignmentId) = await CreateAssignmentAsync(factory, credentials.Email, "Rapid Naming");
        using var client = CreateClient(factory);
        await SignInAsync(client, credentials.Email, credentials.Password);

        var decodingPath = $"/activities/{decodingAssignmentId}/word-reader";
        var decodingReady = await GetHtmlAsync(client, decodingPath);
        Assert.Contains("Practice focus: Decoding / Non-word Reading", decodingReady);
        var start = await client.PostAsync(
            $"/activities/{decodingAssignmentId}/word-reader/start",
            AntiForgeryForm(decodingReady));
        Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);
        var decodingRun = await GetHtmlAsync(client, decodingPath);
        Assert.Contains("mip", decodingRun);
        Assert.DoesNotContain("Practice focus: Word Reading", decodingRun);

        var unmatched = await GetHtmlAsync(client, $"/activities/{unmatchedAssignmentId}/word-reader");
        Assert.Contains("No valid word practice content is configured", unmatched);
        Assert.DoesNotContain("Start word practice", unmatched);
    }

    private static async Task<(string LearnerId, int AssignmentId)> CreateAssignmentAsync(
        LexiAssistWebApplicationFactory factory,
        string learnerEmail,
        string flaggedSkillArea)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var learner = await userManager.FindByEmailAsync(learnerEmail);
        Assert.NotNull(learner);
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTime.UtcNow;
        var screeningAttempt = new ScreeningAttempt
        {
            UserId = learner.Id,
            StartedAtUtc = now.AddMinutes(-3),
            CompletedAtUtc = now.AddMinutes(-2),
            Status = ScreeningAttemptStatus.Completed
        };
        dbContext.ScreeningAttempts.Add(screeningAttempt);
        await dbContext.SaveChangesAsync();

        var report = new ScreeningReport
        {
            ScreeningAttemptId = screeningAttempt.Id,
            UserId = learner.Id,
            GeneratedAtUtc = now.AddMinutes(-2),
            Summary = "Test screening support summary.",
            FlaggedAreasJson = JsonSerializer.Serialize(
                new[] { new ScreeningReportFlaggedAreaSnapshot(flaggedSkillArea, 40m) }, JsonOptions),
            ScoringVersion = "test",
            IsReferralRecommended = false,
            SkillAreasJson = "[]",
            RecommendedNextAction = "Practice support"
        };
        dbContext.ScreeningReports.Add(report);
        await dbContext.SaveChangesAsync();

        var activity = await dbContext.LearningActivities.SingleAsync(item => item.Name == "Word Reader");
        var assignment = new ActivityAssignment
        {
            LearnerId = learner.Id,
            LearningActivityId = activity.Id,
            RelatedScreeningReportId = report.Id,
            Status = ActivityAssignmentStatus.Assigned,
            AssignedAtUtc = now
        };
        dbContext.ActivityAssignments.Add(assignment);
        await dbContext.SaveChangesAsync();
        return (learner.Id, assignment.Id);
    }

    private static Dictionary<string, string> BuildResponseForm(string html, WordReaderPracticeOutcome outcome) => new()
    {
        ["__RequestVerificationToken"] = ExtractInputValue(html, "__RequestVerificationToken"),
        ["AttemptId"] = ExtractInputValue(html, "AttemptId"),
        ["ItemId"] = ExtractInputValue(html, "ItemId"),
        ["SubmissionId"] = ExtractInputValue(html, "SubmissionId"),
        ["Outcome"] = outcome.ToString()
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
