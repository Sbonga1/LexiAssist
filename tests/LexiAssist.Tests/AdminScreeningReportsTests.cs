using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Identity;
using LexiAssist.Web.Models.Reports;
using LexiAssist.Web.Models.Screening;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace LexiAssist.Tests;

public sealed class AdminScreeningReportsTests
{
    [Theory]
    [InlineData(ApplicationRoles.Learner)]
    [InlineData(ApplicationRoles.Psychologist)]
    public async Task Non_administrator_cannot_access_reports(string role)
    {
        using var factory = new LexiAssistWebApplicationFactory();
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var (email, password) = await factory.CreateUserAsync(role);
        await SignInAsync(client, email, password);
        var (learnerId, _) = await CreateLearnerAsync(factory, "Private", "Learner", null);
        var reportId = await AddReportAsync(
            factory,
            learnerId,
            new DateTime(2026, 4, 10, 9, 0, 0, DateTimeKind.Utc),
            "Red");

        var response = await client.GetAsync("/admin/screening-reports");
        var detailsResponse = await client.GetAsync($"/admin/screening-reports/{reportId}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/AccessDenied", GetLocationPath(response));
        Assert.Equal(HttpStatusCode.Redirect, detailsResponse.StatusCode);
        Assert.StartsWith("/Account/AccessDenied", GetLocationPath(detailsResponse));
    }

    [Fact]
    public async Task Administrator_can_filter_reports_and_open_saved_category_results()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var (adminEmail, adminPassword) = await factory.CreateUserAsync(ApplicationRoles.Administrator);
        await SignInAsync(client, adminEmail, adminPassword);

        var (learnerId, learnerEmail) = await CreateLearnerAsync(factory, "Avery", "Reader", "Avery preferred");
        var firstReportId = await AddReportAsync(factory, learnerId, new DateTime(2026, 5, 12, 9, 30, 0, DateTimeKind.Utc), "Amber");
        await AddReportAsync(factory, learnerId, new DateTime(2026, 5, 13, 9, 30, 0, DateTimeKind.Utc), "Green");
        var (otherLearnerId, _) = await CreateLearnerAsync(factory, "Jordan", "Other", null);
        await AddReportAsync(factory, otherLearnerId, new DateTime(2026, 5, 12, 10, 0, 0, DateTimeKind.Utc), "Amber");

        var filtered = await client.GetAsync(
            "/admin/screening-reports?searchLearner=Avery&fromDate=2026-05-12&toDate=2026-05-12&riskTier=Amber");
        Assert.Equal(HttpStatusCode.OK, filtered.StatusCode);
        var filteredHtml = await filtered.Content.ReadAsStringAsync();
        Assert.Contains("Showing 1–1 of 1 reports", filteredHtml);
        Assert.Contains("Avery preferred", filteredHtml);
        Assert.DoesNotContain(learnerEmail, filteredHtml);
        Assert.DoesNotContain("Word Reading", filteredHtml);
        Assert.DoesNotContain("82%", filteredHtml);
        Assert.DoesNotContain("Saved summary for this completed educational screening.", filteredHtml);

