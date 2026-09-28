using System.Net;
using System.Text.RegularExpressions;
using System.Text.Json;
using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Identity;
using LexiAssist.Web.Models.Screening;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LexiAssist.Tests;

public sealed class LearnerScreeningWorkflowTests
{
    [Fact]
    public async Task Screening_routes_are_for_learners_and_exclude_inactive_categories()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        using var anonymousClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var anonymousResult = await anonymousClient.GetAsync("/screening");
        Assert.Equal(HttpStatusCode.Redirect, anonymousResult.StatusCode);
        Assert.StartsWith("/Account/Login", GetLocationPath(anonymousResult));

        using var administratorClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (adminEmail, adminPassword) = await factory.CreateUserAsync(ApplicationRoles.Administrator);
        await SignInAsync(administratorClient, adminEmail, adminPassword);
        var administratorResult = await administratorClient.GetAsync("/screening");
        Assert.Equal(HttpStatusCode.Redirect, administratorResult.StatusCode);
        Assert.StartsWith("/Account/AccessDenied", GetLocationPath(administratorResult));

        using var psychologistClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (psychologistEmail, psychologistPassword) = await factory.CreateUserAsync(ApplicationRoles.Psychologist);
        await SignInAsync(psychologistClient, psychologistEmail, psychologistPassword);
        var psychologistResult = await psychologistClient.GetAsync("/screening");
        Assert.Equal(HttpStatusCode.Redirect, psychologistResult.StatusCode);
        Assert.StartsWith("/Account/AccessDenied", GetLocationPath(psychologistResult));

        using var learnerClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (learnerEmail, learnerPassword) = await factory.CreateUserAsync(ApplicationRoles.Learner);
        await SignInAsync(learnerClient, learnerEmail, learnerPassword);

        var landingResult = await learnerClient.GetAsync("/screening");
        Assert.Equal(HttpStatusCode.OK, landingResult.StatusCode);
        var landingHtml = await landingResult.Content.ReadAsStringAsync();
        Assert.Contains("educational screening and support", landingHtml);
        Assert.Contains("screening/support tier do not provide a clinical diagnosis", landingHtml);
        Assert.Contains("Word Reading", landingHtml);
        Assert.Contains("Oral Language and Listening Comprehension", landingHtml);
        Assert.DoesNotContain("Memory and Broader Thinking Skills", landingHtml);

