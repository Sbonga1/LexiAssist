using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Identity;
using LexiAssist.Web.Models.Notifications;
using LexiAssist.Web.Models.Screening;
using LexiAssist.Web.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LexiAssist.Tests;

public sealed class NotificationWorkflowTests
{
    [Fact]
    public async Task Completing_red_screening_notifies_learner_and_administrators_of_the_referral()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        var (learnerEmail, learnerPassword) = await factory.CreateUserAsync(ApplicationRoles.Learner);
        var (administratorEmail, administratorPassword) = await factory.CreateUserAsync(ApplicationRoles.Administrator);
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.ScreeningQuestions.Add(new ScreeningQuestion
            {
                ScreeningTestId = 1,
                Prompt = "DEMO: choose the correct answer",
                QuestionType = ScreeningQuestionType.MultipleChoice,
                CorrectAnswer = "right",
                SortOrder = 1,
                MetadataJson = JsonSerializer.Serialize(new ScreeningQuestionMetadata
                {
                    ChoiceOptions = ["right", "wrong"]
                })
            });
            await dbContext.SaveChangesAsync();
        }

        using var learnerClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(learnerClient, learnerEmail, learnerPassword);
        var startPage = await learnerClient.GetAsync("/screening/start");
        var startToken = ExtractAntiForgeryToken(await startPage.Content.ReadAsStringAsync());
        var start = await learnerClient.PostAsync(
            "/screening/start",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = startToken
            }));
        Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);

        var questionPage = await learnerClient.GetAsync("/screening/resume");
        var questionHtml = await questionPage.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, questionPage.StatusCode);
        var answer = await learnerClient.PostAsync(
            "/screening/respond",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiForgeryToken(questionHtml),
                ["QuestionToken"] = ExtractInputValue(questionHtml, "QuestionToken"),
                ["SelectedAnswer"] = "wrong"
            }));
        Assert.Equal(HttpStatusCode.Redirect, answer.StatusCode);

        var completion = await learnerClient.GetAsync("/screening/resume");
        Assert.Equal(HttpStatusCode.Redirect, completion.StatusCode);
        Assert.StartsWith("/screening/results/", completion.Headers.Location?.OriginalString);

        var resultPage = await learnerClient.GetAsync(completion.Headers.Location!.OriginalString!);
        Assert.Equal(HttpStatusCode.OK, resultPage.StatusCode);
        Assert.Contains("Notifications, 2 unread", await resultPage.Content.ReadAsStringAsync());

        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>();
            var learner = await userManager.FindByEmailAsync(learnerEmail);
            var administrator = await userManager.FindByEmailAsync(administratorEmail);
            Assert.NotNull(learner);
            Assert.NotNull(administrator);

            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var attempt = await dbContext.ScreeningAttempts.SingleAsync(item => item.UserId == learner.Id);
            Assert.Equal("Red", attempt.RiskTier);
            Assert.True(await dbContext.Referrals.AnyAsync(item => item.LearnerId == learner.Id));
            var learnerNotifications = await dbContext.AppNotifications
                .Where(notification => notification.RecipientUserId == learner.Id)
                .Select(notification => notification.Type)
                .ToListAsync();
            Assert.Contains(AppNotificationType.ScreeningCompleted, learnerNotifications);
            Assert.Contains(AppNotificationType.ReferralCreated, learnerNotifications);
            Assert.Contains(await dbContext.AppNotifications.ToListAsync(), notification =>
                notification.RecipientUserId == administrator.Id && notification.Type == AppNotificationType.ReferralCreated);
        }

        using var administratorClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(administratorClient, administratorEmail, administratorPassword);
        var administratorNotifications = await administratorClient.GetAsync("/Notifications");
        Assert.Equal(HttpStatusCode.OK, administratorNotifications.StatusCode);
        Assert.Contains("Referral awaiting assignment", await administratorNotifications.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Notifications_are_private_marked_read_and_include_learning_activity_support()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        var (recipientEmail, recipientPassword) = await factory.CreateUserAsync(ApplicationRoles.Learner);
        var (otherEmail, _) = await factory.CreateUserAsync(ApplicationRoles.Learner);
        int screeningNotificationId;
        int activityNotificationId;
        int otherNotificationId;

        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>();
            var recipient = await userManager.FindByEmailAsync(recipientEmail);
            var other = await userManager.FindByEmailAsync(otherEmail);
            Assert.NotNull(recipient);
            Assert.NotNull(other);

            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var notificationService = scope.ServiceProvider.GetRequiredService<AppNotificationService>();
            var screeningNotification = notificationService.Add(
                recipient.Id,
                AppNotificationType.ScreeningCompleted,
                "Screening completed",
                "Your screening result is ready.",
                "/screening/results/42");
            var activityNotification = notificationService.AddLearningActivityAssigned(recipient.Id, "DEMO practice activity");
            var otherNotification = notificationService.Add(
                other.Id,
                AppNotificationType.ReferralAssigned,
                "Private update",
                "This belongs to another account.");
            await dbContext.SaveChangesAsync();
            screeningNotificationId = screeningNotification.Id;
            activityNotificationId = activityNotification.Id;
            otherNotificationId = otherNotification.Id;
        }

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(client, recipientEmail, recipientPassword);

        var page = await client.GetAsync("/Notifications");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("Screening completed", html);
        Assert.Contains("Learning activity assigned", html);
        Assert.Contains("DEMO practice activity", html);
        Assert.Contains("Notifications, 2 unread", html);
        Assert.DoesNotContain("Private update", html);

        var token = ExtractAntiForgeryToken(html);
        var open = await client.PostAsync(
            $"/Notifications/Open/{screeningNotificationId}",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token
            }));
        Assert.Equal(HttpStatusCode.Redirect, open.StatusCode);
        Assert.Equal("/screening/results/42", open.Headers.Location?.OriginalString);

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var savedReadNotification = await dbContext.AppNotifications.FindAsync(screeningNotificationId);
            var untouchedActivityNotification = await dbContext.AppNotifications.FindAsync(activityNotificationId);
            Assert.NotNull(savedReadNotification);
            Assert.NotNull(untouchedActivityNotification);
            Assert.NotNull(savedReadNotification.ReadAtUtc);
            Assert.Null(untouchedActivityNotification.ReadAtUtc);
        }

        var privateOpen = await client.PostAsync(
            $"/Notifications/Open/{otherNotificationId}",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token
            }));
        Assert.Equal(HttpStatusCode.NotFound, privateOpen.StatusCode);

        var refreshed = await client.GetAsync("/Notifications");
        Assert.Contains("Notifications, 1 unread", await refreshed.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Notifications_are_paginated_newest_first_and_display_cat_times()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        var (email, password) = await factory.CreateUserAsync(ApplicationRoles.Learner);

        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>();
            var user = await userManager.FindByEmailAsync(email);
            Assert.NotNull(user);

            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            for (var day = 1; day <= 12; day++)
            {
                dbContext.AppNotifications.Add(new AppNotification
                {
                    RecipientUserId = user.Id,
                    Type = AppNotificationType.ScreeningCompleted,
                    Title = $"Notification {day}",
                    Message = $"Message {day}",
                    CreatedAtUtc = new DateTime(2026, 9, day, 16, 29, 21, DateTimeKind.Utc)
                });
            }

            await dbContext.SaveChangesAsync();
        }

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(client, email, password);

        var firstPage = await client.GetAsync("/Notifications?page=1");
        var firstHtml = await firstPage.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, firstPage.StatusCode);
        Assert.Contains("Showing 1–5 of 12 notifications.", firstHtml);
        Assert.Contains("Notification 12", firstHtml);
        Assert.Contains("Notification 8", firstHtml);
        Assert.DoesNotContain("Notification 7", firstHtml);
        Assert.Contains("2026-09-12 18:29:21 CAT", firstHtml);
        Assert.Contains("aria-disabled=\"true\">Previous", firstHtml);

        var secondPage = await client.GetAsync("/Notifications?page=2");
        var secondHtml = await secondPage.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, secondPage.StatusCode);
        Assert.Contains("Showing 6–10 of 12 notifications.", secondHtml);
        Assert.Contains("Notification 7", secondHtml);
        Assert.DoesNotContain("Notification 12", secondHtml);
        Assert.Contains("aria-current=\"page\">2", secondHtml);

        var lastPage = await client.GetAsync("/Notifications?page=999");
        var lastHtml = await lastPage.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, lastPage.StatusCode);
        Assert.Contains("Showing 11–12 of 12 notifications.", lastHtml);
        Assert.Contains("Notification 1", lastHtml);
        Assert.Contains("Notification 2", lastHtml);
        Assert.Contains("aria-disabled=\"true\">Next", lastHtml);
    }

    [Fact]
    public async Task Invalid_notification_pages_are_normalized_without_error()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        var (email, password) = await factory.CreateUserAsync(ApplicationRoles.Learner);

        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>();
            var user = await userManager.FindByEmailAsync(email);
            Assert.NotNull(user);
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            for (var day = 1; day <= 6; day++)
            {
                dbContext.AppNotifications.Add(new AppNotification
                {
                    RecipientUserId = user.Id,
                    Type = AppNotificationType.ReferralCreated,
                    Title = $"Update {day}",
                    Message = "A notification update.",
                    CreatedAtUtc = new DateTime(2026, 9, day, 12, 0, 0, DateTimeKind.Utc)
                });
            }

            await dbContext.SaveChangesAsync();
        }

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(client, email, password);

        foreach (var invalidPage in new[] { "0", "-1", "not-a-number" })
        {
            var response = await client.GetAsync($"/Notifications?page={invalidPage}");
            var html = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("Showing 1–5 of 6 notifications.", html);
        }
    }

    [Fact]
    public async Task Anonymous_users_cannot_view_notifications()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/Notifications");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/Login", GetLocationPath(response));
    }

    [Fact]
    public async Task Notification_links_reject_encoded_redirect_targets()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        var (email, _) = await factory.CreateUserAsync(ApplicationRoles.Learner);

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.NotNull(user);

        var service = scope.ServiceProvider.GetRequiredService<AppNotificationService>();
        Assert.Throws<ArgumentException>(() => service.Add(
            user.Id,
            AppNotificationType.ScreeningCompleted,
            "Screening completed",
            "Your screening result is ready.",
            "/%2f%2fevil.example"));
    }

    private static async Task SignInAsync(HttpClient client, string email, string password)
    {
        var loginPage = await client.GetAsync("/Account/Login");
        var token = ExtractAntiForgeryToken(await loginPage.Content.ReadAsStringAsync());
        var response = await client.PostAsync(
            "/Account/Login",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["Email"] = email,
                ["Password"] = password,
                ["RememberMe"] = "false"
            }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private static string ExtractAntiForgeryToken(string html)
    {
        var match = Regex.Match(html, "<input[^>]*name=\\\"__RequestVerificationToken\\\"[^>]*value=\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase);
        Assert.True(match.Success, "The rendered page should include an antiforgery token.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static string ExtractInputValue(string html, string name)
    {
        var match = Regex.Match(html, $"<input[^>]*name=\\\"{Regex.Escape(name)}\\\"[^>]*value=\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase);
        Assert.True(match.Success, $"The rendered page should include {name}.");
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
