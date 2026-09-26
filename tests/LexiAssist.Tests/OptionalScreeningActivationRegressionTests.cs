using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Identity;
using LexiAssist.Web.Models.Screening;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LexiAssist.Tests;

public sealed class OptionalScreeningActivationRegressionTests
{
    private const int OptionalCategoryId = 9;
    private const string OptionalPrompt = "Optional activation regression question";

    [Fact]
    public async Task Optional_category_activation_keeps_one_attempt_and_deactivation_preserves_completed_result()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        var adminCredentials = await factory.CreateUserAsync(ApplicationRoles.Administrator);
        var learnerCredentials = await factory.CreateUserAsync(ApplicationRoles.Learner);
        await AddOptionalQuestionAsync(factory);

        using var adminClient = CreateClient(factory);
        await SignInAsync(adminClient, adminCredentials.Email, adminCredentials.Password);

        var inactiveDetails = await adminClient.GetAsync($"/admin/screening-tests/{OptionalCategoryId}");
        Assert.Equal(HttpStatusCode.OK, inactiveDetails.StatusCode);
        Assert.Contains("disabled by default", await inactiveDetails.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        using var learnerClient = CreateClient(factory);
        await SignInAsync(learnerClient, learnerCredentials.Email, learnerCredentials.Password);
        var inactiveStart = await learnerClient.GetAsync("/screening/start");
        Assert.DoesNotContain("Memory and Broader Thinking Skills", await inactiveStart.Content.ReadAsStringAsync());

        await ActivateOptionalCategoryAsync(adminClient, inactiveDetails);

        var activeStart = await learnerClient.GetAsync("/screening/start");
        var activeStartHtml = await activeStart.Content.ReadAsStringAsync();
        Assert.Contains("Memory and Broader Thinking Skills", activeStartHtml);

        var startResponse = await learnerClient.PostAsync("/screening/start", BuildActionForm(ExtractAntiForgeryToken(activeStartHtml)));
        Assert.Equal("/screening/resume", GetLocationPath(startResponse));
        Assert.Equal(1, await CountAttemptsAsync(factory, learnerCredentials.Email));

        var questionPage = await learnerClient.GetAsync("/screening/resume");
        var questionHtml = await questionPage.Content.ReadAsStringAsync();
        Assert.Contains(OptionalPrompt, questionHtml);
        var answerResponse = await learnerClient.PostAsync(
            "/screening/respond",
            BuildAnswerForm(questionHtml, "SelectedAnswer", "yes"));
        Assert.Equal("/screening/resume", GetLocationPath(answerResponse));

        var completionResponse = await learnerClient.GetAsync("/screening/resume");
        var resultPath = GetLocationPath(completionResponse);
        Assert.StartsWith("/screening/results/", resultPath);

        var activeDetails = await adminClient.GetAsync($"/admin/screening-tests/{OptionalCategoryId}");
        await DeactivateOptionalCategoryAsync(adminClient, activeDetails);

        var historicalResult = await learnerClient.GetAsync(resultPath);
        Assert.Equal(HttpStatusCode.OK, historicalResult.StatusCode);
        var historicalHtml = await historicalResult.Content.ReadAsStringAsync();
        Assert.Contains("Memory and Broader Thinking Skills", historicalHtml);
        Assert.Contains("screening information only", historicalHtml, StringComparison.OrdinalIgnoreCase);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var optionalCategory = await dbContext.ScreeningTests.SingleAsync(test => test.Id == OptionalCategoryId);
        Assert.False(optionalCategory.IsActive);
        Assert.Single(await dbContext.ScreeningQuestions.Where(question => question.ScreeningTestId == OptionalCategoryId).ToListAsync());

        var learner = await dbContext.Users.SingleAsync(user => user.Email == learnerCredentials.Email);
        var attempt = Assert.Single(await dbContext.ScreeningAttempts
            .Include(item => item.TestAttempts)
            .ThenInclude(item => item.Responses)
            .Where(item => item.UserId == learner.Id)
            .ToListAsync());
        Assert.Equal(ScreeningAttemptStatus.Completed, attempt.Status);
        var testAttempt = Assert.Single(attempt.TestAttempts);
        Assert.Single(testAttempt.Responses);
        Assert.NotNull(await dbContext.ScreeningReports.SingleOrDefaultAsync(report => report.ScreeningAttemptId == attempt.Id));

        var inactiveLanding = await learnerClient.GetAsync("/screening");
        Assert.DoesNotContain("Memory and Broader Thinking Skills", await inactiveLanding.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Deactivating_a_started_optional_category_does_not_break_the_in_progress_attempt()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        var adminCredentials = await factory.CreateUserAsync(ApplicationRoles.Administrator);
        var learnerCredentials = await factory.CreateUserAsync(ApplicationRoles.Learner);
        await AddOptionalQuestionAsync(factory);

        using var adminClient = CreateClient(factory);
        await SignInAsync(adminClient, adminCredentials.Email, adminCredentials.Password);
        var details = await adminClient.GetAsync($"/admin/screening-tests/{OptionalCategoryId}");
        await ActivateOptionalCategoryAsync(adminClient, details);

        using var learnerClient = CreateClient(factory);
        await SignInAsync(learnerClient, learnerCredentials.Email, learnerCredentials.Password);
        var startPage = await learnerClient.GetAsync("/screening/start");
        var startHtml = await startPage.Content.ReadAsStringAsync();
        await learnerClient.PostAsync("/screening/start", BuildActionForm(ExtractAntiForgeryToken(startHtml)));

        var startedQuestionPage = await learnerClient.GetAsync("/screening/resume");
        var startedQuestionHtml = await startedQuestionPage.Content.ReadAsStringAsync();
        Assert.Contains(OptionalPrompt, startedQuestionHtml);

        details = await adminClient.GetAsync($"/admin/screening-tests/{OptionalCategoryId}");
        await DeactivateOptionalCategoryAsync(adminClient, details);

        var resumedQuestionPage = await learnerClient.GetAsync("/screening/resume");
        var resumedQuestionHtml = await resumedQuestionPage.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, resumedQuestionPage.StatusCode);
        Assert.Contains(OptionalPrompt, resumedQuestionHtml);

        var answerResponse = await learnerClient.PostAsync(
            "/screening/respond",
            BuildAnswerForm(resumedQuestionHtml, "SelectedAnswer", "yes"));
        Assert.Equal("/screening/resume", GetLocationPath(answerResponse));

        var completionResponse = await learnerClient.GetAsync("/screening/resume");
        Assert.StartsWith("/screening/results/", GetLocationPath(completionResponse));
        Assert.Equal(1, await CountAttemptsAsync(factory, learnerCredentials.Email));
    }

