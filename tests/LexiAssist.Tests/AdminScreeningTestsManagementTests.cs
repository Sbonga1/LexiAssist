using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Identity;
using LexiAssist.Web.Models.Screening;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LexiAssist.Tests;

public sealed class AdminScreeningTestsManagementTests
{
    [Fact]
    public async Task Screening_test_management_is_restricted_to_administrators()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        using var anonymousClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var anonymousResult = await anonymousClient.GetAsync("/admin/screening-tests");
        Assert.Equal(HttpStatusCode.Redirect, anonymousResult.StatusCode);
        Assert.StartsWith("/Account/Login", GetLocationPath(anonymousResult));

        using var learnerClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (learnerEmail, learnerPassword) = await factory.CreateUserAsync(ApplicationRoles.Learner);
        await SignInAsync(learnerClient, learnerEmail, learnerPassword);

        var learnerResult = await learnerClient.GetAsync("/admin/screening-tests");
        Assert.Equal(HttpStatusCode.Redirect, learnerResult.StatusCode);
        Assert.StartsWith("/Account/AccessDenied", GetLocationPath(learnerResult));

        using var psychologistClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (psychologistEmail, psychologistPassword) = await factory.CreateUserAsync(ApplicationRoles.Psychologist);
        await SignInAsync(psychologistClient, psychologistEmail, psychologistPassword);

        var psychologistResult = await psychologistClient.GetAsync("/admin/screening-tests");
        Assert.Equal(HttpStatusCode.Redirect, psychologistResult.StatusCode);
        Assert.StartsWith("/Account/AccessDenied", GetLocationPath(psychologistResult));

        using var adminClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (adminEmail, adminPassword) = await factory.CreateUserAsync(ApplicationRoles.Administrator);
        await SignInAsync(adminClient, adminEmail, adminPassword);

