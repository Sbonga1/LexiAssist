using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Identity;
using LexiAssist.Web.Models.Referrals;
using LexiAssist.Web.Models.Screening;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LexiAssist.Tests;

public sealed class CompleteScreeningWorkflowTests
{
    [Fact]
    public async Task Learner_screening_is_scored_reported_and_referral_access_is_limited_to_authorized_people()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        var learnerCredentials = await factory.CreateUserAsync(ApplicationRoles.Learner);
        var unrelatedLearnerCredentials = await factory.CreateUserAsync(ApplicationRoles.Learner);
        var administratorCredentials = await factory.CreateUserAsync(ApplicationRoles.Administrator);
        var assignedPsychologistCredentials = await factory.CreateUserAsync(ApplicationRoles.Psychologist);
        var unrelatedPsychologistCredentials = await factory.CreateUserAsync(ApplicationRoles.Psychologist);
        string learnerId;

        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var learner = await userManager.FindByEmailAsync(learnerCredentials.Email);
            Assert.NotNull(learner);
            learner.FirstName = "Journey";
            learner.LastName = "Learner";
            var update = await userManager.UpdateAsync(learner);
            Assert.True(update.Succeeded, string.Join("; ", update.Errors.Select(error => error.Description)));
            learnerId = learner.Id;

            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.ScreeningQuestions.AddRange(
                new ScreeningQuestion
                {
                    ScreeningTestId = 1,
                    Prompt = "DEMO: choose the word shown",
                    QuestionType = ScreeningQuestionType.MultipleChoice,
                    CorrectAnswer = "sun",
                    MetadataJson = JsonSerializer.Serialize(new ScreeningQuestionMetadata
                    {
                        ChoiceOptions = ["sun", "moon"]
                    }),
                    SortOrder = 1
                },
                new ScreeningQuestion
                {
                    ScreeningTestId = 1,
                    Prompt = "DEMO: type the word shown",
                    QuestionType = ScreeningQuestionType.TextResponse,
                    CorrectAnswer = "garden",
                    SortOrder = 2
                });
            await dbContext.SaveChangesAsync();
        }

        using var learnerClient = CreateClient(factory);
        var learnerLogin = await SignInAsync(learnerClient, learnerCredentials.Email, learnerCredentials.Password);
        Assert.Equal("/Dashboard/Learner", learnerLogin.Headers.Location?.OriginalString);

        var startPage = await learnerClient.GetAsync("/screening/start");
        Assert.Equal(HttpStatusCode.OK, startPage.StatusCode);
        var startHtml = await startPage.Content.ReadAsStringAsync();
        Assert.Contains("do not provide a clinical diagnosis", startHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Word Reading", startHtml);
        var started = await learnerClient.PostAsync("/screening/start", Form(startHtml));
        Assert.Equal(HttpStatusCode.Redirect, started.StatusCode);
        Assert.Equal("/screening/resume", started.Headers.Location?.OriginalString);

        var firstQuestion = await GetQuestionAsync(learnerClient, "DEMO: choose the word shown");
        var firstAnswer = await learnerClient.PostAsync("/screening/respond", AnswerForm(firstQuestion, "SelectedAnswer", "moon"));
        Assert.Equal(HttpStatusCode.Redirect, firstAnswer.StatusCode);
        Assert.Equal("/screening/resume", firstAnswer.Headers.Location?.OriginalString);

        var secondQuestion = await GetQuestionAsync(learnerClient, "DEMO: type the word shown");
        var secondAnswer = await learnerClient.PostAsync("/screening/respond", AnswerForm(secondQuestion, "TextAnswer", "moon"));
        Assert.Equal(HttpStatusCode.Redirect, secondAnswer.StatusCode);
        Assert.Equal("/screening/resume", secondAnswer.Headers.Location?.OriginalString);

        var completion = await learnerClient.GetAsync("/screening/resume");
        Assert.Equal(HttpStatusCode.Redirect, completion.StatusCode);
        var resultPath = completion.Headers.Location?.OriginalString;
        Assert.NotNull(resultPath);
        Assert.StartsWith("/screening/results/", resultPath);
        var attemptId = int.Parse(resultPath["/screening/results/".Length..]);

        var learnerResult = await learnerClient.GetAsync(resultPath);
        Assert.Equal(HttpStatusCode.OK, learnerResult.StatusCode);
        var resultHtml = await learnerResult.Content.ReadAsStringAsync();
        Assert.Contains("Screening complete", resultHtml);
        Assert.Contains("Red", resultHtml);
        Assert.Contains("not a clinical diagnosis", resultHtml, StringComparison.OrdinalIgnoreCase);

        int reportId;
        int referralId;
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var attempt = await dbContext.ScreeningAttempts
                .Include(item => item.TestAttempts)
                .ThenInclude(item => item.Responses)
                .SingleAsync(item => item.Id == attemptId);

            Assert.Equal(learnerId, attempt.UserId);
            Assert.Equal(ScreeningAttemptStatus.Completed, attempt.Status);
            Assert.NotNull(attempt.CompletedAtUtc);
            Assert.Equal(0d, attempt.OverallScore);
            Assert.Equal("Red", attempt.RiskTier);
            Assert.Equal("1.0", attempt.RiskCalculationVersion);
            Assert.NotNull(attempt.RiskCalculatedAtUtc);

            var testAttempt = Assert.Single(attempt.TestAttempts);
            Assert.NotNull(testAttempt.CompletedAtUtc);
            Assert.Equal(0d, testAttempt.RawScore);
            Assert.Equal(0d, testAttempt.PercentageScore);
            Assert.Equal(2, testAttempt.Responses.Count);
            Assert.All(testAttempt.Responses, response => Assert.True(response.ResponseTimeMilliseconds >= 0));
            Assert.Contains(testAttempt.Responses, response => response.SelectedAnswer == "moon");
            Assert.Contains(testAttempt.Responses, response => response.TextAnswer == "moon");

            var report = await dbContext.ScreeningReports.SingleAsync(item => item.ScreeningAttemptId == attemptId);
            reportId = report.Id;
            Assert.Equal(learnerId, report.UserId);
            Assert.Equal("Red", report.RiskTier);
            Assert.Equal("1.0", report.ScoringVersion);
            Assert.True(report.IsReferralRecommended);
            Assert.Contains("not a clinical diagnosis", report.Summary, StringComparison.OrdinalIgnoreCase);

            var referral = await dbContext.Referrals.SingleAsync(item => item.ScreeningReportId == report.Id);
            referralId = referral.Id;
            Assert.Equal(learnerId, referral.LearnerId);
            Assert.Equal(ReferralStatus.Pending, referral.Status);
            Assert.Null(referral.AssignedPsychologistId);
        }

        using var administratorClient = CreateClient(factory);
        var administratorLogin = await SignInAsync(
            administratorClient,
            administratorCredentials.Email,
            administratorCredentials.Password);
        Assert.Equal("/Dashboard/Administrator", administratorLogin.Headers.Location?.OriginalString);

        var reportList = await administratorClient.GetAsync("/admin/screening-reports?searchLearner=Journey");
        Assert.Equal(HttpStatusCode.OK, reportList.StatusCode);
        var reportListHtml = await reportList.Content.ReadAsStringAsync();
        Assert.Contains("Journey Learner", reportListHtml);
        var adminReport = await administratorClient.GetAsync($"/admin/screening-reports/{reportId}");
        Assert.Equal(HttpStatusCode.OK, adminReport.StatusCode);
        var adminReportHtml = await adminReport.Content.ReadAsStringAsync();
        Assert.Contains("Red", adminReportHtml);
        Assert.Contains("Word Reading", adminReportHtml);

        var adminReferral = await administratorClient.GetAsync($"/admin/referrals/{referralId}");
        Assert.Equal(HttpStatusCode.OK, adminReferral.StatusCode);
        var referralHtml = await adminReferral.Content.ReadAsStringAsync();
        var assignment = await administratorClient.PostAsync(
            $"/admin/referrals/{referralId}/assign",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractInputValue(referralHtml, "__RequestVerificationToken"),
                ["PsychologistId"] = await GetUserIdAsync(factory, assignedPsychologistCredentials.Email)
            }));
        Assert.Equal(HttpStatusCode.Redirect, assignment.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var referral = await dbContext.Referrals.SingleAsync(item => item.Id == referralId);
            Assert.Equal(ReferralStatus.Assigned, referral.Status);
            Assert.Equal(
                await GetUserIdAsync(factory, assignedPsychologistCredentials.Email),
                referral.AssignedPsychologistId);
        }

        using var assignedPsychologistClient = CreateClient(factory);
        var assignedLogin = await SignInAsync(
            assignedPsychologistClient,
            assignedPsychologistCredentials.Email,
            assignedPsychologistCredentials.Password);
        Assert.Equal("/Dashboard/Psychologist", assignedLogin.Headers.Location?.OriginalString);
        var assignedReferral = await assignedPsychologistClient.GetAsync($"/psychologist/referrals/{referralId}");
        Assert.Equal(HttpStatusCode.OK, assignedReferral.StatusCode);
        Assert.Contains("Journey Learner", await assignedReferral.Content.ReadAsStringAsync());

        using var unrelatedLearnerClient = CreateClient(factory);
        await SignInAsync(unrelatedLearnerClient, unrelatedLearnerCredentials.Email, unrelatedLearnerCredentials.Password);
        var unrelatedLearnerResult = await unrelatedLearnerClient.GetAsync(resultPath);
        Assert.Equal(HttpStatusCode.NotFound, unrelatedLearnerResult.StatusCode);

        using var unrelatedPsychologistClient = CreateClient(factory);
        await SignInAsync(
            unrelatedPsychologistClient,
            unrelatedPsychologistCredentials.Email,
            unrelatedPsychologistCredentials.Password);
        var unrelatedReferral = await unrelatedPsychologistClient.GetAsync($"/psychologist/referrals/{referralId}");
        Assert.Equal(HttpStatusCode.NotFound, unrelatedReferral.StatusCode);
    }

    private static HttpClient CreateClient(LexiAssistWebApplicationFactory factory) =>
        factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

    private static async Task<HttpResponseMessage> SignInAsync(HttpClient client, string email, string password)
    {
        var loginPage = await client.GetAsync("/Account/Login");
        Assert.Equal(HttpStatusCode.OK, loginPage.StatusCode);
        var html = await loginPage.Content.ReadAsStringAsync();
        var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractInputValue(html, "__RequestVerificationToken"),
            ["Email"] = email,
            ["Password"] = password,
            ["RememberMe"] = "false"
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return response;
    }

    private static async Task<string> GetQuestionAsync(HttpClient client, string prompt)
    {
        var response = await client.GetAsync("/screening/resume");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains(prompt, html);
        return html;
    }

    private static FormUrlEncodedContent Form(string html) => new(new Dictionary<string, string>
    {
        ["__RequestVerificationToken"] = ExtractInputValue(html, "__RequestVerificationToken")
    });

    private static FormUrlEncodedContent AnswerForm(string html, string answerName, string answer) =>
        new(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractInputValue(html, "__RequestVerificationToken"),
            ["QuestionToken"] = ExtractInputValue(html, "QuestionToken"),
            [answerName] = answer
        });

    private static async Task<string> GetUserIdAsync(LexiAssistWebApplicationFactory factory, string email)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.NotNull(user);
        return user.Id;
    }

    private static string ExtractInputValue(string html, string name)
    {
        var match = Regex.Match(
            html,
            $"<input[^>]*name=\\\"{Regex.Escape(name)}\\\"[^>]*value=\\\"([^\\\"]*)\\\"",
            RegexOptions.IgnoreCase);
        Assert.True(match.Success, $"The rendered form should include '{name}'.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
}
