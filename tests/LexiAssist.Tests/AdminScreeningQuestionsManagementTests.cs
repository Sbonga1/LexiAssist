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

public sealed class AdminScreeningQuestionsManagementTests
{
    [Fact]
    public async Task Question_management_is_administrator_only_and_is_linked_from_test_details()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        using var anonymousClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var anonymousResult = await anonymousClient.GetAsync("/admin/screening-tests/1/questions");
        Assert.Equal(HttpStatusCode.Redirect, anonymousResult.StatusCode);
        Assert.StartsWith("/Account/Login", GetLocationPath(anonymousResult));

        using var learnerClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (learnerEmail, learnerPassword) = await factory.CreateUserAsync(ApplicationRoles.Learner);
        await SignInAsync(learnerClient, learnerEmail, learnerPassword);
        var learnerResult = await learnerClient.GetAsync("/admin/screening-tests/1/questions");
        Assert.Equal(HttpStatusCode.Redirect, learnerResult.StatusCode);
        Assert.StartsWith("/Account/AccessDenied", GetLocationPath(learnerResult));

        using var psychologistClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (psychologistEmail, psychologistPassword) = await factory.CreateUserAsync(ApplicationRoles.Psychologist);
        await SignInAsync(psychologistClient, psychologistEmail, psychologistPassword);
        var psychologistResult = await psychologistClient.GetAsync("/admin/screening-tests/1/questions");
        Assert.Equal(HttpStatusCode.Redirect, psychologistResult.StatusCode);
        Assert.StartsWith("/Account/AccessDenied", GetLocationPath(psychologistResult));

        using var adminClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (adminEmail, adminPassword) = await factory.CreateUserAsync(ApplicationRoles.Administrator);
        await SignInAsync(adminClient, adminEmail, adminPassword);

        var testDetails = await adminClient.GetAsync("/admin/screening-tests/1");
        Assert.Equal(HttpStatusCode.OK, testDetails.StatusCode);
        Assert.Contains("href=\"/admin/screening-tests/1/questions\"", await testDetails.Content.ReadAsStringAsync());

