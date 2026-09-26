using System.Net;
using System.Text.RegularExpressions;
using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Identity;
using LexiAssist.Web.Models.Referrals;
using LexiAssist.Web.Models.Reports;
using LexiAssist.Web.Models.Screening;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LexiAssist.Tests;

/// <summary>
/// Focused regression coverage for security boundaries that span multiple workflows.
/// The feature-specific test classes cover the normal success paths.
/// </summary>
public sealed class ExpandedCoverageTests
{
    [Fact]
    public async Task Deactivated_account_is_rejected_during_a_new_login()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        var (email, password) = await factory.CreateUserAsync(ApplicationRoles.Learner);

        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByEmailAsync(email);
            Assert.NotNull(user);
            user.IsActive = false;
            var update = await userManager.UpdateAsync(user);
            Assert.True(update.Succeeded, string.Join("; ", update.Errors.Select(error => error.Description)));
        }

        using var client = CreateClient(factory);
        var loginPage = await client.GetAsync("/Account/Login");
        Assert.Equal(HttpStatusCode.OK, loginPage.StatusCode);
        var loginHtml = await loginPage.Content.ReadAsStringAsync();

        var response = await client.PostAsync(
            "/Account/Login",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractInputValue(loginHtml, "__RequestVerificationToken"),
                ["Email"] = email,
                ["Password"] = password,
                ["RememberMe"] = "false"
            }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("email address or password is incorrect", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(ApplicationRoles.Learner, "/admin/users")]
    [InlineData(ApplicationRoles.Psychologist, "/admin/screening-reports")]
    public async Task Non_administrator_roles_cannot_cross_into_administrator_management(string role, string path)
    {
        using var factory = new LexiAssistWebApplicationFactory();
        using var client = CreateClient(factory);
        var (email, password) = await factory.CreateUserAsync(role);
        await SignInAsync(client, email, password);

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/AccessDenied", GetLocationPath(response));
    }

    [Fact]
    public async Task Administrator_cannot_assign_a_referral_to_an_inactive_psychologist()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        using var adminClient = CreateClient(factory);
        var (adminEmail, adminPassword) = await factory.CreateUserAsync(ApplicationRoles.Administrator);
        var (learnerEmail, _) = await factory.CreateUserAsync(ApplicationRoles.Learner);
        var (psychologistEmail, _) = await factory.CreateUserAsync(ApplicationRoles.Psychologist);
        int referralId;
        string psychologistId;

        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var learner = await userManager.FindByEmailAsync(learnerEmail);
            var psychologist = await userManager.FindByEmailAsync(psychologistEmail);
            Assert.NotNull(learner);
            Assert.NotNull(psychologist);
            psychologistId = psychologist.Id;
            psychologist.IsActive = false;
            var update = await userManager.UpdateAsync(psychologist);
            Assert.True(update.Succeeded, string.Join("; ", update.Errors.Select(error => error.Description)));

            var now = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var attempt = new ScreeningAttempt
            {
                UserId = learner.Id,
                StartedAtUtc = now.AddMinutes(-10),
                CompletedAtUtc = now,
                Status = ScreeningAttemptStatus.Completed,
                RiskTier = nameof(ScreeningSupportTier.Red)
            };
            db.ScreeningAttempts.Add(attempt);
            await db.SaveChangesAsync();

            var report = new ScreeningReport
            {
                ScreeningAttemptId = attempt.Id,
                UserId = learner.Id,
                GeneratedAtUtc = now,
                RiskTier = nameof(ScreeningSupportTier.Red),
                Summary = "Educational screening summary.",
                FlaggedAreasJson = "[]",
                ScoringVersion = "test-v1",
                IsReferralRecommended = true,
                SkillAreasJson = "[]",
                RecommendedNextAction = "Consider support review."
            };
            db.ScreeningReports.Add(report);
            await db.SaveChangesAsync();

            var referral = new Referral
            {
                ScreeningReportId = report.Id,
                LearnerId = learner.Id,
                Status = ReferralStatus.Pending,
                CreatedAtUtc = now
            };
            db.Referrals.Add(referral);
            await db.SaveChangesAsync();
            referralId = referral.Id;
        }

        await SignInAsync(adminClient, adminEmail, adminPassword);
        var details = await adminClient.GetAsync($"/admin/referrals/{referralId}");
        Assert.Equal(HttpStatusCode.OK, details.StatusCode);
        var detailsHtml = await details.Content.ReadAsStringAsync();

        var result = await adminClient.PostAsync(
            $"/admin/referrals/{referralId}/assign",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractInputValue(detailsHtml, "__RequestVerificationToken"),
                ["PsychologistId"] = psychologistId
            }));

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Contains("Choose an active Psychologist account", await result.Content.ReadAsStringAsync());

        using var verifyScope = factory.Services.CreateScope();
        var savedReferral = await verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .Referrals.SingleAsync(item => item.Id == referralId);
        Assert.Equal(ReferralStatus.Pending, savedReferral.Status);
        Assert.Null(savedReferral.AssignedPsychologistId);
    }

    [Fact]
    public async Task Unassigned_psychologist_cannot_open_a_data_passport_by_tampering_with_the_referral_id()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        var (learnerEmail, _) = await factory.CreateUserAsync(ApplicationRoles.Learner);
        var (assignedEmail, _) = await factory.CreateUserAsync(ApplicationRoles.Psychologist);
        var (unassignedEmail, unassignedPassword) = await factory.CreateUserAsync(ApplicationRoles.Psychologist);
        int referralId;

        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var learner = await userManager.FindByEmailAsync(learnerEmail);
            var assigned = await userManager.FindByEmailAsync(assignedEmail);
            Assert.NotNull(learner);
            Assert.NotNull(assigned);

            var now = new DateTime(2026, 9, 2, 10, 0, 0, DateTimeKind.Utc);
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var attempt = new ScreeningAttempt
            {
                UserId = learner.Id,
                StartedAtUtc = now.AddMinutes(-5),
                CompletedAtUtc = now,
                Status = ScreeningAttemptStatus.Completed,
                RiskTier = nameof(ScreeningSupportTier.Red)
            };
            db.ScreeningAttempts.Add(attempt);
            await db.SaveChangesAsync();

            var report = new ScreeningReport
            {
                ScreeningAttemptId = attempt.Id,
                UserId = learner.Id,
                GeneratedAtUtc = now,
                RiskTier = nameof(ScreeningSupportTier.Red),
                Summary = "Educational screening summary.",
                FlaggedAreasJson = "[]",
                ScoringVersion = "test-v1",
                IsReferralRecommended = true,
                SkillAreasJson = "[]",
                RecommendedNextAction = "Consider support review."
            };
            db.ScreeningReports.Add(report);
            await db.SaveChangesAsync();

            var referral = new Referral
            {
                ScreeningReportId = report.Id,
                LearnerId = learner.Id,
                AssignedPsychologistId = assigned.Id,
                Status = ReferralStatus.Assigned,
                CreatedAtUtc = now
            };
            db.Referrals.Add(referral);
            await db.SaveChangesAsync();
            referralId = referral.Id;
        }

        using var client = CreateClient(factory);
        await SignInAsync(client, unassignedEmail, unassignedPassword);

        var response = await client.GetAsync($"/psychologist/referrals/{referralId}/data-passport");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static HttpClient CreateClient(LexiAssistWebApplicationFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static async Task SignInAsync(HttpClient client, string email, string password)
    {
        var loginPage = await client.GetAsync("/Account/Login");
        var html = await loginPage.Content.ReadAsStringAsync();
        var response = await client.PostAsync(
            "/Account/Login",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractInputValue(html, "__RequestVerificationToken"),
                ["Email"] = email,
                ["Password"] = password,
                ["RememberMe"] = "false"
            }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
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
