using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Activities;
using LexiAssist.Web.Models.Identity;
using LexiAssist.Web.Models.Referrals;
using LexiAssist.Web.Models.Reports;
using LexiAssist.Web.Models.Screening;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LexiAssist.Tests;

public sealed class LearningActivityAssignmentTests
{
    [Fact]
    public async Task Recommendations_are_read_only_and_admin_or_assigned_psychologist_can_manually_assign()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        var learnerCredentials = await factory.CreateUserAsync(ApplicationRoles.Learner);
        var adminCredentials = await factory.CreateUserAsync(ApplicationRoles.Administrator);
        var psychologistCredentials = await factory.CreateUserAsync(ApplicationRoles.Psychologist);
        var unrelatedPsychologistCredentials = await factory.CreateUserAsync(ApplicationRoles.Psychologist);
        var (learnerId, adminId, psychologistId, unrelatedPsychologistId, reportId, soundBuilderId, sayWriteMatchId) =
            await SeedScenarioAsync(factory, learnerCredentials.Email, adminCredentials.Email, psychologistCredentials.Email, unrelatedPsychologistCredentials.Email);

        using var adminClient = CreateClient(factory);
        await SignInAsync(adminClient, adminCredentials.Email, adminCredentials.Password);
        var recommendationPage = await adminClient.GetAsync($"/activity-assignments/create/{reportId}");
        Assert.Equal(HttpStatusCode.OK, recommendationPage.StatusCode);
        var recommendationHtml = await recommendationPage.Content.ReadAsStringAsync();
        Assert.Contains("Sound Builder", recommendationHtml);
        Assert.Contains("Word Reader", recommendationHtml);
        Assert.Contains("Syllable Slicer", recommendationHtml);
        Assert.DoesNotContain("Fast Name Race", recommendationHtml);
        Assert.Contains("recommendation never creates or changes an assignment", recommendationHtml);
        Assert.Equal(0, await GetAssignmentCountAsync(factory, reportId));

