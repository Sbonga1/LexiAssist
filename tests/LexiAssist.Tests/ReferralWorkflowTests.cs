using System.Net;
using System.Text.RegularExpressions;
using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Identity;
using LexiAssist.Web.Models.Notifications;
using LexiAssist.Web.Models.Referrals;
using LexiAssist.Web.Models.Reports;
using LexiAssist.Web.Models.Screening;
using LexiAssist.Web.Services.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LexiAssist.Tests;

public sealed class ReferralWorkflowTests
{
    [Fact]
    public async Task Red_report_and_pending_referral_are_persisted_together()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        var (email, _) = await factory.CreateUserAsync(ApplicationRoles.Learner);
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var learner = await userManager.FindByEmailAsync(email);
        Assert.NotNull(learner);

        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var completedAt = DateTime.UtcNow;
        var attempt = new ScreeningAttempt
        {
            UserId = learner.Id,
            StartedAtUtc = completedAt.AddMinutes(-10),
            CompletedAtUtc = completedAt,
            Status = ScreeningAttemptStatus.Completed
        };
        dbContext.ScreeningAttempts.Add(attempt);
        await dbContext.SaveChangesAsync();

        var scoringResult = new ScreeningOverallScoringResult(
            10m,
            100m,
            10m,
            [],
            ScreeningCompletionStatus.Completed,
            1,
            1,
            []);
        var riskClassification = new ScreeningRiskClassificationResult(
            ScreeningSupportTier.Red,
            ScreeningRiskClassificationResult.GetTierDescription(ScreeningSupportTier.Red),
            10m,
            "risk-rules-test",
            completedAt);
        var reportService = scope.ServiceProvider.GetRequiredService<IScreeningReportGenerationService>();
        var report = reportService.Generate(learner.Id, attempt.Id, scoringResult, riskClassification);
        dbContext.ScreeningReports.Add(report);
        await dbContext.SaveChangesAsync();