    private static HttpClient CreateClient(LexiAssistWebApplicationFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static async Task AddOptionalQuestionAsync(LexiAssistWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.ScreeningQuestions.Add(new ScreeningQuestion
        {
            ScreeningTestId = OptionalCategoryId,
            Prompt = OptionalPrompt,
            QuestionType = ScreeningQuestionType.MultipleChoice,
            CorrectAnswer = "yes",
            MetadataJson = JsonSerializer.Serialize(new ScreeningQuestionMetadata
            {
                ChoiceOptions = ["yes", "no"]
            }),
            SortOrder = 1
        });
        await dbContext.SaveChangesAsync();
    }

    private static async Task ActivateOptionalCategoryAsync(HttpClient adminClient, HttpResponseMessage details)
    {
        var html = await details.Content.ReadAsStringAsync();
        var response = await adminClient.PostAsync(
            $"/admin/screening-tests/{OptionalCategoryId}/activate",
            BuildActionForm(ExtractAntiForgeryToken(html)));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private static async Task DeactivateOptionalCategoryAsync(HttpClient adminClient, HttpResponseMessage details)
    {
        var html = await details.Content.ReadAsStringAsync();
        var response = await adminClient.PostAsync(
            $"/admin/screening-tests/{OptionalCategoryId}/deactivate",
            BuildActionForm(ExtractAntiForgeryToken(html)));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private static async Task<int> CountAttemptsAsync(LexiAssistWebApplicationFactory factory, string email)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await dbContext.Users.SingleAsync(item => item.Email == email);
        return await dbContext.ScreeningAttempts.CountAsync(attempt => attempt.UserId == user.Id);
    }

    private static async Task SignInAsync(HttpClient client, string email, string password)
    {
        var loginPage = await client.GetAsync("/Account/Login");
        var loginHtml = await loginPage.Content.ReadAsStringAsync();
        var loginResponse = await client.PostAsync(
            "/Account/Login",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiForgeryToken(loginHtml),
                ["Email"] = email,
                ["Password"] = password,
                ["RememberMe"] = "false"
            }));
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);
    }

    private static FormUrlEncodedContent BuildActionForm(string token) =>
        new(new Dictionary<string, string> { ["__RequestVerificationToken"] = token });

    private static FormUrlEncodedContent BuildAnswerForm(string html, string answerName, string answer) =>
        new(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiForgeryToken(html),
            ["QuestionToken"] = ExtractInputValue(html, "QuestionToken"),
            [answerName] = answer
        });

    private static string ExtractAntiForgeryToken(string html) => ExtractInputValue(html, "__RequestVerificationToken");

    private static string ExtractInputValue(string html, string name)
    {
        var match = Regex.Match(
            html,
            $"<input[^>]*name=\\\"{Regex.Escape(name)}\\\"[^>]*value=\\\"([^\\\"]*)\\\"",
            RegexOptions.IgnoreCase);
        Assert.True(match.Success, $"The rendered page should include '{name}'.");
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