        var details = await client.GetAsync($"/admin/screening-reports/{firstReportId}");
        Assert.Equal(HttpStatusCode.OK, details.StatusCode);
        var detailsHtml = await details.Content.ReadAsStringAsync();
        Assert.Contains(learnerEmail, detailsHtml);
        Assert.Contains("Screening category results", detailsHtml);
        Assert.Contains("Word Reading", detailsHtml);
        Assert.Contains("82%", detailsHtml);
        Assert.Contains("Support review area", detailsHtml);
        Assert.Contains("saved report snapshot", detailsHtml);
        Assert.DoesNotContain("PasswordHash", detailsHtml);
    }

    [Fact]
    public async Task Report_list_paginates_and_preserves_search_filter()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var (adminEmail, adminPassword) = await factory.CreateUserAsync(ApplicationRoles.Administrator);
        await SignInAsync(client, adminEmail, adminPassword);
        var (learnerId, _) = await CreateLearnerAsync(factory, "Casey", "Manyreports", null);

        for (var day = 1; day <= 21; day++)
        {
            await AddReportAsync(
                factory,
                learnerId,
                new DateTime(2026, 6, day, 12, 0, 0, DateTimeKind.Utc),
                day % 2 == 0 ? "Amber" : "Green");
        }

        var firstPage = await client.GetAsync("/admin/screening-reports?searchLearner=Casey&page=1");
        var firstPageHtml = await firstPage.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, firstPage.StatusCode);
        Assert.Contains("Showing 1–5 of 21 reports", firstPageHtml);
        Assert.Contains("2026-06-21", firstPageHtml);
        Assert.DoesNotContain("2026-06-01", firstPageHtml);
        Assert.Contains("searchLearner=Casey", firstPageHtml);

        var secondPage = await client.GetAsync("/admin/screening-reports?searchLearner=Casey&page=2");
        var secondPageHtml = await secondPage.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, secondPage.StatusCode);
        Assert.Contains("Showing 6–10 of 21 reports", secondPageHtml);
        Assert.Contains("2026-06-16", secondPageHtml);
        Assert.DoesNotContain("2026-06-21", secondPageHtml);
    }

    [Fact]
    public async Task Date_filters_use_south_african_calendar_boundaries()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var (adminEmail, adminPassword) = await factory.CreateUserAsync(ApplicationRoles.Administrator);
        await SignInAsync(client, adminEmail, adminPassword);
        var (learnerId, _) = await CreateLearnerAsync(factory, "Boundary", "Reader", null);

        await AddReportAsync(factory, learnerId, new DateTime(2026, 5, 12, 22, 30, 0, DateTimeKind.Utc), "Green");

        var localDate = await client.GetAsync("/admin/screening-reports?fromDate=2026-05-13&toDate=2026-05-13");
        var localDateHtml = await localDate.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, localDate.StatusCode);
        Assert.Contains("Showing 1–1 of 1 reports", localDateHtml);
        Assert.Contains("Boundary Reader", localDateHtml);

        var previousDate = await client.GetAsync("/admin/screening-reports?fromDate=2026-05-12&toDate=2026-05-12");
        var previousDateHtml = await previousDate.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, previousDate.StatusCode);
        Assert.Contains("Showing 0–0 of 0 reports", previousDateHtml);
        Assert.DoesNotContain("Boundary Reader", previousDateHtml);
    }

    private static async Task<(string Id, string Email)> CreateLearnerAsync(
        LexiAssistWebApplicationFactory factory,
        string firstName,
        string lastName,
        string? preferredName)
    {
        var (email, _) = await factory.CreateUserAsync(ApplicationRoles.Learner);
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.NotNull(user);
        user.FirstName = firstName;
        user.LastName = lastName;
        user.PreferredName = preferredName;
        var updateResult = await userManager.UpdateAsync(user);
        Assert.True(updateResult.Succeeded, string.Join("; ", updateResult.Errors.Select(error => error.Description)));
        return (user.Id, email);
    }

    private static async Task<int> AddReportAsync(
        LexiAssistWebApplicationFactory factory,
        string userId,
        DateTime generatedAtUtc,
        string? riskTier)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var attempt = new ScreeningAttempt
        {
            UserId = userId,
            StartedAtUtc = generatedAtUtc.AddMinutes(-10),
            CompletedAtUtc = generatedAtUtc.AddMinutes(-1),
            Status = ScreeningAttemptStatus.Completed
        };
        dbContext.ScreeningAttempts.Add(attempt);
        await dbContext.SaveChangesAsync();

        var report = new ScreeningReport
        {
            ScreeningAttemptId = attempt.Id,
            UserId = userId,
            GeneratedAtUtc = generatedAtUtc,
            RiskTier = riskTier,
            Summary = "Saved summary for this completed educational screening.",
            FlaggedAreasJson = "[]",
            ScoringVersion = "screening-v1",
            IsReferralRecommended = false,
            RecommendedNextAction = "Consider discussing support options.",
            SkillAreasJson = JsonSerializer.Serialize(
            new[]
            {
                new ScreeningReportSkillAreaSnapshot("Word Reading", 82m, false, true, "This area may benefit from additional support."),
                new ScreeningReportSkillAreaSnapshot("Listening", 94m, true, false, "This is a relative strength.")
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
        };
        dbContext.ScreeningReports.Add(report);
        await dbContext.SaveChangesAsync();
        return report.Id;
    }

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