        var referral = await dbContext.Referrals.SingleAsync(item => item.ScreeningReportId == report.Id);
        Assert.Equal(learner.Id, referral.LearnerId);
        Assert.Equal(ReferralStatus.Pending, referral.Status);
        Assert.Equal(report.GeneratedAtUtc, referral.CreatedAtUtc);
        Assert.Null(referral.AssignedPsychologistId);
    }

    [Fact]
    public async Task Administrator_assigns_referral_and_only_assigned_psychologist_can_review_it()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        using var adminClient = CreateClient(factory);
        var (adminEmail, adminPassword) = await factory.CreateUserAsync(ApplicationRoles.Administrator);
        await SignInAsync(adminClient, adminEmail, adminPassword);

        var (learnerId, _, _, _, referralId) = await CreateRedReferralAsync(factory);
        var (psychologistId, psychologistEmail, psychologistPassword) = await CreateUserWithDisplayNameAsync(
            factory,
            ApplicationRoles.Psychologist,
            "Morgan",
            "Professional");
        var (_, otherPsychologistEmail, otherPsychologistPassword) = await CreateUserWithDisplayNameAsync(
            factory,
            ApplicationRoles.Psychologist,
            "Taylor",
            "Unassigned");

        var adminIndex = await adminClient.GetAsync("/admin/referrals");
        Assert.Equal(HttpStatusCode.OK, adminIndex.StatusCode);
        var indexHtml = await adminIndex.Content.ReadAsStringAsync();
        Assert.Contains("Referral workflow", indexHtml);
        Assert.Contains("Showing 1–1 of 1 referrals", indexHtml);
        Assert.Contains("Referral Learner", indexHtml);
        Assert.DoesNotContain("confidential psychologist note", indexHtml);

        var adminDetails = await adminClient.GetAsync($"/admin/referrals/{referralId}");
        var adminDetailsHtml = await adminDetails.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, adminDetails.StatusCode);
        var assignToken = ExtractAntiForgeryToken(adminDetailsHtml);

        var rejectedAssignment = await adminClient.PostAsync(
            $"/admin/referrals/{referralId}/assign",
            BuildForm(assignToken, learnerId));
        Assert.Equal(HttpStatusCode.OK, rejectedAssignment.StatusCode);
        Assert.Contains("Choose an active Psychologist account", await rejectedAssignment.Content.ReadAsStringAsync());

        var assigned = await adminClient.PostAsync(
            $"/admin/referrals/{referralId}/assign",
            BuildForm(assignToken, psychologistId));
        Assert.Equal(HttpStatusCode.Redirect, assigned.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var referral = await dbContext.Referrals.FindAsync(referralId);
            Assert.NotNull(referral);
            Assert.Equal(psychologistId, referral.AssignedPsychologistId);
            Assert.Equal(ReferralStatus.Assigned, referral.Status);
            Assert.Contains(await dbContext.AppNotifications.ToListAsync(), notification =>
                notification.RecipientUserId == psychologistId && notification.Type == AppNotificationType.ReferralAssigned);
            Assert.Contains(await dbContext.AppNotifications.ToListAsync(), notification =>
                notification.RecipientUserId == learnerId && notification.Type == AppNotificationType.ReferralAssigned);
        }

        using var psychologistClient = CreateClient(factory);
        await SignInAsync(psychologistClient, psychologistEmail, psychologistPassword);
        var assignedList = await psychologistClient.GetAsync("/psychologist/referrals");
        var assignedListHtml = await assignedList.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, assignedList.StatusCode);
        Assert.Contains("Referral Learner", assignedListHtml);
        Assert.Contains("Assigned", assignedListHtml);

        var reviewPage = await psychologistClient.GetAsync($"/psychologist/referrals/{referralId}");
        Assert.Equal(HttpStatusCode.OK, reviewPage.StatusCode);
        var reviewHtml = await reviewPage.Content.ReadAsStringAsync();
        Assert.Contains("not a clinical diagnosis", reviewHtml);
        var reviewToken = ExtractAntiForgeryToken(reviewHtml);
        const string professionalNotes = "confidential psychologist note for support planning";

        var invalidTransition = await psychologistClient.PostAsync(
            $"/psychologist/referrals/{referralId}/review",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = reviewToken,
                ["Status"] = nameof(ReferralStatus.Completed),
                ["Notes"] = "This invalid transition must not be saved."
            }));
        Assert.Equal(HttpStatusCode.OK, invalidTransition.StatusCode);
        Assert.Contains("status change is not available", await invalidTransition.Content.ReadAsStringAsync());
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var referral = await dbContext.Referrals.FindAsync(referralId);
            Assert.NotNull(referral);
            Assert.Equal(ReferralStatus.Assigned, referral.Status);
            Assert.Null(referral.Notes);
        }

        var reviewed = await psychologistClient.PostAsync(
            $"/psychologist/referrals/{referralId}/review",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = reviewToken,
                ["Status"] = nameof(ReferralStatus.UnderReview),
                ["Notes"] = professionalNotes
            }));
        Assert.Equal(HttpStatusCode.Redirect, reviewed.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var referral = await dbContext.Referrals.FindAsync(referralId);
            Assert.NotNull(referral);
            Assert.Equal(ReferralStatus.UnderReview, referral.Status);
            Assert.Equal(professionalNotes, referral.Notes);
            Assert.NotNull(referral.ReviewedAtUtc);
        }

        var followUpRequested = await psychologistClient.PostAsync(
            $"/psychologist/referrals/{referralId}/review",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = reviewToken,
                ["Status"] = nameof(ReferralStatus.FollowUpRequested),
                ["Notes"] = professionalNotes
            }));
        Assert.Equal(HttpStatusCode.Redirect, followUpRequested.StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Contains(await dbContext.AppNotifications.ToListAsync(), notification =>
                notification.RecipientUserId == learnerId && notification.Type == AppNotificationType.ProfessionalFollowUpRequested);
        }

        using var otherPsychologistClient = CreateClient(factory);
        await SignInAsync(otherPsychologistClient, otherPsychologistEmail, otherPsychologistPassword);
        var unassignedDetails = await otherPsychologistClient.GetAsync($"/psychologist/referrals/{referralId}");
        Assert.Equal(HttpStatusCode.NotFound, unassignedDetails.StatusCode);
        Assert.DoesNotContain("Referral Learner", await (await otherPsychologistClient.GetAsync("/psychologist/referrals")).Content.ReadAsStringAsync());

    }

    [Fact]
    public async Task Learner_cannot_read_or_change_professional_referral_notes()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        var (learnerId, learnerEmail, learnerPassword, attemptId, referralId) = await CreateRedReferralAsync(factory);
        var (_, psychologistEmail, psychologistPassword) = await CreateUserWithDisplayNameAsync(
            factory,
            ApplicationRoles.Psychologist,
            "Pat",
            "Assigned");

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var referral = await dbContext.Referrals.FindAsync(referralId);
            Assert.NotNull(referral);
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var psychologist = await userManager.FindByEmailAsync(psychologistEmail);
            Assert.NotNull(psychologist);
            referral.AssignedPsychologistId = psychologist.Id;
            referral.Status = ReferralStatus.Assigned;
            await dbContext.SaveChangesAsync();
        }

        using var psychologistClient = CreateClient(factory);
        await SignInAsync(psychologistClient, psychologistEmail, psychologistPassword);
        var page = await psychologistClient.GetAsync($"/psychologist/referrals/{referralId}");
        var token = ExtractAntiForgeryToken(await page.Content.ReadAsStringAsync());
        const string professionalNotes = "confidential psychologist note for support planning";
        var save = await psychologistClient.PostAsync(
            $"/psychologist/referrals/{referralId}/review",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["Status"] = nameof(ReferralStatus.UnderReview),
                ["Notes"] = professionalNotes
            }));
        Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);

        using var learnerClient = CreateClient(factory);
        await SignInAsync(learnerClient, learnerEmail, learnerPassword);

        var deniedDetails = await learnerClient.GetAsync($"/psychologist/referrals/{referralId}");
        Assert.Equal(HttpStatusCode.Redirect, deniedDetails.StatusCode);
        Assert.StartsWith("/Account/AccessDenied", GetLocationPath(deniedDetails));

        var resultPage = await learnerClient.GetAsync($"/screening/results/{attemptId}");
        Assert.Equal(HttpStatusCode.OK, resultPage.StatusCode);
        var resultHtml = await resultPage.Content.ReadAsStringAsync();
        Assert.DoesNotContain(professionalNotes, resultHtml);

        var unauthorizedEdit = await learnerClient.PostAsync(
            $"/psychologist/referrals/{referralId}/review",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiForgeryToken(resultHtml),
                ["Status"] = nameof(ReferralStatus.Completed),
                ["Notes"] = "learner attempt to alter notes"
            }));
        Assert.Equal(HttpStatusCode.Redirect, unauthorizedEdit.StatusCode);
        Assert.StartsWith("/Account/AccessDenied", GetLocationPath(unauthorizedEdit));

        using var verifyScope = factory.Services.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var savedReferral = await verifyContext.Referrals.FindAsync(referralId);
        Assert.NotNull(savedReferral);
        Assert.Equal(professionalNotes, savedReferral.Notes);
        Assert.Equal(ReferralStatus.UnderReview, savedReferral.Status);
    }

    [Fact]
    public async Task Administrator_referral_index_uses_five_row_pages_cat_time_and_stable_ordering()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        using var adminClient = CreateClient(factory);
        var (adminEmail, adminPassword) = await factory.CreateUserAsync(ApplicationRoles.Administrator);
        await SignInAsync(adminClient, adminEmail, adminPassword);

        for (var index = 0; index < 12; index++)
        {
            await CreateRedReferralAsync(factory, "Page", $"Learner {index}");
        }

        var firstPage = await adminClient.GetAsync("/admin/referrals?search=Page&page=1");
        var firstPageHtml = await firstPage.Content.ReadAsStringAsync();
        var finalPage = await adminClient.GetAsync("/admin/referrals?search=Page&page=999");
        var finalPageHtml = await finalPage.Content.ReadAsStringAsync();
        var malformedPage = await adminClient.GetAsync("/admin/referrals?search=Page&page=not-a-number");
        var malformedPageHtml = await malformedPage.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, firstPage.StatusCode);
        Assert.Equal(5, Regex.Matches(firstPageHtml, "admin-referrals-row").Count);
        Assert.Contains("Showing 1–5 of 12 referrals", firstPageHtml);
        Assert.Contains("Page Learner 11", firstPageHtml);
        Assert.Contains("2026-08-18 12:00 CAT", firstPageHtml);
        Assert.Contains("aria-current=\"page\">1</span>", firstPageHtml);
        Assert.Contains("page=2", firstPageHtml);

        Assert.Equal(HttpStatusCode.OK, finalPage.StatusCode);
        Assert.Equal(2, Regex.Matches(finalPageHtml, "admin-referrals-row").Count);
        Assert.Contains("Showing 11–12 of 12 referrals", finalPageHtml);
        Assert.Contains("Page Learner 0", finalPageHtml);
        Assert.Contains("aria-current=\"page\">3</span>", finalPageHtml);
        Assert.Contains("aria-disabled=\"true\">Next</span>", finalPageHtml);

        Assert.Equal(HttpStatusCode.OK, malformedPage.StatusCode);
        Assert.Contains("Showing 1–5 of 12 referrals", malformedPageHtml);
    }

    [Fact]
    public async Task Administrator_referral_index_searches_name_and_email_filters_status_and_preserves_pager_state()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        using var adminClient = CreateClient(factory);
        var (adminEmail, adminPassword) = await factory.CreateUserAsync(ApplicationRoles.Administrator);
        await SignInAsync(adminClient, adminEmail, adminPassword);

        var (_, redEmail, _, _, _) = await CreateRedReferralAsync(factory, "Red", "Learner One");
        await CreateRedReferralAsync(factory, "Red", "Learner Two");
        for (var index = 0; index < 5; index++)
        {
            await CreateRedReferralAsync(factory, "Red", $"Learner {index + 3}");
        }

        var (_, _, _, _, blueReferralId) = await CreateRedReferralAsync(factory, "Blue", "Learner");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var blueReferral = await db.Referrals.SingleAsync(referral => referral.Id == blueReferralId);
            blueReferral.Status = ReferralStatus.UnderReview;
            await db.SaveChangesAsync();
        }

        var nameSearchHtml = await (await adminClient.GetAsync("/admin/referrals?search=Red%20Learner%20One")).Content.ReadAsStringAsync();
        var emailSearchHtml = await (await adminClient.GetAsync($"/admin/referrals?search={Uri.EscapeDataString(redEmail)}")).Content.ReadAsStringAsync();
        var statusHtml = await (await adminClient.GetAsync("/admin/referrals?status=UnderReview")).Content.ReadAsStringAsync();
        var filteredPage = await adminClient.GetAsync("/admin/referrals?search=Red&status=Pending&page=2");
        var filteredPageHtml = await filteredPage.Content.ReadAsStringAsync();
        var emptyHtml = await (await adminClient.GetAsync("/admin/referrals?search=does-not-exist")).Content.ReadAsStringAsync();

        Assert.Contains("Red Learner One", nameSearchHtml);
        var learnerCellHtml = Regex.Match(nameSearchHtml, """<td class="admin-referrals-learner">.*?</td>""", RegexOptions.Singleline).Value;
        Assert.Contains(redEmail, learnerCellHtml);
        Assert.Contains("admin-referrals-learner-email", learnerCellHtml);
        Assert.DoesNotContain("Blue Learner", nameSearchHtml);
        Assert.Contains("Red Learner One", emailSearchHtml);
        Assert.Contains("Blue Learner", statusHtml);
        Assert.Contains("Under review", statusHtml);
        Assert.Contains("Showing 6–7 of 7 referrals", filteredPageHtml);
        Assert.Contains("value=\"Red\"", filteredPageHtml);
        Assert.Contains("status=Pending", filteredPageHtml);
        Assert.Contains("search=Red", filteredPageHtml);
        Assert.Contains("page=1", filteredPageHtml);
        Assert.Contains("No referrals match your current filters.", emptyHtml);
        Assert.Contains("Clear filters", emptyHtml);
        Assert.DoesNotContain("admin-referrals-row", emptyHtml);
    }

    private static async Task<(string LearnerId, string Email, string Password, int AttemptId, int ReferralId)> CreateRedReferralAsync(
        LexiAssistWebApplicationFactory factory,
        string firstName = "Referral",
        string lastName = "Learner")
    {
        var (email, password) = await factory.CreateUserAsync(ApplicationRoles.Learner);
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var learner = await userManager.FindByEmailAsync(email);
        Assert.NotNull(learner);
        learner.FirstName = firstName;
        learner.LastName = lastName;
        var updateResult = await userManager.UpdateAsync(learner);
        Assert.True(updateResult.Succeeded, string.Join("; ", updateResult.Errors.Select(error => error.Description)));

        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var completedAtUtc = new DateTime(2026, 8, 18, 10, 0, 0, DateTimeKind.Utc);
        var attempt = new ScreeningAttempt
        {
            UserId = learner.Id,
            StartedAtUtc = completedAtUtc.AddMinutes(-30),
            CompletedAtUtc = completedAtUtc,
            Status = ScreeningAttemptStatus.Completed,
            RiskTier = "Red"
        };
        dbContext.ScreeningAttempts.Add(attempt);
        await dbContext.SaveChangesAsync();

        var report = new ScreeningReport
        {
            ScreeningAttemptId = attempt.Id,
            UserId = learner.Id,
            GeneratedAtUtc = completedAtUtc,
            RiskTier = "Red",
            Summary = "Educational screening summary. This is not a clinical diagnosis.",
            FlaggedAreasJson = "[]",
            ScoringVersion = "test-v1",
            IsReferralRecommended = true,
            SkillAreasJson = "[]",
            RecommendedNextAction = "A suitable support professional may review this screening."
        };
        dbContext.ScreeningReports.Add(report);
        await dbContext.SaveChangesAsync();

        var referral = new Referral
        {
            ScreeningReportId = report.Id,
            LearnerId = learner.Id,
            Status = ReferralStatus.Pending,
            CreatedAtUtc = completedAtUtc
        };
        dbContext.Referrals.Add(referral);
        await dbContext.SaveChangesAsync();
        return (learner.Id, email, password, attempt.Id, referral.Id);
    }

    private static async Task<(string Id, string Email, string Password)> CreateUserWithDisplayNameAsync(
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
        await userManager.UpdateAsync(user);
        return (user.Id, email, password);
    }

    private static HttpClient CreateClient(LexiAssistWebApplicationFactory factory) =>
        factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

    private static FormUrlEncodedContent BuildForm(string token, string psychologistId) => new(new Dictionary<string, string>
    {
        ["__RequestVerificationToken"] = token,
        ["PsychologistId"] = psychologistId
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