        var questionsPage = await adminClient.GetAsync("/admin/screening-tests/1/questions");
        Assert.Equal(HttpStatusCode.OK, questionsPage.StatusCode);
        Assert.Contains("No questions have been added to this test", await questionsPage.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Administrator_can_create_edit_activate_deactivate_and_reorder_questions_with_type_validation()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (adminEmail, adminPassword) = await factory.CreateUserAsync(ApplicationRoles.Administrator);
        await SignInAsync(client, adminEmail, adminPassword);

        const int screeningTestId = 1;
        var questionsUrl = $"/admin/screening-tests/{screeningTestId}/questions";
        var createPage = await client.GetAsync($"{questionsUrl}/create");
        Assert.Equal(HttpStatusCode.OK, createPage.StatusCode);
        var createHtml = await createPage.Content.ReadAsStringAsync();
        Assert.Contains("data-question-types=\"1,5,12,13\"", createHtml);
        Assert.Contains("data-question-types=\"8,10\"", createHtml);
        Assert.Contains("screening-question-form.js", createHtml);
        var createToken = ExtractAntiForgeryToken(createHtml);

        var invalidChoiceResult = await client.PostAsync(
            $"{questionsUrl}/create",
            BuildForm(createToken, ScreeningQuestionType.MultipleChoice, "Select a word.",
                ("ChoiceOptionsText", "one"), ("CorrectAnswer", "missing")));
        Assert.Equal(HttpStatusCode.OK, invalidChoiceResult.StatusCode);
        Assert.Contains("Enter at least two different answer choices", await invalidChoiceResult.Content.ReadAsStringAsync());

        var multipleChoiceResult = await client.PostAsync(
            $"{questionsUrl}/create",
            BuildForm(createToken, ScreeningQuestionType.MultipleChoice, "Select a word.",
                ("ChoiceOptionsText", "cat\ndog"), ("CorrectAnswer", "Dog"), ("ImagePath", "/images/demo.png")));
        Assert.Equal(HttpStatusCode.Redirect, multipleChoiceResult.StatusCode);

        var passageCreatePage = await client.GetAsync($"{questionsUrl}/create");
        var passageToken = ExtractAntiForgeryToken(await passageCreatePage.Content.ReadAsStringAsync());
        var invalidPassageResult = await client.PostAsync(
            $"{questionsUrl}/create",
            BuildForm(passageToken, ScreeningQuestionType.ReadingPassage, "Read the passage.",
                ("AcceptedAnswersText", "response")));
        Assert.Equal(HttpStatusCode.OK, invalidPassageResult.StatusCode);
        Assert.Contains("Enter the passage text", await invalidPassageResult.Content.ReadAsStringAsync());

        var passageResult = await client.PostAsync(
            $"{questionsUrl}/create",
            BuildForm(passageToken, ScreeningQuestionType.ReadingPassage, "Read the passage.",
                ("PassageText", "A short original demo passage."),
                ("AcceptedAnswersText", "response one\nresponse two")));
        Assert.Equal(HttpStatusCode.Redirect, passageResult.StatusCode);

        int multipleChoiceId;
        int passageQuestionId;
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var questions = await dbContext.ScreeningQuestions
                .Where(question => question.ScreeningTestId == screeningTestId)
                .OrderBy(question => question.SortOrder)
                .ToListAsync();

            Assert.Equal(2, questions.Count);
            var multipleChoice = questions.Single(question => question.QuestionType == ScreeningQuestionType.MultipleChoice);
            var passage = questions.Single(question => question.QuestionType == ScreeningQuestionType.ReadingPassage);
            multipleChoiceId = multipleChoice.Id;
            passageQuestionId = passage.Id;
            Assert.Equal(screeningTestId, multipleChoice.ScreeningTestId);
            Assert.Equal("dog", multipleChoice.CorrectAnswer);
            Assert.Equal("/images/demo.png", multipleChoice.ImagePath);
            Assert.Null(multipleChoice.AcceptedAnswersJson);

            using var optionsJson = JsonDocument.Parse(multipleChoice.MetadataJson!);
            Assert.Equal(new[] { "cat", "dog" }, optionsJson.RootElement.GetProperty("choiceOptions").EnumerateArray()
                .Select(option => option.GetString()).ToArray());

            Assert.Equal(screeningTestId, passage.ScreeningTestId);
            Assert.Equal("A short original demo passage.",
                JsonDocument.Parse(passage.MetadataJson!).RootElement.GetProperty("passageText").GetString());
            Assert.Equal(new[] { "response one", "response two" },
                JsonDocument.Parse(passage.AcceptedAnswersJson!).RootElement.EnumerateArray()
                    .Select(answer => answer.GetString()).ToArray());
        }

        var passageEditPage = await client.GetAsync($"{questionsUrl}/{passageQuestionId}/edit");
        var passageEditHtml = await passageEditPage.Content.ReadAsStringAsync();
        Assert.Contains("A short original demo passage.", passageEditHtml);
        Assert.Contains("response one", passageEditHtml);
        var editToken = ExtractAntiForgeryToken(passageEditHtml);
        var editResult = await client.PostAsync(
            $"{questionsUrl}/{passageQuestionId}/edit",
            BuildForm(editToken, ScreeningQuestionType.ReadingPassage, "Updated reading prompt.",
                ("Id", passageQuestionId.ToString()),
                ("PassageText", "Updated original passage."),
                ("AcceptedAnswersText", "updated answer")));
        Assert.Equal(HttpStatusCode.Redirect, editResult.StatusCode);

        var crossTestEdit = await client.GetAsync($"/admin/screening-tests/2/questions/{passageQuestionId}/edit");
        Assert.Equal(HttpStatusCode.NotFound, crossTestEdit.StatusCode);

