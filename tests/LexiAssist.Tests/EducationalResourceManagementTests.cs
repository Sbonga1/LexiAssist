using System.Net;
using System.Text.RegularExpressions;
using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Identity;
using LexiAssist.Web.Models.Resources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LexiAssist.Tests;

public sealed class EducationalResourceManagementTests
{
    [Fact]
    public async Task Resource_management_is_restricted_and_published_resources_are_role_scoped()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        using var anonymousClient = factory.CreateClient(new() { AllowAutoRedirect = false });
        var anonymousResult = await anonymousClient.GetAsync("/resources");
        Assert.Equal(HttpStatusCode.Redirect, anonymousResult.StatusCode);
        Assert.StartsWith("/Account/Login", GetLocationPath(anonymousResult));

        var (adminEmail, adminPassword) = await factory.CreateUserAsync(ApplicationRoles.Administrator);
        using var adminClient = factory.CreateClient(new() { AllowAutoRedirect = false });
        await SignInAsync(adminClient, adminEmail, adminPassword);
        var createPage = await adminClient.GetAsync("/admin/resources/create");
        Assert.Equal(HttpStatusCode.OK, createPage.StatusCode);
        var createToken = ExtractAntiForgeryToken(await createPage.Content.ReadAsStringAsync());

        var createResult = await adminClient.PostAsync("/admin/resources/create", BuildForm(createToken, "Reading guide", "Guide", "Helpful content.", "https://example.test/guide"));
        Assert.Equal(HttpStatusCode.Redirect, createResult.StatusCode);

