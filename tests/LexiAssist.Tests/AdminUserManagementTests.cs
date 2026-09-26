using System.Net;
using System.Text.RegularExpressions;
using LexiAssist.Web.Models.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LexiAssist.Tests;

public sealed class AdminUserManagementTests
{
    [Fact]
    public async Task Non_admin_cannot_access_user_management()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (email, password) = await factory.CreateUserAsync(ApplicationRoles.Learner);
        await SignInAsync(client, email, password);

        var result = await client.GetAsync("/admin/users");

        Assert.Equal(HttpStatusCode.Redirect, result.StatusCode);
        Assert.StartsWith("/Account/AccessDenied", GetLocationPath(result));
    }

    [Fact]
    public async Task Psychologist_cannot_access_administrator_user_management()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (email, password) = await factory.CreateUserAsync(ApplicationRoles.Psychologist);
        await SignInAsync(client, email, password);

        var result = await client.GetAsync("/admin/users");

        Assert.Equal(HttpStatusCode.Redirect, result.StatusCode);
        Assert.StartsWith("/Account/AccessDenied", GetLocationPath(result));
    }

    [Fact]
    public async Task Administrator_can_create_search_edit_activate_and_deactivate_users()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        using var adminClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (adminEmail, adminPassword) = await factory.CreateUserAsync(ApplicationRoles.Administrator);
        await SignInAsync(adminClient, adminEmail, adminPassword);

        var createPage = await adminClient.GetAsync("/admin/users/create");
        Assert.Equal(HttpStatusCode.OK, createPage.StatusCode);
        var createHtml = await createPage.Content.ReadAsStringAsync();
        Assert.Contains("Learner", createHtml);
        Assert.Contains("Psychologist", createHtml);
        Assert.DoesNotContain("Administrator</option>", createHtml);

        var maliciousEmail = $"{Guid.NewGuid():N}@example.test";
        var rejectedRoleResult = await adminClient.PostAsync(
            "/admin/users/create",
            BuildForm(await ExtractAntiForgeryTokenAsync(createHtml), maliciousEmail, "Administrator"));
        Assert.Equal(HttpStatusCode.OK, rejectedRoleResult.StatusCode);

        var psychologistEmail = $"psychologist-{Guid.NewGuid():N}@example.test";
        const string psychologistPassword = "Psychologist2026";
        var psychologistResult = await adminClient.PostAsync(
            "/admin/users/create",
            BuildForm(await ExtractAntiForgeryTokenAsync(createHtml), psychologistEmail, "Psychologist", psychologistPassword));
        Assert.Equal(HttpStatusCode.Redirect, psychologistResult.StatusCode);

        var learnerEmail = $"learner-{Guid.NewGuid():N}@example.test";
        const string learnerPassword = "LearnerPass2026";
        var learnerResult = await adminClient.PostAsync(
            "/admin/users/create",
            BuildForm(await ExtractAntiForgeryTokenAsync(createHtml), learnerEmail, "Learner", learnerPassword));
        Assert.Equal(HttpStatusCode.Redirect, learnerResult.StatusCode);

        string psychologistId;
        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.Null(await userManager.FindByEmailAsync(maliciousEmail));

            var psychologist = await userManager.FindByEmailAsync(psychologistEmail);
            var learner = await userManager.FindByEmailAsync(learnerEmail);
            Assert.NotNull(psychologist);
            Assert.NotNull(learner);
            psychologistId = psychologist.Id;
            Assert.True(await userManager.IsInRoleAsync(psychologist, ApplicationRoles.Psychologist));
            Assert.False(await userManager.IsInRoleAsync(psychologist, ApplicationRoles.Administrator));
            Assert.True(await userManager.IsInRoleAsync(learner, ApplicationRoles.Learner));
        }

        var searchResult = await adminClient.GetAsync($"/admin/users?search={Uri.EscapeDataString(psychologistEmail)}");
        Assert.Equal(HttpStatusCode.OK, searchResult.StatusCode);
        Assert.Contains(psychologistEmail, await searchResult.Content.ReadAsStringAsync());

        var detailsUrl = $"/admin/users/{psychologistId}";
        var detailsResult = await adminClient.GetAsync(detailsUrl);
        var detailsHtml = await detailsResult.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, detailsResult.StatusCode);
        Assert.Contains("Psychologist", detailsHtml);
        Assert.DoesNotContain("PasswordHash", detailsHtml);

        var editUrl = $"{detailsUrl}/edit";
        var editPage = await adminClient.GetAsync(editUrl);
        var editToken = await ExtractAntiForgeryTokenAsync(await editPage.Content.ReadAsStringAsync());
        var editResult = await adminClient.PostAsync(
            editUrl,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = editToken,
                ["Id"] = psychologistId,
                ["FirstName"] = "Updated",
                ["LastName"] = "Professional",
                ["PreferredName"] = "Updated name",
                ["DateOfBirth"] = "1985-03-02",
                ["Email"] = psychologistEmail
            }));
        Assert.Equal(HttpStatusCode.Redirect, editResult.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var updatedPsychologist = await userManager.FindByIdAsync(psychologistId);
            Assert.NotNull(updatedPsychologist);
            Assert.Equal("Updated", updatedPsychologist.FirstName);
            Assert.Equal("Updated name", updatedPsychologist.PreferredName);
            Assert.True(await userManager.IsInRoleAsync(updatedPsychologist, ApplicationRoles.Psychologist));
        }

        using var psychologistClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(psychologistClient, psychologistEmail, psychologistPassword);
        Assert.Equal(HttpStatusCode.OK, (await psychologistClient.GetAsync("/Dashboard/Psychologist")).StatusCode);

        var deactivateToken = await ExtractAntiForgeryTokenAsync(
            await (await adminClient.GetAsync(detailsUrl)).Content.ReadAsStringAsync());
        var deactivateResult = await adminClient.PostAsync(
            $"{detailsUrl}/deactivate",
            BuildActionForm(deactivateToken));
        Assert.Equal(HttpStatusCode.Redirect, deactivateResult.StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.False((await userManager.FindByIdAsync(psychologistId))!.IsActive);
        }

        var blockedFormerSession = await psychologistClient.GetAsync("/Dashboard/Psychologist");
        Assert.Equal(HttpStatusCode.Redirect, blockedFormerSession.StatusCode);
        Assert.StartsWith("/Account/Login", GetLocationPath(blockedFormerSession));

        var activateToken = await ExtractAntiForgeryTokenAsync(
            await (await adminClient.GetAsync(detailsUrl)).Content.ReadAsStringAsync());
        var activateResult = await adminClient.PostAsync(
            $"{detailsUrl}/activate",
            BuildActionForm(activateToken));
        Assert.Equal(HttpStatusCode.Redirect, activateResult.StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.True((await userManager.FindByIdAsync(psychologistId))!.IsActive);
        }
    }

    [Fact]
    public async Task Administrator_cannot_deactivate_the_account_they_are_using()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (email, password) = await factory.CreateUserAsync(ApplicationRoles.Administrator);
        await SignInAsync(client, email, password);

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var administrator = await userManager.FindByEmailAsync(email);
        Assert.NotNull(administrator);

        var detailsUrl = $"/admin/users/{administrator.Id}";
        var details = await client.GetAsync(detailsUrl);
        var token = await ExtractAntiForgeryTokenAsync(await details.Content.ReadAsStringAsync());
        var result = await client.PostAsync($"{detailsUrl}/deactivate", BuildActionForm(token));

        Assert.Equal(HttpStatusCode.Redirect, result.StatusCode);
        Assert.True((await userManager.FindByIdAsync(administrator.Id))!.IsActive);
        var resultPage = await client.GetAsync(detailsUrl);
        Assert.Contains("You cannot deactivate the administrator account", await resultPage.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Administrator_details_pager_uses_canonical_order_and_preserves_authorization()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        using var adminClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var admin = await factory.CreateUserAsync(ApplicationRoles.Administrator);
        var second = await factory.CreateUserAsync(ApplicationRoles.Learner);
        var third = await factory.CreateUserAsync(ApplicationRoles.Learner);
        var fourth = await factory.CreateUserAsync(ApplicationRoles.Psychologist);

        var identities = new[]
        {
            (admin.Email, FirstName: "Admin", LastName: "Aardvark"),
            (second.Email, FirstName: "Alex", LastName: "Baker"),
            (third.Email, FirstName: "Pat", LastName: "Clark"),
            (fourth.Email, FirstName: "Red", LastName: "Davis")
        };
        var userIds = new Dictionary<string, string>();

        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            foreach (var identity in identities)
            {
                var user = await userManager.FindByEmailAsync(identity.Email);
                Assert.NotNull(user);
                user.FirstName = identity.FirstName;
                user.LastName = identity.LastName;
                var updateResult = await userManager.UpdateAsync(user);
                Assert.True(updateResult.Succeeded, string.Join("; ", updateResult.Errors.Select(error => error.Description)));
                userIds[identity.Email] = user.Id;
            }
        }

        await SignInAsync(adminClient, admin.Email, admin.Password);

        var firstDetails = await adminClient.GetAsync($"/admin/users/{userIds[admin.Email]}");
        var firstHtml = await firstDetails.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, firstDetails.StatusCode);
        Assert.Contains("User 1 of 4", firstHtml);
        Assert.Contains($"/admin/users/{userIds[second.Email]}", firstHtml);

        var secondDetails = await adminClient.GetAsync($"/admin/users/{userIds[second.Email]}");
        var secondHtml = await secondDetails.Content.ReadAsStringAsync();
        Assert.Contains("User 2 of 4", secondHtml);
        Assert.Contains($"/admin/users/{userIds[admin.Email]}", secondHtml);
        Assert.Contains($"/admin/users/{userIds[third.Email]}", secondHtml);

        var lastDetails = await adminClient.GetAsync($"/admin/users/{userIds[fourth.Email]}");
        var lastHtml = await lastDetails.Content.ReadAsStringAsync();
        Assert.Contains("User 4 of 4", lastHtml);
        Assert.Contains($"/admin/users/{userIds[third.Email]}", lastHtml);
        Assert.DoesNotContain($"href=\"/admin/users/{userIds[fourth.Email]}\"", lastHtml);

        using var learnerClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(learnerClient, second.Email, second.Password);
        var unauthorized = await learnerClient.GetAsync($"/admin/users/{userIds[admin.Email]}");
        Assert.Equal(HttpStatusCode.Redirect, unauthorized.StatusCode);
        Assert.StartsWith("/Account/AccessDenied", GetLocationPath(unauthorized));
    }

    private static async Task SignInAsync(HttpClient client, string email, string password)
    {
        var loginPage = await client.GetAsync("/Account/Login");
        var token = await ExtractAntiForgeryTokenAsync(await loginPage.Content.ReadAsStringAsync());
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

    private static FormUrlEncodedContent BuildForm(
        string token,
        string email,
        string role,
        string password = "AdminCreated2026") => new(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["FirstName"] = "Account",
            ["LastName"] = "User",
            ["Email"] = email,
            ["Role"] = role,
            ["Password"] = password,
            ["ConfirmPassword"] = password
        });

    private static FormUrlEncodedContent BuildActionForm(string token) =>
        new(new Dictionary<string, string> { ["__RequestVerificationToken"] = token });

    private static async Task<string> ExtractAntiForgeryTokenAsync(string html)
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