        var moveToken = ExtractAntiForgeryToken(await (await client.GetAsync(questionsUrl)).Content.ReadAsStringAsync());
        var moveResult = await client.PostAsync(
            $"{questionsUrl}/{passageQuestionId}/move",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = moveToken,
                ["direction"] = "up"
            }));
        Assert.Equal(HttpStatusCode.Redirect, moveResult.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(1, (await dbContext.ScreeningQuestions.SingleAsync(question => question.Id == passageQuestionId)).SortOrder);
        }

        var actionToken = ExtractAntiForgeryToken(await (await client.GetAsync(questionsUrl)).Content.ReadAsStringAsync());
        var deactivateResult = await client.PostAsync(
            $"{questionsUrl}/{passageQuestionId}/deactivate",
            BuildActionForm(actionToken));
        Assert.Equal(HttpStatusCode.Redirect, deactivateResult.StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.False((await dbContext.ScreeningQuestions.SingleAsync(question => question.Id == passageQuestionId)).IsActive);
            Assert.NotNull(await dbContext.ScreeningQuestions.SingleOrDefaultAsync(question => question.Id == passageQuestionId));
        }

        actionToken = ExtractAntiForgeryToken(await (await client.GetAsync(questionsUrl)).Content.ReadAsStringAsync());
        var activateResult = await client.PostAsync(
            $"{questionsUrl}/{passageQuestionId}/activate",
            BuildActionForm(actionToken));
        Assert.Equal(HttpStatusCode.Redirect, activateResult.StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var passage = await dbContext.ScreeningQuestions.SingleAsync(question => question.Id == passageQuestionId);
            Assert.True(passage.IsActive);
            Assert.Equal("Updated reading prompt.", passage.Prompt);
            Assert.Equal("Updated original passage.",
                JsonDocument.Parse(passage.MetadataJson!).RootElement.GetProperty("passageText").GetString());
        }

        Assert.NotEqual(multipleChoiceId, passageQuestionId);
    }

    [Fact]
    public async Task Reading_fluency_configuration_round_trips_passage_count_and_optional_target()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (adminEmail, adminPassword) = await factory.CreateUserAsync(ApplicationRoles.Administrator);
        await SignInAsync(client, adminEmail, adminPassword);

        const int testId = 6;
        var questionsUrl = $"/admin/screening-tests/{testId}/questions";
        var createPage = await client.GetAsync($"{questionsUrl}/create");
        var createHtml = await createPage.Content.ReadAsStringAsync();
        Assert.Contains("Reading fluency", createHtml);
        Assert.Contains("Reference word count (optional)", createHtml);
        Assert.Contains("Optional target pace (words per minute)", createHtml);
        var antiForgeryToken = ExtractAntiForgeryToken(createHtml);

        var invalidResult = await client.PostAsync(
            $"{questionsUrl}/create",
            BuildForm(antiForgeryToken, ScreeningQuestionType.ReadingFluency, "Read this passage aloud."));
        Assert.Equal(HttpStatusCode.OK, invalidResult.StatusCode);
        Assert.Contains("Enter the passage text", await invalidResult.Content.ReadAsStringAsync());

        var createResult = await client.PostAsync(
            $"{questionsUrl}/create",
            BuildForm(antiForgeryToken, ScreeningQuestionType.ReadingFluency, "Read this passage aloud.",
                ("PassageText", "A short original fluency passage."),
                ("ReferenceWordCount", "6"),
                ("TargetWordsPerMinute", "120")));
        Assert.Equal(HttpStatusCode.Redirect, createResult.StatusCode);

        int questionId;
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var question = await dbContext.ScreeningQuestions.SingleAsync(item =>
                item.ScreeningTestId == testId && item.QuestionType == ScreeningQuestionType.ReadingFluency);
            questionId = question.Id;
            using var metadata = JsonDocument.Parse(question.MetadataJson!);
            Assert.Equal("A short original fluency passage.", metadata.RootElement.GetProperty("passageText").GetString());
            Assert.Equal(6, metadata.RootElement.GetProperty("referenceWordCount").GetInt32());
            Assert.Equal(120, metadata.RootElement.GetProperty("targetWordsPerMinute").GetInt32());
        }

        var editPage = await client.GetAsync($"{questionsUrl}/{questionId}/edit");
        var editHtml = await editPage.Content.ReadAsStringAsync();
        Assert.Contains("value=\"6\"", editHtml);
        Assert.Contains("value=\"120\"", editHtml);
        var editResult = await client.PostAsync(
            $"{questionsUrl}/{questionId}/edit",
            BuildForm(ExtractAntiForgeryToken(editHtml), ScreeningQuestionType.ReadingFluency, "Updated passage prompt.",
                ("Id", questionId.ToString()),
                ("PassageText", "Updated fluency passage."),
                ("ReferenceWordCount", "7")));
        Assert.Equal(HttpStatusCode.Redirect, editResult.StatusCode);

        using var verifyScope = factory.Services.CreateScope();
        var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var updatedQuestion = await verifyDbContext.ScreeningQuestions.SingleAsync(item => item.Id == questionId);
        using var updatedMetadata = JsonDocument.Parse(updatedQuestion.MetadataJson!);
        Assert.Equal("Updated fluency passage.", updatedMetadata.RootElement.GetProperty("passageText").GetString());
        Assert.Equal(7, updatedMetadata.RootElement.GetProperty("referenceWordCount").GetInt32());
        Assert.False(updatedMetadata.RootElement.TryGetProperty("targetWordsPerMinute", out _));
    }

    [Fact]
    public async Task Listening_comprehension_requires_a_safe_audio_file_name_and_supports_replay_limits()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (adminEmail, adminPassword) = await factory.CreateUserAsync(ApplicationRoles.Administrator);
        await SignInAsync(client, adminEmail, adminPassword);

        const int testId = 8;
        var questionsUrl = $"/admin/screening-tests/{testId}/questions";
        var createPage = await client.GetAsync($"{questionsUrl}/create");
        var createHtml = await createPage.Content.ReadAsStringAsync();
        var antiForgeryToken = ExtractAntiForgeryToken(createHtml);

        var invalidResult = await client.PostAsync(
            $"{questionsUrl}/create",
            BuildForm(antiForgeryToken, ScreeningQuestionType.ListeningResponse, "What did you hear?",
                ("AudioPath", "../private/story.mp3")));
        Assert.Equal(HttpStatusCode.OK, invalidResult.StatusCode);
        Assert.Contains("Folder paths and URLs are not allowed", await invalidResult.Content.ReadAsStringAsync());

        var createResult = await client.PostAsync(
            $"{questionsUrl}/create",
            BuildForm(antiForgeryToken, ScreeningQuestionType.ListeningResponse, "What did you hear?",
                ("AudioPath", "story.mp3"),
                ("MaxAudioReplays", "2")));
        Assert.Equal(HttpStatusCode.Redirect, createResult.StatusCode);

        var defaultReplayResult = await client.PostAsync(
            $"{questionsUrl}/create",
            BuildForm(antiForgeryToken, ScreeningQuestionType.ListeningResponse, "What happened next?",
                ("AudioPath", "second-story.wav")));
        Assert.Equal(HttpStatusCode.Redirect, defaultReplayResult.StatusCode);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var questions = await dbContext.ScreeningQuestions
            .Where(item => item.ScreeningTestId == testId && item.QuestionType == ScreeningQuestionType.ListeningResponse)
            .OrderBy(item => item.SortOrder)
            .ToListAsync();
        Assert.Equal(2, questions.Count);
        Assert.Equal("story.mp3", questions[0].AudioPath);
        using var metadata = JsonDocument.Parse(questions[0].MetadataJson!);
        Assert.Equal(2, metadata.RootElement.GetProperty("maxAudioReplays").GetInt32());
        Assert.Equal("second-story.wav", questions[1].AudioPath);
        Assert.Null(questions[1].MetadataJson);

        var editPage = await client.GetAsync($"{questionsUrl}/{questions[0].Id}/edit");
        Assert.Contains("value=\"2\"", await editPage.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Timed_naming_questions_require_twenty_items_and_round_trip_their_configuration()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (adminEmail, adminPassword) = await factory.CreateUserAsync(ApplicationRoles.Administrator);
        await SignInAsync(client, adminEmail, adminPassword);

        const int testId = 4;
        var questionsUrl = $"/admin/screening-tests/{testId}/questions";
        var createPage = await client.GetAsync($"{questionsUrl}/create");
        var createHtml = await createPage.Content.ReadAsStringAsync();
        Assert.Contains("Enter exactly 20 symbols, letters, numbers, or familiar words", createHtml);
        var antiForgeryToken = ExtractAntiForgeryToken(createHtml);
        var itemLines = Enumerable.Range(1, 20).Select(number => $"item-{number}").ToArray();

        var invalidResult = await client.PostAsync(
            $"{questionsUrl}/create",
            BuildForm(antiForgeryToken, ScreeningQuestionType.TimedNaming, "Name each item aloud.",
                ("TimeLimitSeconds", "60"),
                ("TimedNamingItemsText", string.Join('\n', itemLines.Take(19)))));
        Assert.Equal(HttpStatusCode.OK, invalidResult.StatusCode);
        Assert.Contains("Enter exactly 20 naming items", await invalidResult.Content.ReadAsStringAsync());

        var createResult = await client.PostAsync(
            $"{questionsUrl}/create",
            BuildForm(antiForgeryToken, ScreeningQuestionType.TimedNaming, "Name each item aloud.",
                ("TimeLimitSeconds", "60"),
                ("TimedNamingItemsText", string.Join('\n', itemLines))));
        Assert.Equal(HttpStatusCode.Redirect, createResult.StatusCode);

        int questionId;
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var question = await dbContext.ScreeningQuestions.SingleAsync(item =>
                item.ScreeningTestId == testId && item.QuestionType == ScreeningQuestionType.TimedNaming);
            questionId = question.Id;
            using var metadata = JsonDocument.Parse(question.MetadataJson!);
            Assert.Equal(itemLines, metadata.RootElement.GetProperty("timedNamingItems").EnumerateArray()
                .Select(item => item.GetString()).ToArray());
            Assert.Equal(60, metadata.RootElement.GetProperty("timeLimitSeconds").GetInt32());
        }

        var editPage = await client.GetAsync($"{questionsUrl}/{questionId}/edit");
        var editHtml = await editPage.Content.ReadAsStringAsync();
        Assert.Contains("item-1", editHtml);
        Assert.Contains("item-20", editHtml);
    }

    [Fact]
    public async Task New_cognitive_question_types_round_trip_metadata_without_activating_optional_category()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (adminEmail, adminPassword) = await factory.CreateUserAsync(ApplicationRoles.Administrator);
        await SignInAsync(client, adminEmail, adminPassword);

        const int testId = 9;
        var questionsUrl = $"/admin/screening-tests/{testId}/questions";
        var createPage = await client.GetAsync($"{questionsUrl}/create");
        var createHtml = await createPage.Content.ReadAsStringAsync();
        Assert.Contains("Working memory sequence", createHtml);
        Assert.Contains("Timed matching", createHtml);
        Assert.Contains("Pattern reasoning", createHtml);
        var token = ExtractAntiForgeryToken(createHtml);

        var workingMemoryResult = await client.PostAsync(
            $"{questionsUrl}/create",
            BuildForm(token, ScreeningQuestionType.WorkingMemorySequence, "Remember the sequence.",
                ("CorrectAnswer", "red blue green"),
                ("WorkingMemorySequenceText", "red\nblue\ngreen"),
                ("MemoryDisplayDurationMilliseconds", "2500"),
                ("AcceptedAnswersText", "red, blue, green")));
        Assert.Equal(HttpStatusCode.Redirect, workingMemoryResult.StatusCode);

        var timedMatchingPage = await client.GetAsync($"{questionsUrl}/create");
        var timedMatchingHtml = await timedMatchingPage.Content.ReadAsStringAsync();
        var timedMatchingToken = ExtractAntiForgeryToken(timedMatchingHtml);
        var invalidTimedMatchingResult = await client.PostAsync(
            $"{questionsUrl}/create",
            BuildForm(timedMatchingToken, ScreeningQuestionType.TimedMatching, "Choose the matching shape.",
                ("ChoiceOptionsText", "circle\nsquare"),
                ("TimeLimitSeconds", "20")));
        Assert.Equal(HttpStatusCode.OK, invalidTimedMatchingResult.StatusCode);
        Assert.Contains("Enter at least two reference pairs", await invalidTimedMatchingResult.Content.ReadAsStringAsync());

        timedMatchingPage = await client.GetAsync($"{questionsUrl}/create");
        timedMatchingToken = ExtractAntiForgeryToken(await timedMatchingPage.Content.ReadAsStringAsync());
        var timedMatchingResult = await client.PostAsync(
            $"{questionsUrl}/create",
            BuildForm(timedMatchingToken, ScreeningQuestionType.TimedMatching, "Choose the matching shape.",
                ("ChoiceOptionsText", "circle\nsquare"),
                ("TimedMatchingReferenceMappingText", "A = circle\nB = square"),
                ("TimedMatchingItemsText", "A\nB\nA"),
                ("TimeLimitSeconds", "20")));
        Assert.Equal(HttpStatusCode.Redirect, timedMatchingResult.StatusCode);

        var patternPage = await client.GetAsync($"{questionsUrl}/create");
        var patternToken = ExtractAntiForgeryToken(await patternPage.Content.ReadAsStringAsync());
        var patternResult = await client.PostAsync(
            $"{questionsUrl}/create",
            BuildForm(patternToken, ScreeningQuestionType.PatternReasoning, "Which shape completes the pattern?",
                ("ChoiceOptionsText", "triangle\nsquare"),
                ("CorrectAnswer", "triangle"),
                ("PatternDifficultyLevel", "2"),
                ("ImagePath", "/images/original-pattern.svg")));
        Assert.Equal(HttpStatusCode.Redirect, patternResult.StatusCode);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var category = await dbContext.ScreeningTests.SingleAsync(test => test.Id == testId);
        Assert.False(category.IsActive);

        var questions = await dbContext.ScreeningQuestions
            .Where(question => question.ScreeningTestId == testId)
            .OrderBy(question => question.SortOrder)
            .ToListAsync();
        Assert.Equal(new[]
        {
            ScreeningQuestionType.WorkingMemorySequence,
            ScreeningQuestionType.TimedMatching,
            ScreeningQuestionType.PatternReasoning
        }, questions.Select(question => question.QuestionType));
        Assert.Equal("/images/original-pattern.svg", questions[2].ImagePath);
        using var patternMetadata = JsonDocument.Parse(questions[2].MetadataJson!);
        Assert.Equal(2, patternMetadata.RootElement.GetProperty("patternDifficultyLevel").GetInt32());

        using var workingMemoryMetadata = JsonDocument.Parse(questions[0].MetadataJson!);
        Assert.Equal(new[] { "red", "blue", "green" }, workingMemoryMetadata.RootElement
            .GetProperty("workingMemorySequence").EnumerateArray().Select(item => item.GetString()).ToArray());
        Assert.Equal(2500, workingMemoryMetadata.RootElement.GetProperty("memoryDisplayDurationMilliseconds").GetInt32());

        using var timedMatchingMetadata = JsonDocument.Parse(questions[1].MetadataJson!);
        Assert.Equal(20, timedMatchingMetadata.RootElement.GetProperty("timeLimitSeconds").GetInt32());
        Assert.Equal(new[] { "circle", "square" }, timedMatchingMetadata.RootElement
            .GetProperty("choiceOptions").EnumerateArray().Select(item => item.GetString()).ToArray());
        Assert.Equal(new[] { "A", "B", "A" }, timedMatchingMetadata.RootElement
            .GetProperty("timedMatchingItems").EnumerateArray().Select(item => item.GetString()).ToArray());
        Assert.Equal(
            new[] { "A", "B" },
            timedMatchingMetadata.RootElement.GetProperty("timedMatchingReferenceMapping").EnumerateObject()
                .Select(item => item.Name).ToArray());

        var timedMatchingEditPage = await client.GetAsync($"{questionsUrl}/{questions[1].Id}/edit");
        var timedMatchingEditHtml = await timedMatchingEditPage.Content.ReadAsStringAsync();
        Assert.Contains("A = circle", timedMatchingEditHtml);
        Assert.Contains("A", timedMatchingEditHtml);
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
        ScreeningQuestionType questionType,
        string prompt,
        params (string Key, string Value)[] fields)
    {
        var values = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["QuestionType"] = ((int)questionType).ToString(),
            ["Prompt"] = prompt
        };

        foreach (var (key, value) in fields)
        {
            values[key] = value;
        }

        return new FormUrlEncodedContent(values);
    }

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
