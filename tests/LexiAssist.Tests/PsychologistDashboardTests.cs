using System.Net;
using System.Text.RegularExpressions;
using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Identity;
using LexiAssist.Web.Models.Referrals;
using LexiAssist.Web.Models.Reports;
using LexiAssist.Web.Models.Screening;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace LexiAssist.Tests;

public sealed class PsychologistDashboardTests
{
    [Fact]
    public async Task Dashboard_shows_only_assigned_referrals_in_their_workflow_sections()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        var (psychologistId, psychologistEmail, psychologistPassword) = await CreateUserAsync(
            factory,
            ApplicationRoles.Psychologist,
            "Morgan",
            "Professional");
        var (otherPsychologistId, _, _) = await CreateUserAsync(
            factory,
            ApplicationRoles.Psychologist,
            "Taylor",
            "Professional");

        await CreateReferralAsync(factory, psychologistId, "Assigned", "Learner", ReferralStatus.Assigned, null);
        await CreateReferralAsync(factory, psychologistId, "Review", "Learner", ReferralStatus.UnderReview, DateTime.UtcNow.AddHours(-2));
        await CreateReferralAsync(factory, psychologistId, "Followup", "Learner", ReferralStatus.FollowUpRequested, DateTime.UtcNow.AddHours(-1));
        await CreateReferralAsync(factory, psychologistId, "Complete", "Learner", ReferralStatus.Completed, DateTime.UtcNow.AddMinutes(-45));
        await CreateReferralAsync(factory, psychologistId, "Closed", "Learner", ReferralStatus.Closed, DateTime.UtcNow.AddMinutes(-30));
        await CreateReferralAsync(factory, null, "Unassigned", "Learner", ReferralStatus.Pending, null);
        await CreateReferralAsync(factory, otherPsychologistId, "Other", "PsychologistCase", ReferralStatus.Assigned, null);
        await factory.CreateUserAsync(ApplicationRoles.Learner);

        using var client = CreateClient(factory);
        await SignInAsync(client, psychologistEmail, psychologistPassword);

        var response = await client.GetAsync("/Dashboard/Psychologist");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("Assigned pending referrals", html);
        Assert.Contains("Assigned Learner", html);
        Assert.Contains("Referrals under review", html);
        Assert.Contains("Review Learner", html);
        Assert.Contains("Recently reviewed cases", html);
        Assert.Contains("Followup Learner", html);
        Assert.Contains("Complete Learner", html);
        Assert.Contains("Closed Learner", html);
        Assert.Contains("Risk/support tier", html);
        Assert.Contains("Referral date (UTC)", html);
        Assert.Contains("Red", html);
        Assert.Contains("href=\"/psychologist/referrals\"", html);
        Assert.DoesNotContain("Unassigned Learner", html);
        Assert.DoesNotContain("Other PsychologistCase", html);
    }

    [Fact]
    public async Task Dashboard_shows_helpful_empty_states_when_no_referrals_are_assigned()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        var (_, email, password) = await CreateUserAsync(
            factory,
            ApplicationRoles.Psychologist,
            "Morgan",
            "Professional");
        using var client = CreateClient(factory);
        await SignInAsync(client, email, password);

        var response = await client.GetAsync("/Dashboard/Psychologist");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("No assigned referrals are waiting for your review.", html);
        Assert.Contains("You have no referrals under review.", html);
        Assert.Contains("No recently reviewed cases to show.", html);
    }

    [Theory]
    [InlineData(ApplicationRoles.Learner)]
    [InlineData(ApplicationRoles.Administrator)]
    public async Task Other_roles_cannot_access_the_psychologist_dashboard(string role)
    {
        using var factory = new LexiAssistWebApplicationFactory();
        var (email, password) = await factory.CreateUserAsync(role);
        using var client = CreateClient(factory);
        await SignInAsync(client, email, password);

        var response = await client.GetAsync("/Dashboard/Psychologist");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/AccessDenied", GetLocationPath(response));
    }

    [Fact]
    public async Task Unauthenticated_user_is_redirected_from_the_psychologist_dashboard()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/Dashboard/Psychologist");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/Login", GetLocationPath(response));
    }

    private static async Task CreateReferralAsync(
        LexiAssistWebApplicationFactory factory,
        string? psychologistId,
        string learnerFirstName,
        string learnerLastName,
        ReferralStatus status,
        DateTime? reviewedAtUtc)
    {
        var (learnerId, _, _) = await CreateUserAsync(factory, ApplicationRoles.Learner, learnerFirstName, learnerLastName);
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var createdAtUtc = DateTime.UtcNow.AddDays(-2);
        var attempt = new ScreeningAttempt
        {
            UserId = learnerId,
            StartedAtUtc = createdAtUtc.AddMinutes(-10),
            CompletedAtUtc = createdAtUtc.AddMinutes(10),
            Status = ScreeningAttemptStatus.Completed
        };
        dbContext.ScreeningAttempts.Add(attempt);
        await dbContext.SaveChangesAsync();

        var report = new ScreeningReport
        {
            ScreeningAttemptId = attempt.Id,
            UserId = learnerId,
            GeneratedAtUtc = createdAtUtc,
            RiskTier = "Red",
            Summary = "Educational screening results can help guide support discussions. This is not a diagnosis.",
            FlaggedAreasJson = "[]",
            ScoringVersion = "dashboard-test-v1",
            IsReferralRecommended = true,
            SkillAreasJson = "[]",
            RecommendedNextAction = "A suitable support professional may review this screening."
        };
        dbContext.ScreeningReports.Add(report);
        await dbContext.SaveChangesAsync();

        dbContext.Referrals.Add(new Referral
        {
            ScreeningReportId = report.Id,
            LearnerId = learnerId,
            AssignedPsychologistId = psychologistId,
            Status = status,
            CreatedAtUtc = createdAtUtc,
            ReviewedAtUtc = reviewedAtUtc
        });
        await dbContext.SaveChangesAsync();
    }

    private static async Task<(string Id, string Email, string Password)> CreateUserAsync(
        LexiAssistWebApplicationFactory factory,
        string role,
        string firstName,
        string lastName)
    {
        var (email, password) = await factory.CreateUserAsync(role);
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.NotNull(user);
        user.FirstName = firstName;
        user.LastName = lastName;
        var result = await userManager.UpdateAsync(user);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(error => error.Description)));
        return (user.Id, email, password);
    }

    private static HttpClient CreateClient(LexiAssistWebApplicationFactory factory) =>
        factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

    private static async Task SignInAsync(HttpClient client, string email, string password)
    {
        var loginPage = await client.GetAsync("/Account/Login");
        var token = ExtractAntiForgeryToken(await loginPage.Content.ReadAsStringAsync());
        var result = await client.PostAsync(
            "/Account/Login",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["Email"] = email,
                ["Password"] = password,
                ["RememberMe"] = "false"
            }));
        Assert.Equal(HttpStatusCode.Redirect, result.StatusCode);
    }

    private static string ExtractAntiForgeryToken(string html)
    {
        var match = Regex.Match(html, "<input[^>]*name=\\\"__RequestVerificationToken\\\"[^>]*value=\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase);
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