        var manualAssignment = await adminClient.PostAsync(
            $"/activity-assignments/create/{reportId}",
            BuildAssignmentForm(recommendationHtml, sayWriteMatchId));
        Assert.Equal(HttpStatusCode.Redirect, manualAssignment.StatusCode);
        var firstAssignmentId = await GetAssignmentIdAsync(factory, reportId, sayWriteMatchId);
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var assignment = await dbContext.ActivityAssignments.SingleAsync(item => item.Id == firstAssignmentId);
            Assert.Equal(learnerId, assignment.LearnerId);
            Assert.Equal(adminId, assignment.AssignedByUserId);
            Assert.Equal(reportId, assignment.RelatedScreeningReportId);
            Assert.Equal(ActivityAssignmentStatus.Assigned, assignment.Status);
        }

        var duplicateAssignment = await adminClient.PostAsync(
            $"/activity-assignments/create/{reportId}",
            BuildAssignmentForm(await GetHtmlAsync(adminClient, $"/activity-assignments/create/{reportId}"), sayWriteMatchId));
        Assert.Equal(HttpStatusCode.OK, duplicateAssignment.StatusCode);
        Assert.Contains("already assigned", await duplicateAssignment.Content.ReadAsStringAsync());
        Assert.Equal(1, await GetAssignmentCountAsync(factory, reportId));

        using var psychologistClient = CreateClient(factory);
        await SignInAsync(psychologistClient, psychologistCredentials.Email, psychologistCredentials.Password);
        var psychologistPage = await psychologistClient.GetAsync($"/activity-assignments/create/{reportId}");
        Assert.Equal(HttpStatusCode.OK, psychologistPage.StatusCode);
        var psychologistHtml = await psychologistPage.Content.ReadAsStringAsync();
        var psychologistAssignment = await psychologistClient.PostAsync(
            $"/activity-assignments/create/{reportId}",
            BuildAssignmentForm(psychologistHtml, soundBuilderId));
        Assert.Equal(HttpStatusCode.Redirect, psychologistAssignment.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var assignments = await dbContext.ActivityAssignments
                .Where(item => item.RelatedScreeningReportId == reportId)
                .OrderBy(item => item.Id)
                .ToListAsync();
            Assert.Equal(2, assignments.Count);
            Assert.Equal(adminId, assignments[0].AssignedByUserId);
            Assert.Equal(psychologistId, assignments[1].AssignedByUserId);
            Assert.All(assignments, assignment => Assert.Equal(ActivityAssignmentStatus.Assigned, assignment.Status));
        }

        using var unrelatedPsychologistClient = CreateClient(factory);
        await SignInAsync(
            unrelatedPsychologistClient,
            unrelatedPsychologistCredentials.Email,
            unrelatedPsychologistCredentials.Password);
        var unauthorizedPage = await unrelatedPsychologistClient.GetAsync($"/activity-assignments/create/{reportId}");
        Assert.Equal(HttpStatusCode.NotFound, unauthorizedPage.StatusCode);

        using var learnerClient = CreateClient(factory);
        await SignInAsync(learnerClient, learnerCredentials.Email, learnerCredentials.Password);
        var learnerPage = await learnerClient.GetAsync($"/activity-assignments/create/{reportId}");
        Assert.Equal(HttpStatusCode.Redirect, learnerPage.StatusCode);
        Assert.StartsWith("/Account/AccessDenied", GetLocationPath(learnerPage));
        Assert.NotEqual(unrelatedPsychologistId, psychologistId);
    }

    private static async Task<(string LearnerId, string AdminId, string PsychologistId, string UnrelatedPsychologistId, int ReportId, int SoundBuilderId, int SayWriteMatchId)> SeedScenarioAsync(
        LexiAssistWebApplicationFactory factory,
        string learnerEmail,
        string adminEmail,
        string psychologistEmail,
        string unrelatedPsychologistEmail)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var learner = await userManager.FindByEmailAsync(learnerEmail);
        var administrator = await userManager.FindByEmailAsync(adminEmail);
        var psychologist = await userManager.FindByEmailAsync(psychologistEmail);
        var unrelatedPsychologist = await userManager.FindByEmailAsync(unrelatedPsychologistEmail);
        Assert.NotNull(learner);
        Assert.NotNull(administrator);
        Assert.NotNull(psychologist);
        Assert.NotNull(unrelatedPsychologist);
        learner.FirstName = "Robin";
        learner.LastName = "Learner";
        Assert.True((await userManager.UpdateAsync(learner)).Succeeded);

        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var activities = new[]
        {
            new LearningActivity
            {
                Name = "Sound Builder",
                ActivityType = LearningActivityType.PhonologicalAwareness,
                Description = "Practice listening for and working with sounds.",
                Instructions = "DEMO activity instructions.",
                IsActive = true
            },
            new LearningActivity
            {
                Name = "Word Reader",
                ActivityType = LearningActivityType.Reading,
                Description = "Practice reading familiar and unfamiliar words.",
                Instructions = "DEMO activity instructions.",
                IsActive = true
            },
            new LearningActivity
            {
                Name = "Fast Name Race",
                ActivityType = LearningActivityType.Fluency,
                Description = "Configured but inactive for this test.",
                Instructions = "DEMO activity instructions.",
                IsActive = false
            },
        };
        var seededFastNameRace = await dbContext.LearningActivities.SingleAsync(activity => activity.Name == "Fast Name Race");
        seededFastNameRace.IsActive = false;
        var seededWordReader = await dbContext.LearningActivities.SingleAsync(activity => activity.Name == "Word Reader");
        seededWordReader.IsActive = false;
        var sayWriteMatch = await dbContext.LearningActivities.SingleAsync(activity => activity.Name == "Say-Write-Match");
        dbContext.LearningActivities.AddRange(activities);

        var now = DateTime.UtcNow;
        var attempt = new ScreeningAttempt
        {
            UserId = learner.Id,
            StartedAtUtc = now.AddMinutes(-10),
            CompletedAtUtc = now,
            Status = ScreeningAttemptStatus.Completed,
            OverallScore = 0,
            RiskTier = "Red"
        };
        dbContext.ScreeningAttempts.Add(attempt);
        await dbContext.SaveChangesAsync();

        var question = new ScreeningQuestion
        {
            ScreeningTestId = 1,
            Prompt = "DEMO tagged response item",
            QuestionType = ScreeningQuestionType.TextResponse,
            SortOrder = 1
        };
        dbContext.ScreeningQuestions.Add(question);
        await dbContext.SaveChangesAsync();

        var testAttempt = new ScreeningTestAttempt
        {
            ScreeningAttemptId = attempt.Id,
            ScreeningTestId = 1,
            StartedAtUtc = attempt.StartedAtUtc,
            CompletedAtUtc = now
        };
        dbContext.ScreeningTestAttempts.Add(testAttempt);
        await dbContext.SaveChangesAsync();

        var response = new ScreeningResponse
        {
            ScreeningTestAttemptId = testAttempt.Id,
            ScreeningQuestionId = question.Id,
            TextAnswer = "DEMO response",
            CreatedAtUtc = now
        };
        dbContext.ScreeningResponses.Add(response);
        await dbContext.SaveChangesAsync();
        dbContext.ScreeningResponseErrorCategories.Add(new ScreeningResponseErrorCategory
        {
            ScreeningResponseId = response.Id,
            Category = ScreeningErrorCategory.MultiSyllabic
        });

        var report = new ScreeningReport
        {
            ScreeningAttemptId = attempt.Id,
            UserId = learner.Id,
            GeneratedAtUtc = now,
            RiskTier = "Red",
            Summary = "DEMO saved report summary.",
            FlaggedAreasJson = JsonSerializer.Serialize(new[]
            {
                new ScreeningReportFlaggedAreaSnapshot("Phonological Awareness", 0m),
                new ScreeningReportFlaggedAreaSnapshot("Word Reading", 0m),
                new ScreeningReportFlaggedAreaSnapshot("Rapid Naming", 0m)
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            ScoringVersion = "test",
            IsReferralRecommended = true,
            SkillAreasJson = "[]",
            RecommendedNextAction = "DEMO next step."
        };
        dbContext.ScreeningReports.Add(report);
        await dbContext.SaveChangesAsync();
        dbContext.Referrals.Add(new Referral
        {
            ScreeningReportId = report.Id,
            LearnerId = learner.Id,
            AssignedPsychologistId = psychologist.Id,
            Status = ReferralStatus.Assigned,
            CreatedAtUtc = now
        });
        await dbContext.SaveChangesAsync();

        return (
            learner.Id,
            administrator.Id,
            psychologist.Id,
            unrelatedPsychologist.Id,
            report.Id,
            activities.Single(activity => activity.Name == "Sound Builder").Id,
            sayWriteMatch.Id);
    }

    private static HttpClient CreateClient(LexiAssistWebApplicationFactory factory) =>
        factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

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

    private static FormUrlEncodedContent BuildAssignmentForm(string html, int activityId) =>
        new(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractInputValue(html, "__RequestVerificationToken"),
            ["LearningActivityId"] = activityId.ToString()
        });

    private static async Task<string> GetHtmlAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private static async Task<int> GetAssignmentCountAsync(LexiAssistWebApplicationFactory factory, int reportId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await dbContext.ActivityAssignments.CountAsync(item => item.RelatedScreeningReportId == reportId);
    }

    private static async Task<int> GetAssignmentIdAsync(
        LexiAssistWebApplicationFactory factory,
        int reportId,
        int activityId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await dbContext.ActivityAssignments
            .Where(item => item.RelatedScreeningReportId == reportId && item.LearningActivityId == activityId)
            .Select(item => item.Id)
            .SingleAsync();
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