        int resourceId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var resource = await db.EducationalResources.SingleAsync();
            resourceId = resource.Id;
            Assert.False(resource.IsPublished);
        }

        var indexHtml = await (await adminClient.GetAsync("/admin/resources")).Content.ReadAsStringAsync();
        var publishToken = ExtractAntiForgeryToken(indexHtml);
        var publishResult = await adminClient.PostAsync($"/admin/resources/{resourceId}/publish", BuildActionForm(publishToken));
        Assert.Equal(HttpStatusCode.Redirect, publishResult.StatusCode);

        var (learnerEmail, learnerPassword) = await factory.CreateUserAsync(ApplicationRoles.Learner);
        using var learnerClient = factory.CreateClient(new() { AllowAutoRedirect = false });
        await SignInAsync(learnerClient, learnerEmail, learnerPassword);
        var learnerResources = await learnerClient.GetAsync("/resources");
        Assert.Equal(HttpStatusCode.OK, learnerResources.StatusCode);
        Assert.Contains("Reading guide", await learnerResources.Content.ReadAsStringAsync());

        var (psychologistEmail, psychologistPassword) = await factory.CreateUserAsync(ApplicationRoles.Psychologist);
        using var psychologistClient = factory.CreateClient(new() { AllowAutoRedirect = false });
        await SignInAsync(psychologistClient, psychologistEmail, psychologistPassword);
        Assert.Equal(HttpStatusCode.OK, (await psychologistClient.GetAsync($"/resources/{resourceId}")).StatusCode);

        using var learnerAdminAreaClient = factory.CreateClient(new() { AllowAutoRedirect = false });
        await SignInAsync(learnerAdminAreaClient, learnerEmail, learnerPassword);
        var forbidden = await learnerAdminAreaClient.GetAsync("/admin/resources");
        Assert.Equal(HttpStatusCode.Redirect, forbidden.StatusCode);
        Assert.StartsWith("/Account/AccessDenied", GetLocationPath(forbidden));
    }

    [Fact]
    public async Task Unpublished_resource_is_not_visible_to_learners_and_invalid_external_url_is_rejected()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        var (adminEmail, adminPassword) = await factory.CreateUserAsync(ApplicationRoles.Administrator);
        using var adminClient = factory.CreateClient(new() { AllowAutoRedirect = false });
        await SignInAsync(adminClient, adminEmail, adminPassword);
        var createPage = await adminClient.GetAsync("/admin/resources/create");
        var token = ExtractAntiForgeryToken(await createPage.Content.ReadAsStringAsync());

        var invalid = await adminClient.PostAsync("/admin/resources/create", BuildForm(token, "Unsafe", "Guide", "Content", "javascript:alert(1)"));
        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
        Assert.Contains("valid http or https URL", await invalid.Content.ReadAsStringAsync());

        var (learnerEmail, learnerPassword) = await factory.CreateUserAsync(ApplicationRoles.Learner);
        using var learnerClient = factory.CreateClient(new() { AllowAutoRedirect = false });
        await SignInAsync(learnerClient, learnerEmail, learnerPassword);
        var resources = await learnerClient.GetAsync("/resources");
        Assert.Equal(HttpStatusCode.OK, resources.StatusCode);
        Assert.DoesNotContain("Unsafe", await resources.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Administrator_resource_index_uses_five_row_pages_and_normalizes_invalid_pages()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        var (adminEmail, adminPassword) = await factory.CreateUserAsync(ApplicationRoles.Administrator);
        using var adminClient = factory.CreateClient(new() { AllowAutoRedirect = false });
        await SignInAsync(adminClient, adminEmail, adminPassword);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            for (var index = 0; index < 12; index++)
            {
                db.EducationalResources.Add(new EducationalResource
                {
                    Title = $"Paging resource {index}",
                    Summary = "Paging test summary.",
                    Content = "Paging test content.",
                    ResourceType = "Guide",
                    IsPublished = index % 2 == 0,
                    CreatedAtUtc = DateTime.UtcNow.AddDays(-index),
                    UpdatedAtUtc = DateTime.UtcNow.AddDays(-index)
                });
            }

            await db.SaveChangesAsync();
        }

        var firstPage = await adminClient.GetAsync("/admin/resources?search=Paging%20resource&page=1");
        var firstPageHtml = await firstPage.Content.ReadAsStringAsync();
        var finalPage = await adminClient.GetAsync("/admin/resources?search=Paging%20resource&page=999");
        var finalPageHtml = await finalPage.Content.ReadAsStringAsync();
        var invalidPage = await adminClient.GetAsync("/admin/resources?search=Paging%20resource&page=0");
        var invalidPageHtml = await invalidPage.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, firstPage.StatusCode);
        Assert.Equal(5, Regex.Matches(firstPageHtml, "admin-resources-row").Count);
        Assert.Contains("Showing 1–5 of 12 resources", firstPageHtml);
        Assert.Contains("aria-current=\"page\">1</span>", firstPageHtml);
        Assert.Contains("page=2", firstPageHtml);

        Assert.Equal(HttpStatusCode.OK, finalPage.StatusCode);
        Assert.Equal(2, Regex.Matches(finalPageHtml, "admin-resources-row").Count);
        Assert.Contains("Showing 11–12 of 12 resources", finalPageHtml);
        Assert.Contains("aria-current=\"page\">3</span>", finalPageHtml);
        Assert.Contains("aria-disabled=\"true\">Next</span>", finalPageHtml);

        Assert.Equal(HttpStatusCode.OK, invalidPage.StatusCode);
        Assert.Contains("Showing 1–5 of 12 resources", invalidPageHtml);
    }

    [Fact]
    public async Task Administrator_resource_index_searches_title_and_summary_and_preserves_filters()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        var (adminEmail, adminPassword) = await factory.CreateUserAsync(ApplicationRoles.Administrator);
        using var adminClient = factory.CreateClient(new() { AllowAutoRedirect = false });
        await SignInAsync(adminClient, adminEmail, adminPassword);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            for (var index = 0; index < 6; index++)
            {
                db.EducationalResources.Add(new EducationalResource
                {
                    Title = $"Reading resource {index}",
                    Summary = "Short reading practice guide.",
                    Content = "Reading test content.",
                    ResourceType = "Guide",
                    IsPublished = true,
                    CreatedAtUtc = DateTime.UtcNow.AddMinutes(-index),
                    UpdatedAtUtc = DateTime.UtcNow.AddMinutes(-index)
                });
            }

            db.EducationalResources.Add(new EducationalResource
            {
                Title = "Phonics helper",
                Summary = "Reading comprehension support.",
                Content = "Summary search test content.",
                ResourceType = "Guide",
                IsPublished = true,
                CreatedAtUtc = DateTime.UtcNow.AddHours(-1),
                UpdatedAtUtc = DateTime.UtcNow.AddHours(-1)
            });
            db.EducationalResources.Add(new EducationalResource
            {
                Title = "Draft checklist",
                Summary = "A draft support checklist.",
                Content = "Draft filter test content.",
                ResourceType = "Checklist",
                IsPublished = false,
                CreatedAtUtc = DateTime.UtcNow.AddHours(-2),
                UpdatedAtUtc = DateTime.UtcNow.AddHours(-2)
            });
            await db.SaveChangesAsync();
        }

        var titleSearchHtml = await (await adminClient.GetAsync("/admin/resources?search=Reading%20resource")).Content.ReadAsStringAsync();
        var summarySearchHtml = await (await adminClient.GetAsync("/admin/resources?search=comprehension")).Content.ReadAsStringAsync();
        var filteredPage = await adminClient.GetAsync("/admin/resources?search=Reading%20resource&type=Guide&status=published&page=2");
        var filteredPageHtml = await filteredPage.Content.ReadAsStringAsync();
        var statusTypeHtml = await (await adminClient.GetAsync("/admin/resources?type=Checklist&status=draft")).Content.ReadAsStringAsync();
        var emptyHtml = await (await adminClient.GetAsync("/admin/resources?search=does-not-exist")).Content.ReadAsStringAsync();

        Assert.Contains("Reading resource", titleSearchHtml);
        Assert.Contains("Phonics helper", summarySearchHtml);
        Assert.DoesNotContain("Reading resource 0", summarySearchHtml);
        Assert.Contains("Draft checklist", statusTypeHtml);
        Assert.Contains("Showing 6–6 of 6 resources", filteredPageHtml);
        Assert.Contains("value=\"Reading resource\"", filteredPageHtml);
        Assert.Contains("value=\"Guide\"", filteredPageHtml);
        Assert.Contains("search=Reading%20resource", filteredPageHtml);
        Assert.Contains("type=Guide", filteredPageHtml);
        Assert.Contains("status=published", filteredPageHtml);
        Assert.Contains("page=1", filteredPageHtml);
        Assert.Contains("No resources match your current filters.", emptyHtml);
        Assert.DoesNotContain("admin-resources-row", emptyHtml);
    }

    [Fact]
    public async Task Administrator_can_unpublish_a_published_resource()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        var (adminEmail, adminPassword) = await factory.CreateUserAsync(ApplicationRoles.Administrator);
        using var adminClient = factory.CreateClient(new() { AllowAutoRedirect = false });
        await SignInAsync(adminClient, adminEmail, adminPassword);

        int resourceId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var resource = new EducationalResource
            {
                Title = "Published resource",
                Summary = "Published summary.",
                Content = "Published content.",
                ResourceType = "Article",
                IsPublished = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            db.EducationalResources.Add(resource);
            await db.SaveChangesAsync();
            resourceId = resource.Id;
        }

        var indexHtml = await (await adminClient.GetAsync("/admin/resources")).Content.ReadAsStringAsync();
        var token = ExtractAntiForgeryToken(indexHtml);
        var result = await adminClient.PostAsync($"/admin/resources/{resourceId}/unpublish", BuildActionForm(token));

        Assert.Equal(HttpStatusCode.Redirect, result.StatusCode);
        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await verifyDb.EducationalResources.Where(resource => resource.Id == resourceId).Select(resource => resource.IsPublished).SingleAsync());
    }

    private static async Task SignInAsync(HttpClient client, string email, string password)
    {
        var loginPage = await client.GetAsync("/Account/Login");
        var token = ExtractAntiForgeryToken(await loginPage.Content.ReadAsStringAsync());
        var result = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Email"] = email,
            ["Password"] = password,
            ["RememberMe"] = "false"
        }));
        Assert.Equal(HttpStatusCode.Redirect, result.StatusCode);
    }

    private static FormUrlEncodedContent BuildForm(string token, string title, string type, string content, string? externalUrl = null) =>
        new(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Title"] = title,
            ["Summary"] = "A short summary.",
            ["Content"] = content,
            ["ResourceType"] = type,
            ["ExternalUrl"] = externalUrl ?? string.Empty
        });

    private static FormUrlEncodedContent BuildActionForm(string token) =>
        new(new Dictionary<string, string> { ["__RequestVerificationToken"] = token });

    private static string ExtractAntiForgeryToken(string html)
    {
        var match = Regex.Match(html, "<input[^>]*name=\\\"__RequestVerificationToken\\\"[^>]*value=\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase);
        Assert.True(match.Success, "The page should include an antiforgery token.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static string GetLocationPath(HttpResponseMessage response)
    {
        var location = response.Headers.Location;
        return location is null ? string.Empty : location.IsAbsoluteUri ? location.PathAndQuery : location.OriginalString;
    }
}
