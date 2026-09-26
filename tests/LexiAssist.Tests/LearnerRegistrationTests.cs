using System.Net;
using System.Text.RegularExpressions;
using LexiAssist.Web.Models.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace LexiAssist.Tests;

public sealed class LearnerRegistrationTests
{
    [Fact]
    public async Task Registration_creates_and_signs_in_a_learner_even_when_a_privileged_role_is_submitted()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var registrationPage = await client.GetAsync("/Account/Register");
        Assert.Equal(HttpStatusCode.OK, registrationPage.StatusCode);
        var pageHtml = await registrationPage.Content.ReadAsStringAsync();
        Assert.Contains("does not provide a clinical diagnosis", pageHtml);
        var token = ExtractAntiForgeryToken(pageHtml);
        var email = $"{Guid.NewGuid():N}@example.test";
        const string password = "LexiAssist2026";

        var result = await client.PostAsync(
            "/Account/Register",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["FirstName"] = "Morgan",
                ["LastName"] = "Learner",
                ["PreferredName"] = "Mo",
                ["DateOfBirth"] = "2005-06-07",
                ["Email"] = email,
                ["Password"] = password,
                ["ConfirmPassword"] = password,
                ["AcceptScreeningPrivacyNotice"] = "true",
                ["Role"] = ApplicationRoles.Administrator,
                ["Roles"] = ApplicationRoles.Psychologist
            }));

        Assert.Equal(HttpStatusCode.Redirect, result.StatusCode);
        Assert.Equal("/Dashboard/Learner", result.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Dashboard/Learner")).StatusCode);
        var administratorResponse = await client.GetAsync("/Dashboard/Administrator");
        Assert.Equal(HttpStatusCode.Redirect, administratorResponse.StatusCode);
        Assert.StartsWith("/Account/AccessDenied", GetLocationPath(administratorResponse));

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);

        Assert.NotNull(user);
        Assert.Equal("Morgan", user.FirstName);
        Assert.Equal("Learner", user.LastName);
        Assert.Equal("Mo", user.PreferredName);
        Assert.Equal(new DateOnly(2005, 6, 7), user.DateOfBirth);
        Assert.NotNull(user.ScreeningPrivacyNoticeAcceptedAtUtc);
        Assert.True(await userManager.IsInRoleAsync(user, ApplicationRoles.Learner));
        Assert.False(await userManager.IsInRoleAsync(user, ApplicationRoles.Administrator));
        Assert.False(await userManager.IsInRoleAsync(user, ApplicationRoles.Psychologist));
    }

    [Fact]
    public async Task Registration_rejects_missing_notice_mismatched_password_and_future_birth_date()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var registrationPage = await client.GetAsync("/Account/Register");
        var token = ExtractAntiForgeryToken(await registrationPage.Content.ReadAsStringAsync());
        var email = $"{Guid.NewGuid():N}@example.test";

        var result = await client.PostAsync(
            "/Account/Register",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["FirstName"] = "Taylor",
                ["LastName"] = "Learner",
                ["DateOfBirth"] = "2999-01-01",
                ["Email"] = email,
                ["Password"] = "LexiAssist2026",
                ["ConfirmPassword"] = "DifferentPass2026",
                ["AcceptScreeningPrivacyNotice"] = "false"
            }));

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var responseHtml = await result.Content.ReadAsStringAsync();
        Assert.Contains("You must acknowledge the screening and privacy notice", responseHtml);
        Assert.Contains("password and confirmation do not match", responseHtml);
        Assert.Contains("Date of birth cannot be in the future", responseHtml);

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Null(await userManager.FindByEmailAsync(email));
    }

    [Fact]
    public async Task Registration_reports_duplicate_email_without_creating_another_account()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var (existingEmail, _) = await factory.CreateUserAsync(ApplicationRoles.Learner);

        var registrationPage = await client.GetAsync("/Account/Register");
        var token = ExtractAntiForgeryToken(await registrationPage.Content.ReadAsStringAsync());
        var result = await client.PostAsync(
            "/Account/Register",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["FirstName"] = "Another",
                ["LastName"] = "Learner",
                ["Email"] = existingEmail,
                ["Password"] = "LexiAssist2026",
                ["ConfirmPassword"] = "LexiAssist2026",
                ["AcceptScreeningPrivacyNotice"] = "true"
            }));

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Contains("An account with this email address already exists", await result.Content.ReadAsStringAsync());

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.NotNull(await userManager.FindByEmailAsync(existingEmail));
    }

    [Fact]
    public async Task Registration_rejects_oversized_credentials_before_identity_processing()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var registrationPage = await client.GetAsync("/Account/Register");
        var token = ExtractAntiForgeryToken(await registrationPage.Content.ReadAsStringAsync());
        var password = new string('P', 257);
        var email = $"{Guid.NewGuid():N}@example.test";

        var result = await client.PostAsync(
            "/Account/Register",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["FirstName"] = "Length",
                ["LastName"] = "Check",
                ["Email"] = email,
                ["Password"] = password,
                ["ConfirmPassword"] = password,
                ["AcceptScreeningPrivacyNotice"] = "true"
            }));

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Contains("256", await result.Content.ReadAsStringAsync());
    }

    private static string ExtractAntiForgeryToken(string html)
    {
        var match = Regex.Match(
            html,
            "<input[^>]*name=\\\"__RequestVerificationToken\\\"[^>]*value=\\\"([^\\\"]+)\\\"",
            RegexOptions.IgnoreCase);

        Assert.True(match.Success, "The rendered registration form should include an antiforgery token.");
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