        var adminResult = await adminClient.GetAsync("/admin/screening-tests");
        Assert.Equal(HttpStatusCode.OK, adminResult.StatusCode);
        var html = await adminResult.Content.ReadAsStringAsync();
        Assert.Contains("Screening Tests", html);
        Assert.Contains("href=\"/admin/screening-tests\"", html);
    }

    [Fact]
    public async Task Administrator_can_create_edit_reorder_activate_and_deactivate_screening_tests()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (adminEmail, adminPassword) = await factory.CreateUserAsync(ApplicationRoles.Administrator);
        await SignInAsync(client, adminEmail, adminPassword);

        var createPage = await client.GetAsync("/admin/screening-tests/create");
        Assert.Equal(HttpStatusCode.OK, createPage.StatusCode);
        var createPageHtml = await createPage.Content.ReadAsStringAsync();
        var createToken = ExtractAntiForgeryToken(createPageHtml);

        var duplicateSlugResult = await client.PostAsync(
            "/admin/screening-tests/create",
            BuildForm(createToken, "Duplicate category", "WORD-READING"));
        Assert.Equal(HttpStatusCode.OK, duplicateSlugResult.StatusCode);
        Assert.Contains("already exists", await duplicateSlugResult.Content.ReadAsStringAsync());

        var createdResult = await client.PostAsync(
            "/admin/screening-tests/create",
            BuildForm(createToken, "Demo category", "Demo-Category"));
        Assert.Equal(HttpStatusCode.Redirect, createdResult.StatusCode);

        int testId;
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var screeningTest = await dbContext.ScreeningTests.SingleAsync(test => test.Slug == "demo-category");
            testId = screeningTest.Id;
            Assert.False(screeningTest.IsActive);
        }

        var detailsUrl = $"/admin/screening-tests/{testId}";
        var detailsPage = await client.GetAsync(detailsUrl);
        Assert.Equal(HttpStatusCode.OK, detailsPage.StatusCode);
        var detailsHtml = await detailsPage.Content.ReadAsStringAsync();
        Assert.Contains("Demo category", detailsHtml);
        Assert.DoesNotContain("Add question", detailsHtml);

        var editPage = await client.GetAsync($"{detailsUrl}/edit");
        var editToken = ExtractAntiForgeryToken(await editPage.Content.ReadAsStringAsync());
        var duplicateEditResult = await client.PostAsync(
            $"{detailsUrl}/edit",
            BuildForm(editToken, "Updated demo category", "WORD-READING", testId));
        Assert.Equal(HttpStatusCode.OK, duplicateEditResult.StatusCode);
        Assert.Contains("already exists", await duplicateEditResult.Content.ReadAsStringAsync());

        var editResult = await client.PostAsync(
            $"{detailsUrl}/edit",
            BuildForm(editToken, "Updated demo category", "Demo-Category-Updated", testId,
                "Updated description.", "Updated instructions."));
        Assert.Equal(HttpStatusCode.Redirect, editResult.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var screeningTest = await dbContext.ScreeningTests.SingleAsync(test => test.Id == testId);
            Assert.Equal("Updated demo category", screeningTest.Name);
            Assert.Equal("demo-category-updated", screeningTest.Slug);
            Assert.Equal("Updated description.", screeningTest.Description);
        }

        var moveToken = ExtractAntiForgeryToken(await (await client.GetAsync("/admin/screening-tests")).Content.ReadAsStringAsync());
        var moveResult = await client.PostAsync(
            $"{detailsUrl}/move",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = moveToken,
                ["direction"] = "up"
            }));
        Assert.Equal(HttpStatusCode.Redirect, moveResult.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var screeningTest = await dbContext.ScreeningTests.SingleAsync(test => test.Id == testId);
            Assert.Equal(9, screeningTest.SortOrder);
        }

        var token = ExtractAntiForgeryToken(await (await client.GetAsync(detailsUrl)).Content.ReadAsStringAsync());
        var deactivateResult = await client.PostAsync($"{detailsUrl}/deactivate", BuildActionForm(token));
        Assert.Equal(HttpStatusCode.Redirect, deactivateResult.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.False((await dbContext.ScreeningTests.SingleAsync(test => test.Id == testId)).IsActive);
        }

        using var missingTokenForm = new FormUrlEncodedContent(new Dictionary<string, string>());
        var missingTokenActivation = await client.PostAsync($"{detailsUrl}/activate", missingTokenForm);
        Assert.Equal(HttpStatusCode.BadRequest, missingTokenActivation.StatusCode);

        token = ExtractAntiForgeryToken(await (await client.GetAsync(detailsUrl)).Content.ReadAsStringAsync());
        var blockedActivation = await client.PostAsync($"{detailsUrl}/activate", BuildActionForm(token));
        Assert.Equal(HttpStatusCode.Redirect, blockedActivation.StatusCode);
        var blockedDetails = await client.GetAsync(detailsUrl);
        Assert.Contains("Add and activate at least one question before publishing this assessment.", await blockedDetails.Content.ReadAsStringAsync());

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.False((await dbContext.ScreeningTests.SingleAsync(test => test.Id == testId)).IsActive);
            dbContext.ScreeningQuestions.Add(new ScreeningQuestion
            {
                ScreeningTestId = testId,
                Prompt = "Choose the correct answer.",
                QuestionType = ScreeningQuestionType.MultipleChoice,
                CorrectAnswer = "yes",
                MetadataJson = JsonSerializer.Serialize(new ScreeningQuestionMetadata { ChoiceOptions = ["yes", "no"] }),
                SortOrder = 1,
                IsActive = true
            });
            await dbContext.SaveChangesAsync();
        }

        token = ExtractAntiForgeryToken(await (await client.GetAsync(detailsUrl)).Content.ReadAsStringAsync());
        var activateResult = await client.PostAsync($"{detailsUrl}/activate", BuildActionForm(token));
        Assert.Equal(HttpStatusCode.Redirect, activateResult.StatusCode);
        using var verifyScope = factory.Services.CreateScope();
        var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True((await verifyDbContext.ScreeningTests.SingleAsync(test => test.Id == testId)).IsActive);
        Assert.Equal(1, await verifyDbContext.ScreeningQuestions.CountAsync(question => question.ScreeningTestId == testId && question.IsActive));
    }

    [Fact]
    public async Task Screening_test_index_supports_name_and_slug_search()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (adminEmail, adminPassword) = await factory.CreateUserAsync(ApplicationRoles.Administrator);
        await SignInAsync(client, adminEmail, adminPassword);

        var nameSearch = await client.GetAsync("/admin/screening-tests?search=Reading");
        Assert.Equal(HttpStatusCode.OK, nameSearch.StatusCode);
        var nameSearchHtml = await nameSearch.Content.ReadAsStringAsync();
        Assert.Contains("Word Reading", nameSearchHtml);
        Assert.Contains("Reading Fluency", nameSearchHtml);
        Assert.DoesNotContain(">Spelling<", nameSearchHtml);
        Assert.Contains("4 test(s)", nameSearchHtml);
        Assert.Contains("href=\"/admin/screening-tests\">Clear</a>", nameSearchHtml);

        var slugSearch = await client.GetAsync("/admin/screening-tests?search=memory-broader-thinking-skills");
        Assert.Equal(HttpStatusCode.OK, slugSearch.StatusCode);
        var slugSearchHtml = await slugSearch.Content.ReadAsStringAsync();
        Assert.Contains("Memory and Broader Thinking Skills", slugSearchHtml);
        Assert.Contains("1 test(s)", slugSearchHtml);
        Assert.DoesNotContain("Word Reading", slugSearchHtml);
    }

    [Fact]
    public async Task Screening_test_index_paginates_in_display_order_and_normalizes_invalid_pages()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (adminEmail, adminPassword) = await factory.CreateUserAsync(ApplicationRoles.Administrator);
        await SignInAsync(client, adminEmail, adminPassword);

        var firstPage = await client.GetAsync("/admin/screening-tests?page=1");
        var firstPageHtml = await firstPage.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, firstPage.StatusCode);
        Assert.Contains("Word Reading", firstPageHtml);
        Assert.Contains("Reading Fluency", firstPageHtml);
        Assert.DoesNotContain("Reading Comprehension", firstPageHtml);
        Assert.Contains("Drag to reorder Word Reading", firstPageHtml);
        Assert.DoesNotContain("Move Word Reading up", firstPageHtml);
        Assert.DoesNotContain("Move Word Reading down", firstPageHtml);
        Assert.Contains("aria-current=\"page\">1</span>", firstPageHtml);
        Assert.Contains("page=2", firstPageHtml);

        var finalPage = await client.GetAsync("/admin/screening-tests?page=2");
        var finalPageHtml = await finalPage.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, finalPage.StatusCode);
        Assert.DoesNotContain("Word Reading", finalPageHtml);
        Assert.Contains("Reading Comprehension", finalPageHtml);
        Assert.Contains("Memory and Broader Thinking Skills", finalPageHtml);
        Assert.Contains("aria-current=\"page\">2</span>", finalPageHtml);
        Assert.Contains("aria-disabled=\"true\" aria-label=\"Next page\"", finalPageHtml);

        var invalidPage = await client.GetAsync("/admin/screening-tests?page=not-a-number");
        var invalidPageHtml = await invalidPage.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, invalidPage.StatusCode);
        Assert.Contains("aria-current=\"page\">1</span>", invalidPageHtml);

        var beyondFinalPage = await client.GetAsync("/admin/screening-tests?page=999");
        var beyondFinalPageHtml = await beyondFinalPage.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, beyondFinalPage.StatusCode);
        Assert.Contains("aria-current=\"page\">2</span>", beyondFinalPageHtml);
    }

    [Fact]
    public async Task Screening_test_pager_preserves_search_context()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (adminEmail, adminPassword) = await factory.CreateUserAsync(ApplicationRoles.Administrator);
        await SignInAsync(client, adminEmail, adminPassword);

        var response = await client.GetAsync("/admin/screening-tests?search=e&page=2");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("value=\"e\"", html);
        Assert.Contains("8 test(s)", html);
        Assert.Contains("aria-current=\"page\">2</span>", html);
        Assert.Contains("search=e&amp;page=1", html);
        Assert.Contains("href=\"/admin/screening-tests\">Clear</a>", html);
    }

    [Fact]
    public async Task Screening_test_reorder_crossing_page_boundary_uses_full_order()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (adminEmail, adminPassword) = await factory.CreateUserAsync(ApplicationRoles.Administrator);
        await SignInAsync(client, adminEmail, adminPassword);

        var indexHtml = await (await client.GetAsync("/admin/screening-tests?page=1")).Content.ReadAsStringAsync();
        var moveToken = ExtractAntiForgeryToken(indexHtml);
        var response = await client.PostAsync(
            "/admin/screening-tests/reorder",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = moveToken,
                ["id"] = "6",
                ["beforeId"] = "8"
            }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/admin/screening-tests?page=2", GetLocationPath(response));

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(6, await dbContext.ScreeningTests.Where(test => test.Id == 7).Select(test => test.SortOrder).SingleAsync());
        Assert.Equal(7, await dbContext.ScreeningTests.Where(test => test.Id == 6).Select(test => test.SortOrder).SingleAsync());
    }

    [Fact]
    public async Task Optional_memory_category_stays_inactive_until_it_has_an_active_question()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (adminEmail, adminPassword) = await factory.CreateUserAsync(ApplicationRoles.Administrator);
        await SignInAsync(client, adminEmail, adminPassword);

        const int optionalCategoryId = 9;
        var detailsUrl = $"/admin/screening-tests/{optionalCategoryId}";
        var detailsResponse = await client.GetAsync(detailsUrl);
        Assert.Equal(HttpStatusCode.OK, detailsResponse.StatusCode);
        var detailsHtml = await detailsResponse.Content.ReadAsStringAsync();
        Assert.Contains("disabled by default", detailsHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("organisational and professional approval", detailsHtml, StringComparison.OrdinalIgnoreCase);

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.False((await dbContext.ScreeningTests.SingleAsync(test => test.Id == optionalCategoryId)).IsActive);
        }

        var activateToken = ExtractAntiForgeryToken(detailsHtml);
        var activateResponse = await client.PostAsync($"{detailsUrl}/activate", BuildActionForm(activateToken));
        Assert.Equal(HttpStatusCode.Redirect, activateResponse.StatusCode);
        var blockedDetails = await client.GetAsync(detailsUrl);
        Assert.Contains("Add and activate at least one question before publishing this assessment.", await blockedDetails.Content.ReadAsStringAsync());

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.False((await dbContext.ScreeningTests.SingleAsync(test => test.Id == optionalCategoryId)).IsActive);
        }
    }

    private static async Task SignInAsync(HttpClient client, string email, string password)
    {
        var loginPage = await client.GetAsync("/Account/Login");
        var token = ExtractAntiForgeryToken(await loginPage.Content.ReadAsStringAsync());
        var loginResult = await client.PostAsync(
            "/Account/Login",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["Email"] = email,
                ["Password"] = password,
                ["RememberMe"] = "false"
            }));

        Assert.Equal(HttpStatusCode.Redirect, loginResult.StatusCode);
    }

    private static FormUrlEncodedContent BuildForm(
        string token,
        string name,
        string slug,
        int id = 0,
        string description = "Educational screening category for administrator management.",
        string instructions = "Items are not configured yet.") => new(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Id"] = id.ToString(),
            ["Name"] = name,
            ["Slug"] = slug,
            ["Description"] = description,
            ["Instructions"] = instructions
        });

    private static FormUrlEncodedContent BuildActionForm(string token) =>
        new(new Dictionary<string, string> { ["__RequestVerificationToken"] = token });

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