        var startPage = await learnerClient.GetAsync("/screening/start");
        Assert.Equal(HttpStatusCode.OK, startPage.StatusCode);
        var startHtml = await startPage.Content.ReadAsStringAsync();
        Assert.Contains("Word Reading", startHtml);
        Assert.DoesNotContain("Memory and Broader Thinking Skills", startHtml);
    }

    [Fact]
    public async Task Learner_can_start_once_and_resume_only_their_incomplete_attempt()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        using var learnerClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (learnerEmail, learnerPassword) = await factory.CreateUserAsync(ApplicationRoles.Learner);
        await SignInAsync(learnerClient, learnerEmail, learnerPassword);
        await AddQuestionAsync(factory, new ScreeningQuestion
        {
            ScreeningTestId = 1,
            Prompt = "Saved screening question",
            QuestionType = ScreeningQuestionType.WordReading,
            CorrectAnswer = "map",
            AllowsAudioRecording = true,
            MetadataJson = JsonSerializer.Serialize(new ScreeningQuestionMetadata { Transcript = "private diagnostic transcript marker" }),
            SortOrder = 1
        });
        var anotherLearnerCredentials = await factory.CreateUserAsync(ApplicationRoles.Learner);

        var startPage = await learnerClient.GetAsync("/screening/start");
        var startHtml = await startPage.Content.ReadAsStringAsync();
        var token = ExtractAntiForgeryToken(startHtml);

        var startResult = await learnerClient.PostAsync(
            "/screening/start",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token
            }));
        Assert.Equal(HttpStatusCode.Redirect, startResult.StatusCode);
        Assert.Equal("/screening/resume", startResult.Headers.Location?.OriginalString);

        var landingResult = await learnerClient.GetAsync("/screening");
        var landingHtml = await landingResult.Content.ReadAsStringAsync();
        Assert.Contains("You have a screening in progress", landingHtml);
        Assert.Contains("Resume screening", landingHtml);
        Assert.DoesNotContain("Start screening</a>", landingHtml);

        var startAgainPage = await learnerClient.GetAsync("/screening/start");
        Assert.Equal(HttpStatusCode.Redirect, startAgainPage.StatusCode);
        Assert.Equal("/screening/resume", startAgainPage.Headers.Location?.OriginalString);

        var duplicatePost = await learnerClient.PostAsync(
            "/screening/start",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token
            }));
        Assert.Equal(HttpStatusCode.Redirect, duplicatePost.StatusCode);
        Assert.Equal("/screening/resume", duplicatePost.Headers.Location?.OriginalString);

        int attemptId;
        string learnerId;
        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>();
            var learner = await userManager.FindByEmailAsync(learnerEmail);
            Assert.NotNull(learner);
            learnerId = learner.Id;

            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var attempts = await dbContext.ScreeningAttempts.Where(attempt => attempt.UserId == learnerId).ToListAsync();
            var attempt = Assert.Single(attempts);
            attemptId = attempt.Id;
            Assert.Equal(ScreeningAttemptStatus.InProgress, attempt.Status);
            Assert.Null(attempt.CompletedAtUtc);
            Assert.Equal(1, attempt.CurrentTestId);
            Assert.Empty(await dbContext.ScreeningTestAttempts.Where(testAttempt => testAttempt.ScreeningAttemptId == attemptId).ToListAsync());
        }

        var resumeResult = await learnerClient.GetAsync("/screening/resume");
        Assert.Equal(HttpStatusCode.OK, resumeResult.StatusCode);
        var resumeHtml = await resumeResult.Content.ReadAsStringAsync();
        Assert.Contains("Saved screening question", resumeHtml);
        Assert.Contains("Question 1 of 1", resumeHtml);
        Assert.Contains("Assessment in progress", resumeHtml);
        Assert.Contains("class=\"learner-screening-type-badge\">Word reading</span>", resumeHtml);
        Assert.Contains("100%", resumeHtml);
        Assert.Contains("Pause and return later", resumeHtml);
        Assert.Contains("Word Reading", resumeHtml);
        Assert.Contains("data-learner-audio-recorder", resumeHtml);
        Assert.DoesNotContain("Live transcription", resumeHtml);
        Assert.DoesNotContain("recognized speech", resumeHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("NLP analysis", resumeHtml);
        Assert.DoesNotContain("Confidence", resumeHtml);
        Assert.DoesNotContain("Pronunciation score", resumeHtml);
        Assert.DoesNotContain("speech match", resumeHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private diagnostic transcript marker", resumeHtml);
        Assert.DoesNotContain("Memory and Broader Thinking Skills", resumeHtml);
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Single(await dbContext.ScreeningTestAttempts.Where(testAttempt => testAttempt.ScreeningAttemptId == attemptId).ToListAsync());
        }

        using var anotherLearnerClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(anotherLearnerClient, anotherLearnerCredentials.Email, anotherLearnerCredentials.Password);

        var foreignResume = await anotherLearnerClient.GetAsync("/screening/resume");
        Assert.Equal(HttpStatusCode.Redirect, foreignResume.StatusCode);
        Assert.Equal("/screening", foreignResume.Headers.Location?.OriginalString);

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.ScreeningAttempts.Add(new ScreeningAttempt
            {
                UserId = learnerId,
                StartedAtUtc = DateTime.UtcNow,
                Status = ScreeningAttemptStatus.InProgress
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
        }
    }

    private static async Task SignInAsync(HttpClient client, string email, string password)
    {
        var loginPage = await client.GetAsync("/Account/Login");
        var token = ExtractAntiForgeryToken(await loginPage.Content.ReadAsStringAsync());
        var loginResult = await client.PostAsync(
            "/Account/Login",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["Email"] = email,
                ["Password"] = password,
                ["RememberMe"] = "false"
            }));

        Assert.Equal(HttpStatusCode.Redirect, loginResult.StatusCode);
    }

    private static async Task AddQuestionAsync(LexiAssistWebApplicationFactory factory, ScreeningQuestion question)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.ScreeningQuestions.Add(question);
        await dbContext.SaveChangesAsync();
    }

    private static string ExtractAntiForgeryToken(string html)
    {
        var match = Regex.Match(
            html,
            "<input[^>]*name=\\\"__RequestVerificationToken\\\"[^>]*value=\\\"([^\\\"]+)\\\"",
            RegexOptions.IgnoreCase);

        Assert.True(match.Success, "The rendered page should include an antiforgery token.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static string GetLocationPath(HttpResponseMessage response)
    {
        var location = response.Headers.Location;
        return location is null
            ? string.Empty
            : location.IsAbsoluteUri
                ? location.PathAndQuery
                : location.OriginalString;
    }
}
