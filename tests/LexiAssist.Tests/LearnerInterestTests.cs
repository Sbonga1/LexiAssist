using System.Net;
using System.Text.RegularExpressions;
using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LexiAssist.Tests;

public sealed class LearnerInterestTests
{
    [Fact]
    public async Task Learner_can_save_and_update_optional_interests_in_settings()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        var (email, password) = await factory.CreateUserAsync(ApplicationRoles.Learner);
        using var client = CreateClient(factory);
        await SignInAsync(client, email, password);

        var settings = await client.GetAsync("/Settings/Accessibility");
        var settingsHtml = await settings.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, settings.StatusCode);
        Assert.Contains("Learner interests", settingsHtml);
        Assert.Contains("Space", settingsHtml);
        Assert.Contains("Technology", settingsHtml);

        var saved = await client.PostAsync(
            "/Settings/Accessibility",
            new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("__RequestVerificationToken", ExtractAntiForgeryToken(settingsHtml)),
                new KeyValuePair<string, string>("BackgroundTheme", "standard-light"),
                new KeyValuePair<string, string>("FontScale", "1"),
                new KeyValuePair<string, string>("SelectedInterests", "space"),
                new KeyValuePair<string, string>("SelectedInterests", "technology")
            }));

        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await userManager.FindByEmailAsync(email);
            Assert.NotNull(user);

            var selected = await dbContext.LearnerInterests
                .Where(selection => selection.UserId == user.Id)
                .OrderBy(selection => selection.InterestKey)
                .Select(selection => selection.InterestKey)
                .ToListAsync();
            Assert.Equal(new[] { "space", "technology" }, selected);
        }

        var updatedSettings = await client.GetAsync("/Settings/Accessibility");
        var updatedHtml = await updatedSettings.Content.ReadAsStringAsync();
        Assert.Matches("id=\"interest-space\"[^>]*checked", updatedHtml);
        Assert.Matches("id=\"interest-technology\"[^>]*checked", updatedHtml);

        var cleared = await client.PostAsync(
            "/Settings/Accessibility",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiForgeryToken(updatedHtml),
                ["BackgroundTheme"] = "standard-light",
                ["FontScale"] = "1"
            }));
        Assert.Equal(HttpStatusCode.Redirect, cleared.StatusCode);

        using var finalScope = factory.Services.CreateScope();
        var finalUserManager = finalScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var finalDbContext = finalScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var finalUser = await finalUserManager.FindByEmailAsync(email);
        Assert.NotNull(finalUser);
        Assert.Empty(await finalDbContext.LearnerInterests
            .Where(selection => selection.UserId == finalUser.Id)
            .ToListAsync());
    }

    [Fact]
    public async Task Unlisted_interest_is_rejected_and_is_not_saved()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        var (email, password) = await factory.CreateUserAsync(ApplicationRoles.Learner);
        using var client = CreateClient(factory);
        await SignInAsync(client, email, password);

        var settings = await client.GetAsync("/Settings/Accessibility");
        var html = await settings.Content.ReadAsStringAsync();
        var result = await client.PostAsync(
            "/Settings/Accessibility",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiForgeryToken(html),
                ["BackgroundTheme"] = "standard-light",
                ["FontScale"] = "1",
                ["SelectedInterests"] = "clinical-diagnosis"
            }));

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Contains("Choose interests from the available options", await result.Content.ReadAsStringAsync());

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.NotNull(user);
        Assert.Empty(await dbContext.LearnerInterests
            .Where(selection => selection.UserId == user.Id)
            .ToListAsync());
    }

    private static HttpClient CreateClient(LexiAssistWebApplicationFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static async Task SignInAsync(HttpClient client, string email, string password)
    {
        var loginPage = await client.GetAsync("/Account/Login");
        var loginHtml = await loginPage.Content.ReadAsStringAsync();
        var response = await client.PostAsync(
            "/Account/Login",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiForgeryToken(loginHtml),
                ["Email"] = email,
                ["Password"] = password,
                ["RememberMe"] = "true"
            }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Dashboard/Learner", response.Headers.Location?.OriginalString);
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
}
