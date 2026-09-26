using System.Net;
using System.Text.RegularExpressions;
using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LexiAssist.Tests;

public sealed class AccessibilityPreferencesTests
{
    [Fact]
    public async Task Preferences_are_account_specific_and_survive_logout_and_login()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        using var learnerClient = CreateClient(factory);
        var (email, password) = await factory.CreateUserAsync(ApplicationRoles.Learner);

        var protectedPage = await learnerClient.GetAsync("/Settings/Accessibility");
        Assert.Equal(HttpStatusCode.Redirect, protectedPage.StatusCode);
        Assert.StartsWith("/Account/Login", GetLocationPath(protectedPage));

        await SignInAsync(learnerClient, email, password);
        var settingsPage = await learnerClient.GetAsync("/Settings/Accessibility");
        Assert.Equal(HttpStatusCode.OK, settingsPage.StatusCode);
        var settingsHtml = await settingsPage.Content.ReadAsStringAsync();
        var saveResult = await learnerClient.PostAsync(
            "/Settings/Accessibility",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiForgeryToken(settingsHtml),
                ["__Invariant"] = "FontScale",
                ["UserId"] = "another-account-id",
                ["BackgroundTheme"] = "warm-cream",
                ["FontScale"] = "1.2",
                ["IncreasedLineSpacing"] = "true",
                ["ReduceMotion"] = "true"
            }));

        Assert.True(saveResult.StatusCode == HttpStatusCode.Redirect,
            $"Expected a redirect but received {(int)saveResult.StatusCode}: {await saveResult.Content.ReadAsStringAsync()}");
        Assert.Equal("/Settings/Accessibility", saveResult.Headers.Location?.OriginalString);

        var dashboardBeforeLogout = await learnerClient.GetAsync("/Dashboard/Learner");
        var firstDashboardHtml = await dashboardBeforeLogout.Content.ReadAsStringAsync();
        AssertPreferenceAttributes(firstDashboardHtml);

        var logoutResult = await learnerClient.PostAsync(
            "/Account/Logout",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiForgeryToken(firstDashboardHtml)
            }));
        Assert.Equal(HttpStatusCode.Redirect, logoutResult.StatusCode);
        await SignInAsync(learnerClient, email, password);

        var dashboardAfterLogin = await learnerClient.GetAsync("/Dashboard/Learner");
        AssertPreferenceAttributes(await dashboardAfterLogin.Content.ReadAsStringAsync());

        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var learner = await userManager.FindByEmailAsync(email);
            Assert.NotNull(learner);

            var storedPreference = await dbContext.UserAccessibilityPreferences
                .SingleAsync(preference => preference.UserId == learner.Id);
            Assert.Equal("warm-cream", storedPreference.BackgroundTheme);
            Assert.Equal(1.2, storedPreference.FontScale);
            Assert.True(storedPreference.IncreasedLineSpacing);
            Assert.True(storedPreference.ReduceMotion);
            Assert.True(storedPreference.CreatedAtUtc <= storedPreference.UpdatedAtUtc);
        }

        var (anotherEmail, anotherPassword) = await factory.CreateUserAsync(ApplicationRoles.Learner);
        using var anotherClient = CreateClient(factory);
        await SignInAsync(anotherClient, anotherEmail, anotherPassword);

        var anotherDashboard = await anotherClient.GetAsync("/Dashboard/Learner");
        var anotherDashboardHtml = await anotherDashboard.Content.ReadAsStringAsync();
        Assert.Contains("data-reading-background=\"standard-light\"", anotherDashboardHtml);
        Assert.Contains("data-font-scale=\"1\"", anotherDashboardHtml);
        Assert.Contains("data-increased-line-spacing=\"false\"", anotherDashboardHtml);
        Assert.Contains("data-reduce-motion=\"false\"", anotherDashboardHtml);
    }

    [Fact]
    public async Task Unsupported_theme_and_font_scale_are_rejected()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        using var client = CreateClient(factory);
        var (email, password) = await factory.CreateUserAsync(ApplicationRoles.Learner);
        await SignInAsync(client, email, password);

        var settingsPage = await client.GetAsync("/Settings/Accessibility");
        var html = await settingsPage.Content.ReadAsStringAsync();
        using (var initialScope = factory.Services.CreateScope())
        {
            var initialDb = initialScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Empty(await initialDb.UserAccessibilityPreferences.ToListAsync());
        }

        var result = await client.PostAsync(
            "/Settings/Accessibility",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiForgeryToken(html),
                ["__Invariant"] = "FontScale",
                ["BackgroundTheme"] = "unlisted-theme",
                ["FontScale"] = "2.0"
            }));

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var responseHtml = await result.Content.ReadAsStringAsync();
        Assert.Contains("Choose one of the available reading backgrounds", responseHtml);
        Assert.Contains("Font size must be between 90% and 130%", responseHtml);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await dbContext.UserAccessibilityPreferences.ToListAsync());
    }

    [Fact]
    public async Task Administrator_accessibility_settings_stays_inside_the_admin_shell()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        using var client = CreateClient(factory);
        var (email, password) = await factory.CreateUserAsync(ApplicationRoles.Administrator);
        await SignInAsync(client, email, password, "/Dashboard/Administrator");

        var settingsPage = await client.GetAsync("/Settings/Accessibility");

        Assert.Equal(HttpStatusCode.OK, settingsPage.StatusCode);
        var html = await settingsPage.Content.ReadAsStringAsync();
        Assert.Contains("<body class=\"admin-shell\">", html);
        Assert.Contains("aria-label=\"Administrator navigation\"", html);
        Assert.Contains("class=\"admin-topbar-title\"", html);
        Assert.Contains("Reading and accessibility", html);
        Assert.Contains("class=\"admin-nav-link is-active\" href=\"/Settings/Accessibility\"", html);
        Assert.DoesNotContain("class=\"site-header\"", html);
    }

    private static HttpClient CreateClient(LexiAssistWebApplicationFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static async Task SignInAsync(
        HttpClient client,
        string email,
        string password,
        string expectedRedirect = "/Dashboard/Learner")
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
                ["RememberMe"] = "true"
            }));

        Assert.Equal(HttpStatusCode.Redirect, result.StatusCode);
        Assert.Equal(expectedRedirect, result.Headers.Location?.OriginalString);
    }

    private static void AssertPreferenceAttributes(string html)
    {
        Assert.Contains("data-reading-background=\"warm-cream\"", html);
        Assert.Contains("data-font-scale=\"1.2\"", html);
        Assert.Contains("data-increased-line-spacing=\"true\"", html);
        Assert.Contains("data-reduce-motion=\"true\"", html);
        Assert.Contains("href=\"/Settings/Accessibility\">Settings</a>", html);
    }

    private static string ExtractAntiForgeryToken(string html)
    {
        var match = Regex.Match(
            html,
            "<input[^>]*name=\\\"__RequestVerificationToken\\\"[^>]*value=\\\"([^\\\"]+)\\\"",
            RegexOptions.IgnoreCase);

        Assert.True(match.Success, "The rendered form should include an antiforgery token.");
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
