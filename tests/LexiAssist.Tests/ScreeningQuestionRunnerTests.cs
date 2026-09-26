using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Identity;
using LexiAssist.Web.Models.Reports;
using LexiAssist.Web.Models.Screening;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LexiAssist.Tests;

public sealed class ScreeningQuestionRunnerTests
{
    [Fact]
    public async Task Learner_results_show_completed_optional_category_separately_with_neutral_wording()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        var (email, password) = await factory.CreateUserAsync(ApplicationRoles.Learner);
        int attemptId;

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>();
            var learner = await userManager.FindByEmailAsync(email);
            Assert.NotNull(learner);

            var attempt = new ScreeningAttempt
            {
                UserId = learner!.Id,
                StartedAtUtc = DateTime.UtcNow.AddMinutes(-5),
                CompletedAtUtc = DateTime.UtcNow,
                Status = ScreeningAttemptStatus.Completed,
                OverallScore = 78,
                RiskTier = "Amber"
            };
            dbContext.ScreeningAttempts.Add(attempt);
            await dbContext.SaveChangesAsync();

            var report = new ScreeningReport
            {
                ScreeningAttemptId = attempt.Id,
                UserId = learner.Id,
                GeneratedAtUtc = DateTime.UtcNow,
                RiskTier = "Amber",
                Summary = "This is a screening summary.",
                RecommendedNextAction = "A suitable support professional may review this screening according to the LexiAssist support workflow.",
                ScoringVersion = "1.0",
                IsReferralRecommended = true,
                SkillAreasJson = JsonSerializer.Serialize(new[]
                {
                    new ScreeningReportSkillAreaSnapshot("Word Reading", 90m, true, false, "A relative strength in this screening."),
                    new ScreeningReportSkillAreaSnapshot("Memory and Broader Thinking Skills", 72m, false, true, "This category met the configured rule for additional support review.")
                    {
                        ActivityMetrics =
                        [
                            new ScreeningActivityMetric(ScreeningQuestionType.WorkingMemorySequence, "Working Memory", 3, 2, 1, 3, 66.67m, 2, null, null, null, 2m, 3m, 66.67m),
                            new ScreeningActivityMetric(ScreeningQuestionType.TimedMatching, "Processing Speed", 4, 3, 1, 4, 75m, null, 1200, null, null, 3m, 4m, 75m),
                            new ScreeningActivityMetric(ScreeningQuestionType.PatternReasoning, "Pattern Reasoning", 1, 1, 0, 1, 100m, null, null, null, null, 1m, 1m, 100m)
                        ]
                    }
                })
            };
            dbContext.ScreeningReports.Add(report);
            await dbContext.SaveChangesAsync();
            attemptId = attempt.Id;
        }

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(client, email, password);

        var response = await client.GetAsync($"/screening/results/{attemptId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("Skill-area summary", html);
        Assert.Contains("Word Reading", html);
        Assert.Contains("Memory and Broader Thinking Skills", html);
        Assert.Contains("Activity completion", html);
        Assert.Contains("Working Memory", html);
        Assert.Contains("Processing Efficiency", html);
        Assert.Contains("Pattern Reasoning", html);
        Assert.Contains("screening information only", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Areas that may benefit from additional support", html);
        Assert.Contains("A suitable support professional may review this screening according to the LexiAssist support workflow.", html);
        Assert.DoesNotContain("low intelligence", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cognitive impairment", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dyslexia confirmed", html, StringComparison.OrdinalIgnoreCase);

        var generalSummaryStart = html.IndexOf("id=\"skill-area-heading\"", StringComparison.Ordinal);
        var optionalSectionStart = html.IndexOf("id=\"memory-broader-thinking-heading\"", StringComparison.Ordinal);
        Assert.True(generalSummaryStart >= 0);
        Assert.True(optionalSectionStart > generalSummaryStart);
        Assert.DoesNotContain("Memory and Broader Thinking Skills", html[generalSummaryStart..optionalSectionStart], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Timed_naming_randomizes_twenty_items_and_persists_duration_and_self_reported_errors()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        var configuredItems = Enumerable.Range(1, 20).Select(number => $"item-{number}").ToArray();
        await AddQuestionAsync(factory, new ScreeningQuestion
        {
            ScreeningTestId = 4,
            Prompt = "Name each item aloud in order.",
            QuestionType = ScreeningQuestionType.TimedNaming,
            SortOrder = 1,
            MetadataJson = JsonSerializer.Serialize(new ScreeningQuestionMetadata
            {
                TimeLimitSeconds = 600,
                TimedNamingItems = configuredItems.ToList()
            })
        });

        var (email, password) = await factory.CreateUserAsync(ApplicationRoles.Learner);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(client, email, password);
        await StartScreeningAsync(client);

        var activityPage = await GetQuestionHtmlAsync(client, "Name each item aloud in order.");
        Assert.Contains("Rapid Automated Naming", activityPage);
        Assert.Contains("accurate naming", activityPage);
        Assert.Contains("efficient pace", activityPage);
        Assert.Contains("does not mean greater ability or intelligence", activityPage);
        Assert.Contains("Elapsed time", activityPage);
        Assert.Contains("600 seconds", activityPage);
        Assert.Contains("I made an error on this item", activityPage);
        Assert.Contains("Name the 20 items aloud", activityPage);

        var startResponse = await client.PostAsync("/screening/timed-naming/start", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiForgeryToken(activityPage),
            ["QuestionToken"] = ExtractInputValue(activityPage, "QuestionToken")
        }));
        Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);
        using var startJson = JsonDocument.Parse(await startResponse.Content.ReadAsStringAsync());
        var randomizedItems = startJson.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetString()!)
            .ToArray();
        var runToken = startJson.RootElement.GetProperty("runToken").GetString();
        Assert.NotNull(runToken);
        Assert.Equal(20, randomizedItems.Length);
        Assert.Equal(configuredItems.Order(), randomizedItems.Order());

        var earlyPartialResponse = await client.PostAsync("/screening/timed-naming/finish", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiForgeryToken(activityPage),
            ["RunToken"] = runToken,
            ["CompletedOrderJson"] = JsonSerializer.Serialize(Enumerable.Range(0, 19)),
            ["SelfReportedErrorIndexesJson"] = JsonSerializer.Serialize(new[] { 3, 18 })
        }));
        Assert.Equal(HttpStatusCode.BadRequest, earlyPartialResponse.StatusCode);
        Assert.Equal(0, await GetResponseCountAsync(factory));

        var completionForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiForgeryToken(activityPage),
            ["RunToken"] = runToken,
            ["CompletedOrderJson"] = JsonSerializer.Serialize(Enumerable.Range(0, 20)),
            ["SelfReportedErrorIndexesJson"] = JsonSerializer.Serialize(new[] { 3, 18 })
        });
        var finishResponse = await client.PostAsync("/screening/timed-naming/finish", completionForm);
        Assert.Equal(HttpStatusCode.OK, finishResponse.StatusCode);
        Assert.Contains("continueUrl", await finishResponse.Content.ReadAsStringAsync());

        var replayResponse = await client.PostAsync("/screening/timed-naming/finish", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiForgeryToken(activityPage),
            ["RunToken"] = runToken,
            ["CompletedOrderJson"] = JsonSerializer.Serialize(Enumerable.Range(0, 20)),
            ["SelfReportedErrorIndexesJson"] = JsonSerializer.Serialize(new[] { 3, 18 })
        }));
        Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
        Assert.Equal(1, await GetResponseCountAsync(factory));

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>();
        var learner = await userManager.FindByEmailAsync(email);
        Assert.NotNull(learner);
        var attempt = await dbContext.ScreeningAttempts.SingleAsync(item => item.UserId == learner.Id);
        var testAttempt = await dbContext.ScreeningTestAttempts.SingleAsync(item => item.ScreeningAttemptId == attempt.Id);
        var response = await dbContext.ScreeningResponses.SingleAsync(item => item.ScreeningTestAttemptId == testAttempt.Id);

        Assert.Equal(ScreeningQuestionType.TimedNaming, await dbContext.ScreeningQuestions
            .Where(question => question.Id == response.ScreeningQuestionId)
            .Select(question => question.QuestionType)
            .SingleAsync());
        Assert.True(response.ResponseTimeMilliseconds >= 0);
        Assert.Equal(testAttempt.DurationMilliseconds, response.ResponseTimeMilliseconds);
        Assert.Null(response.TextAnswer);
        Assert.Null(response.SelectedAnswer);
        Assert.Null(response.IsCorrect);
        Assert.Null(response.Score);

        using var detailsJson = JsonDocument.Parse(response.ResponseDetailsJson!);
        Assert.Equal(randomizedItems, detailsJson.RootElement.GetProperty("presentationOrder").EnumerateArray()
            .Select(item => item.GetString()!).ToArray());
        Assert.Equal(20, detailsJson.RootElement.GetProperty("completedItemCount").GetInt32());
        Assert.False(detailsJson.RootElement.GetProperty("timeLimitReached").GetBoolean());
        Assert.Equal(new[] { 3, 18 }, detailsJson.RootElement.GetProperty("selfReportedErrorIndexes").EnumerateArray()
            .Select(item => item.GetInt32()).ToArray());

        var completed = await client.GetAsync("/screening/resume");
        Assert.Equal(HttpStatusCode.Redirect, completed.StatusCode);
        Assert.StartsWith("/screening/results/", completed.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Reading_fluency_uses_server_timing_and_persists_neutral_metrics_without_scoring()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        const string passageText = "Mira walked to the bright green garden today.";
        await AddQuestionAsync(factory, new ScreeningQuestion
        {
            ScreeningTestId = 6,
            Prompt = "Read this passage aloud.",
            QuestionType = ScreeningQuestionType.ReadingFluency,
            SortOrder = 1,
            MetadataJson = JsonSerializer.Serialize(new ScreeningQuestionMetadata
            {
                PassageText = passageText,
                ReferenceWordCount = 8,
                TargetWordsPerMinute = 95
            })
        });

        var (email, password) = await factory.CreateUserAsync(ApplicationRoles.Learner);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(client, email, password);
        await StartScreeningAsync(client);

        var activityPage = await GetQuestionHtmlAsync(client, "Read this passage aloud.");
        Assert.Contains(passageText, activityPage);
        Assert.Contains("Start reading", activityPage);
        Assert.Contains("I have finished reading", activityPage);
        Assert.Contains("Optional: how many times did you correct a word", activityPage);
        Assert.Contains("estimated words per minute", activityPage);
        Assert.Contains("do not diagnose dyslexia", activityPage);
        Assert.DoesNotContain("95 words per minute", activityPage);

        var startResponse = await client.PostAsync("/screening/reading-fluency/start", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiForgeryToken(activityPage),
            ["QuestionToken"] = ExtractInputValue(activityPage, "QuestionToken")
        }));
        Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);
        using var startJson = JsonDocument.Parse(await startResponse.Content.ReadAsStringAsync());
        var runToken = startJson.RootElement.GetProperty("runToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(runToken));
        await Task.Delay(30);

        var invalidFinishResponse = await client.PostAsync("/screening/reading-fluency/finish", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiForgeryToken(activityPage),
            ["RunToken"] = runToken!,
            ["CorrectionCount"] = "5001"
        }));
        Assert.Equal(HttpStatusCode.BadRequest, invalidFinishResponse.StatusCode);
        Assert.Equal(0, await GetResponseCountAsync(factory));

        var (otherEmail, otherPassword) = await factory.CreateUserAsync(ApplicationRoles.Learner);
        using var otherClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(otherClient, otherEmail, otherPassword);
        await StartScreeningAsync(otherClient);
        var otherHtml = await GetQuestionHtmlAsync(otherClient, "Read this passage aloud.");
        var foreignTokenResponse = await otherClient.PostAsync("/screening/reading-fluency/finish", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiForgeryToken(otherHtml),
            ["RunToken"] = runToken!,
            ["CorrectionCount"] = "1"
        }));
        Assert.Equal(HttpStatusCode.BadRequest, foreignTokenResponse.StatusCode);

        var finishResponse = await client.PostAsync("/screening/reading-fluency/finish", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiForgeryToken(activityPage),
            ["RunToken"] = runToken!,
            ["CorrectionCount"] = "2"
        }));
        Assert.Equal(HttpStatusCode.OK, finishResponse.StatusCode);

        var replayResponse = await client.PostAsync("/screening/reading-fluency/finish", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiForgeryToken(activityPage),
            ["RunToken"] = runToken!,
            ["CorrectionCount"] = "2"
        }));
        Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
        Assert.Equal(1, await GetResponseCountAsync(factory));

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>();
        var learner = await userManager.FindByEmailAsync(email);
        Assert.NotNull(learner);
        var attempt = await dbContext.ScreeningAttempts.SingleAsync(item => item.UserId == learner.Id);
        var testAttempt = await dbContext.ScreeningTestAttempts.SingleAsync(item => item.ScreeningAttemptId == attempt.Id);
        var response = await dbContext.ScreeningResponses.SingleAsync(item => item.ScreeningTestAttemptId == testAttempt.Id);

        Assert.True(response.ResponseTimeMilliseconds > 0);
        Assert.Equal(response.ResponseTimeMilliseconds, testAttempt.DurationMilliseconds);
        Assert.Null(response.TextAnswer);
        Assert.Null(response.SelectedAnswer);
        Assert.Null(response.IsCorrect);
        Assert.Null(response.Score);
        using var details = JsonDocument.Parse(response.ResponseDetailsJson!);
        Assert.Equal(8, details.RootElement.GetProperty("referenceWordCount").GetInt32());
        Assert.Equal(95, details.RootElement.GetProperty("targetWordsPerMinute").GetInt32());
        Assert.Equal(2, details.RootElement.GetProperty("selfReportedCorrectionCount").GetInt32());
        Assert.Equal(
            Math.Round(8 * 60000d / response.ResponseTimeMilliseconds!.Value, 2),
            details.RootElement.GetProperty("wordsPerMinute").GetDouble());

        var completed = await client.GetAsync("/screening/resume");
        Assert.Equal(HttpStatusCode.Redirect, completed.StatusCode);
        Assert.StartsWith("/screening/results/", completed.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Reading_and_listening_comprehension_show_stimuli_and_save_answers_with_their_tests()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        var mediaFileName = $"comprehension-{Guid.NewGuid():N}.mp3";
        var audioBytes = new byte[] { 0x49, 0x44, 0x33, 0x03, 0x00, 0x00, 0x00, 0x00 };
        string audioPath;
        using (var scope = factory.Services.CreateScope())
        {
            var environment = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
            var audioFolder = Path.Combine(environment.ContentRootPath, "App_Data", "ScreeningAudio");
            Directory.CreateDirectory(audioFolder);
            audioPath = Path.Combine(audioFolder, mediaFileName);
        }

        await File.WriteAllBytesAsync(audioPath, audioBytes);
        try
        {
            const string passage = "Tari found a small blue shell beside the quiet shore.";
            await AddQuestionAsync(factory, new ScreeningQuestion
            {
                ScreeningTestId = 7,
                Prompt = "Where did Tari find the shell?",
                QuestionType = ScreeningQuestionType.ReadingPassage,
                SortOrder = 1,
                MetadataJson = JsonSerializer.Serialize(new ScreeningQuestionMetadata { PassageText = passage })
            });
            await AddQuestionAsync(factory, new ScreeningQuestion
            {
                ScreeningTestId = 7,
                Prompt = "What color was the shell?",
                QuestionType = ScreeningQuestionType.ReadingPassage,
                SortOrder = 2,
                MetadataJson = JsonSerializer.Serialize(new ScreeningQuestionMetadata { PassageText = passage })
            });
            await AddQuestionAsync(factory, new ScreeningQuestion
            {
                ScreeningTestId = 8,
                Prompt = "What did the speaker pack?",
                QuestionType = ScreeningQuestionType.ListeningResponse,
                SortOrder = 1,
                AudioPath = mediaFileName,
                MetadataJson = JsonSerializer.Serialize(new ScreeningQuestionMetadata { MaxAudioReplays = 2 })
            });
            await AddQuestionAsync(factory, new ScreeningQuestion
            {
                ScreeningTestId = 8,
                Prompt = "Where did the speaker go?",
                QuestionType = ScreeningQuestionType.ListeningResponse,
                SortOrder = 2,
                AudioPath = mediaFileName
            });

            var (email, password) = await factory.CreateUserAsync(ApplicationRoles.Learner);
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            await SignInAsync(client, email, password);
            await StartScreeningAsync(client);

            var firstReadingQuestion = await GetQuestionHtmlAsync(client, "Where did Tari find the shell?");
            Assert.Contains(passage, firstReadingQuestion);
            var firstReadingResponse = await client.PostAsync("/screening/respond", CreateAnswerPost(firstReadingQuestion, ("TextAnswer", "Beside the quiet shore.")));
            Assert.Equal(HttpStatusCode.Redirect, firstReadingResponse.StatusCode);

            var secondReadingQuestion = await GetQuestionHtmlAsync(client, "What color was the shell?");
            Assert.Contains(passage, secondReadingQuestion);
            var secondReadingResponse = await client.PostAsync("/screening/respond", CreateAnswerPost(secondReadingQuestion, ("TextAnswer", "Blue.")));
            Assert.Equal(HttpStatusCode.Redirect, secondReadingResponse.StatusCode);

            var firstListeningQuestion = await GetQuestionHtmlAsync(client, "What did the speaker pack?");
            Assert.Contains("You may replay the audio up to 2 time(s)", firstListeningQuestion);
            Assert.Contains("screening-listening-comprehension.js", firstListeningQuestion);
            var audioSourceMatch = Regex.Match(firstListeningQuestion, "<audio[^>]*src=\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase);
            Assert.True(audioSourceMatch.Success, "The question should link to its protected audio source.");
            var audioResponse = await client.GetAsync(WebUtility.HtmlDecode(audioSourceMatch.Groups[1].Value));
            Assert.Equal(HttpStatusCode.OK, audioResponse.StatusCode);
            Assert.Equal("audio/mpeg", audioResponse.Content.Headers.ContentType?.MediaType);
            Assert.Contains("no-store", audioResponse.Headers.CacheControl?.ToString());
            Assert.Equal("nosniff", audioResponse.Headers.GetValues("X-Content-Type-Options").Single());
            Assert.Equal(audioBytes, await audioResponse.Content.ReadAsByteArrayAsync());
            var firstListeningResponse = await client.PostAsync("/screening/respond", CreateAnswerPost(firstListeningQuestion, ("TextAnswer", "A lunch bag.")));
            Assert.Equal(HttpStatusCode.Redirect, firstListeningResponse.StatusCode);

            var secondListeningQuestion = await GetQuestionHtmlAsync(client, "Where did the speaker go?");
            Assert.Contains("You may replay the audio as needed", secondListeningQuestion);
            var secondListeningResponse = await client.PostAsync("/screening/respond", CreateAnswerPost(secondListeningQuestion, ("TextAnswer", "The library.")));
            Assert.Equal(HttpStatusCode.Redirect, secondListeningResponse.StatusCode);
            var completedResult = await client.GetAsync("/screening/resume");
            Assert.Equal(HttpStatusCode.Redirect, completedResult.StatusCode);
            Assert.StartsWith("/screening/results/", completedResult.Headers.Location?.OriginalString);

            using var verifyScope = factory.Services.CreateScope();
            var dbContext = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var learner = await verifyScope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>()
                .FindByEmailAsync(email);
            Assert.NotNull(learner);
            var responses = await dbContext.ScreeningResponses
                .Include(response => response.ScreeningQuestion)
                .Include(response => response.ScreeningTestAttempt)
                .Where(response => response.ScreeningTestAttempt.ScreeningAttempt.UserId == learner.Id)
                .OrderBy(response => response.ScreeningQuestion.ScreeningTestId)
                .ThenBy(response => response.ScreeningQuestion.SortOrder)
                .ToListAsync();
            Assert.Equal(4, responses.Count);
            Assert.Equal(new[] { 7, 7, 8, 8 }, responses.Select(response => response.ScreeningTestAttempt.ScreeningTestId));
            Assert.Equal(new[] { "Beside the quiet shore.", "Blue.", "A lunch bag.", "The library." },
                responses.Select(response => response.TextAnswer));
        }
        finally
        {
            File.Delete(audioPath);
        }
    }

    [Fact]
    public async Task Missing_listening_audio_shows_a_helpful_message()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        await AddQuestionAsync(factory, new ScreeningQuestion
        {
            ScreeningTestId = 8,
            Prompt = "What did you hear?",
            QuestionType = ScreeningQuestionType.ListeningResponse,
            SortOrder = 1,
            AudioPath = "missing-screening-audio.mp3"
        });

        var (email, password) = await factory.CreateUserAsync(ApplicationRoles.Learner);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(client, email, password);
        await StartScreeningAsync(client);

        var response = await client.GetAsync("/screening/resume");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("The configured audio file is missing or unavailable", html);
        Assert.Contains("Your progress is saved", html);
    }

    [Fact]
    public async Task Learner_can_answer_supported_question_types_in_order_and_receive_non_diagnostic_support_tier()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        await SeedQuestionSetAsync(factory);
        var (email, password) = await factory.CreateUserAsync(ApplicationRoles.Learner);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(client, email, password);

        await StartScreeningAsync(client);

        var firstQuestion = await client.GetAsync("/screening/resume");
        Assert.Equal(HttpStatusCode.OK, firstQuestion.StatusCode);
        var firstHtml = await firstQuestion.Content.ReadAsStringAsync();
        Assert.Contains("Which one is the sun?", firstHtml);
        Assert.Contains("Question 1 of 6", firstHtml);
        Assert.Contains("aria-valuenow=\"17\"", firstHtml);
        Assert.DoesNotContain("Inactive question", firstHtml);
        Assert.DoesNotContain("Unsupported reading-passage question", firstHtml);
        Assert.DoesNotContain("Disabled category question", firstHtml);

        var firstPost = CreateAnswerPost(firstHtml, ("SelectedAnswer", "sun"));
        var rejectedChoice = await client.PostAsync("/screening/respond", CreateAnswerPost(firstHtml, ("SelectedAnswer", "not-an-option")));
        Assert.Equal(HttpStatusCode.OK, rejectedChoice.StatusCode);
        Assert.Contains("Choose one of the listed answers", await rejectedChoice.Content.ReadAsStringAsync());
        Assert.Equal(0, await GetResponseCountAsync(factory));

        var acceptedChoice = await client.PostAsync("/screening/respond", firstPost);
        Assert.Equal(HttpStatusCode.Redirect, acceptedChoice.StatusCode);
        Assert.Equal("/screening/resume", acceptedChoice.Headers.Location?.OriginalString);

        var replayedPost = await client.PostAsync("/screening/respond", CreateAnswerPost(firstHtml, ("SelectedAnswer", "sun")));
        Assert.Equal(HttpStatusCode.Redirect, replayedPost.StatusCode);
        Assert.Equal(1, await GetResponseCountAsync(factory));

        var textQuestion = await GetQuestionHtmlAsync(client, "What did you notice?");
        Assert.Contains("Question 2 of 6", textQuestion);
        var textResponse = await client.PostAsync("/screening/respond", CreateAnswerPost(textQuestion, ("TextAnswer", "A bright circle")));
        Assert.Equal(HttpStatusCode.Redirect, textResponse.StatusCode);

        var spellingQuestion = await GetQuestionHtmlAsync(client, "Type the word shown:");
        Assert.Contains("Question 3 of 6", spellingQuestion);
        var spellingResponse = await client.PostAsync("/screening/respond", CreateAnswerPost(spellingQuestion, ("TextAnswer", "garden")));
        Assert.Equal(HttpStatusCode.Redirect, spellingResponse.StatusCode);

        var phonologicalQuestion = await GetQuestionHtmlAsync(client, "Which word begins with /m/?");
        Assert.Contains("Question 4 of 6", phonologicalQuestion);
        var phonologicalResponse = await client.PostAsync("/screening/respond", CreateAnswerPost(phonologicalQuestion, ("SelectedAnswer", "moon")));
        Assert.Equal(HttpStatusCode.Redirect, phonologicalResponse.StatusCode);

        var wordReadingQuestion = await GetQuestionHtmlAsync(client, "river");
        Assert.Contains("Question 5 of 6", wordReadingQuestion);
        Assert.Contains("Read the word", wordReadingQuestion);
        Assert.Contains("does not record your voice or automatically evaluate pronunciation", wordReadingQuestion);
        var wordReadingResponse = await client.PostAsync("/screening/respond", CreateAnswerPost(wordReadingQuestion, ("TextAnswer", "river")));
        Assert.Equal(HttpStatusCode.Redirect, wordReadingResponse.StatusCode);

        var nonWordReadingQuestion = await GetQuestionHtmlAsync(client, "vop");
        Assert.Contains("Question 6 of 6", nonWordReadingQuestion);
        Assert.Contains("Read this made-up word", nonWordReadingQuestion);
        var nonWordReadingResponse = await client.PostAsync("/screening/respond", CreateAnswerPost(nonWordReadingQuestion, ("TextAnswer", "vop")));
        Assert.Equal(HttpStatusCode.Redirect, nonWordReadingResponse.StatusCode);

        var completed = await client.GetAsync("/screening/resume");
        Assert.Equal(HttpStatusCode.Redirect, completed.StatusCode);
        var resultPath = completed.Headers.Location?.OriginalString;
        Assert.StartsWith("/screening/results/", resultPath);
        var resultResponse = await client.GetAsync(resultPath);
        Assert.Equal(HttpStatusCode.OK, resultResponse.StatusCode);
        var completedHtml = await resultResponse.Content.ReadAsStringAsync();
        Assert.Contains("Screening complete", completedHtml);
        Assert.Contains("Screening/support tier", completedHtml);
        Assert.Contains("Green", completedHtml);
        Assert.Contains("do not provide a clinical diagnosis", completedHtml);
        Assert.Contains("Skill-area summary", completedHtml);
        Assert.Contains("Potential strengths", completedHtml);
        Assert.Contains("Word Reading", completedHtml);
        Assert.Contains("100% matched", completedHtml);
        Assert.Contains("Continue with learning supports that work for you.", completedHtml);
        Assert.DoesNotContain("You have dyslexia.", completedHtml, StringComparison.OrdinalIgnoreCase);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>();
        var learner = await userManager.FindByEmailAsync(email);
        Assert.NotNull(learner);

        var attempt = await dbContext.ScreeningAttempts
            .Include(item => item.TestAttempts)
            .ThenInclude(item => item.Responses)
            .SingleAsync(item => item.UserId == learner.Id);
        Assert.Equal(ScreeningAttemptStatus.Completed, attempt.Status);
        Assert.NotNull(attempt.CompletedAtUtc);
        Assert.Equal(100d, attempt.OverallScore);
        Assert.Equal("Green", attempt.RiskTier);
        Assert.Equal("1.0", attempt.RiskCalculationVersion);
        Assert.NotNull(attempt.RiskCalculatedAtUtc);

        var testAttempt = Assert.Single(attempt.TestAttempts);
        Assert.NotNull(testAttempt.CompletedAtUtc);
        Assert.Equal(4d, testAttempt.RawScore);
        Assert.Equal(100d, testAttempt.PercentageScore);
        Assert.False(testAttempt.HasFlaggedSkillArea);
        Assert.Equal(6, testAttempt.Responses.Count);

        var report = await dbContext.ScreeningReports.SingleAsync(item => item.ScreeningAttemptId == attempt.Id);
        Assert.Equal(learner.Id, report.UserId);
        Assert.Equal("Green", report.RiskTier);
        Assert.Equal("1.0", report.ScoringVersion);
        Assert.False(report.IsReferralRecommended);
        Assert.Contains("100%", report.Summary);
        Assert.Contains("not a clinical diagnosis", report.Summary, StringComparison.OrdinalIgnoreCase);
        using (var reportAreas = JsonDocument.Parse(report.SkillAreasJson))
        {
            var savedArea = Assert.Single(reportAreas.RootElement.EnumerateArray().ToArray());
            Assert.Equal("Word Reading", savedArea.GetProperty("name").GetString());
            Assert.Equal(100m, savedArea.GetProperty("percentageScore").GetDecimal());
            Assert.True(savedArea.GetProperty("isStrength").GetBoolean());
            Assert.False(savedArea.GetProperty("isFlaggedForSupport").GetBoolean());
        }
        Assert.Equal("[]", report.FlaggedAreasJson);

        var otherCredentials = await factory.CreateUserAsync(ApplicationRoles.Learner);
        using var otherClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(otherClient, otherCredentials.Email, otherCredentials.Password);
        var foreignResultResponse = await otherClient.GetAsync(resultPath);
        Assert.Equal(HttpStatusCode.NotFound, foreignResultResponse.StatusCode);

        var dashboardResponse = await client.GetAsync("/Dashboard/Learner");
        Assert.Equal(HttpStatusCode.OK, dashboardResponse.StatusCode);
        var dashboardHtml = await dashboardResponse.Content.ReadAsStringAsync();
        Assert.Contains("Most recent screening result", dashboardHtml);
        Assert.Contains("View screening result", dashboardHtml);

        var savedResponses = await dbContext.ScreeningResponses
            .AsNoTracking()
            .Include(response => response.ScreeningQuestion)
            .Where(response => response.ScreeningTestAttempt.ScreeningAttemptId == attempt.Id)
            .OrderBy(response => response.ScreeningQuestion.SortOrder)
            .ToListAsync();

        Assert.Equal(new[] { "sun", "A bright circle", "garden", "moon", "river", "vop" }, savedResponses.Select(response =>
            response.SelectedAnswer ?? response.TextAnswer));
        Assert.Equal(
            new[]
            {
                ScreeningQuestionType.MultipleChoice,
                ScreeningQuestionType.TextResponse,
                ScreeningQuestionType.Spelling,
                ScreeningQuestionType.PhonologicalChoice,
                ScreeningQuestionType.WordReading,
                ScreeningQuestionType.NonWordReading
            },
            savedResponses.Select(response => response.ScreeningQuestion.QuestionType));
        Assert.All(savedResponses, response =>
        {
            Assert.True(response.ResponseTimeMilliseconds >= 0);
            Assert.Null(response.Score);
            Assert.Null(response.IsCorrect);
            Assert.Null(response.AudioRecordingPath);
        });

        var screeningTest = await dbContext.ScreeningTests.SingleAsync(item => item.Id == 1);
        var answerKey = await dbContext.ScreeningQuestions.SingleAsync(item => item.Prompt == "Which one is the sun?");
        screeningTest.Name = "Updated after report generation";
        answerKey.CorrectAnswer = "moon";
        testAttempt.PercentageScore = 0d;
        testAttempt.HasFlaggedSkillArea = true;
        attempt.RiskTier = "Red";
        await dbContext.SaveChangesAsync();

        var historicalResultResponse = await client.GetAsync(resultPath);
        Assert.Equal(HttpStatusCode.OK, historicalResultResponse.StatusCode);
        var historicalResultHtml = await historicalResultResponse.Content.ReadAsStringAsync();
        Assert.Contains("Green", historicalResultHtml);
        Assert.Contains("Word Reading", historicalResultHtml);
        Assert.Contains("100% matched", historicalResultHtml);
        Assert.DoesNotContain("Updated after report generation", historicalResultHtml);
        Assert.DoesNotContain(">0% matched</span>", historicalResultHtml);

    }

    [Fact]
    public async Task New_cognitive_question_types_render_and_save_through_the_existing_runner()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        await AddQuestionAsync(factory, new ScreeningQuestion
        {
            ScreeningTestId = 1,
            Prompt = "Remember the original sequence.",
            QuestionType = ScreeningQuestionType.WorkingMemorySequence,
            CorrectAnswer = "red blue green",
            SortOrder = 1,
            MetadataJson = JsonSerializer.Serialize(new ScreeningQuestionMetadata
            {
                WorkingMemorySequence = ["red", "blue", "green"],
                MemoryDisplayDurationMilliseconds = 2500
            })
        });
        await AddQuestionAsync(factory, new ScreeningQuestion
        {
            ScreeningTestId = 1,
            Prompt = "Choose the matching shape.",
            QuestionType = ScreeningQuestionType.TimedMatching,
            CorrectAnswer = "square",
            SortOrder = 2,
            MetadataJson = JsonSerializer.Serialize(new ScreeningQuestionMetadata
            {
                ChoiceOptions = ["circle", "square"],
                TimeLimitSeconds = 20
            })
        });
        await AddQuestionAsync(factory, new ScreeningQuestion
        {
            ScreeningTestId = 1,
            Prompt = "Which shape completes the pattern?",
            QuestionType = ScreeningQuestionType.PatternReasoning,
            CorrectAnswer = "triangle",
            ImagePath = "/images/original-pattern.svg",
            SortOrder = 3,
            MetadataJson = JsonSerializer.Serialize(new ScreeningQuestionMetadata
            {
                ChoiceOptions = ["triangle", "square"],
                PatternDifficultyLevel = 2
            })
        });

        var (email, password) = await factory.CreateUserAsync(ApplicationRoles.Learner);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(client, email, password);
        await StartScreeningAsync(client);

        var workingMemoryHtml = await GetQuestionHtmlAsync(client, "Remember the original sequence.");
        Assert.Contains("Working memory sequence", workingMemoryHtml);
        Assert.Contains("screening-working-memory.js", workingMemoryHtml);
        Assert.Contains("Practice example", workingMemoryHtml);
        Assert.Contains("A · 7", workingMemoryHtml);
        Assert.Contains("data-sequence-length=\"3\"", workingMemoryHtml);
        Assert.Contains("red", workingMemoryHtml);
        Assert.Contains("blue", workingMemoryHtml);
        Assert.Contains("green", workingMemoryHtml);
        var workingMemoryResult = await client.PostAsync("/screening/respond", CreateAnswerPost(workingMemoryHtml, ("TextAnswer", "red blue green")));
        Assert.Equal(HttpStatusCode.Redirect, workingMemoryResult.StatusCode);

        var timedMatchingHtml = await GetQuestionHtmlAsync(client, "Choose the matching shape.");
        Assert.Contains("Timed matching", timedMatchingHtml);
        Assert.Contains("screening-timed-matching.js", timedMatchingHtml);
        Assert.Contains("Time remaining", timedMatchingHtml);
        var timedMatchingResult = await client.PostAsync("/screening/respond", CreateAnswerPost(timedMatchingHtml, ("SelectedAnswer", "square")));
        Assert.Equal(HttpStatusCode.Redirect, timedMatchingResult.StatusCode);

        var patternHtml = await GetQuestionHtmlAsync(client, "Which shape completes the pattern?");
        Assert.Contains("Pattern reasoning", patternHtml);
        Assert.Contains("pattern-reasoning-activity", patternHtml);
        Assert.Contains("broader thinking / pattern reasoning", patternHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Activity level: 2 of 5", patternHtml);
        Assert.Contains("/images/original-pattern.svg", patternHtml);
        Assert.Contains("btn-lg", patternHtml);
        Assert.Contains("Continue", patternHtml);
        Assert.Contains("triangle", patternHtml);
        var patternResult = await client.PostAsync("/screening/respond", CreateAnswerPost(patternHtml, ("SelectedAnswer", "triangle")));
        Assert.Equal(HttpStatusCode.Redirect, patternResult.StatusCode);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>();
        var learner = await userManager.FindByEmailAsync(email);
        Assert.NotNull(learner);
        var responses = await dbContext.ScreeningResponses
            .Include(response => response.ScreeningQuestion)
            .Where(response => response.ScreeningTestAttempt.ScreeningAttempt.UserId == learner.Id)
            .OrderBy(response => response.ScreeningQuestion.SortOrder)
            .ToListAsync();
        Assert.Equal(3, responses.Count);
        Assert.Equal(new[]
        {
            ScreeningQuestionType.WorkingMemorySequence,
            ScreeningQuestionType.TimedMatching,
            ScreeningQuestionType.PatternReasoning
        }, responses.Select(response => response.ScreeningQuestion.QuestionType));
        Assert.Equal("red blue green", responses[0].TextAnswer);
        Assert.True(responses[0].IsCorrect);
        Assert.Equal(1d, responses[0].Score);
        Assert.NotNull(responses[0].ResponseTimeMilliseconds);
        Assert.Equal("square", responses[1].SelectedAnswer);
        Assert.Equal("triangle", responses[2].SelectedAnswer);
        Assert.True(responses[2].IsCorrect);
        Assert.Equal(1d, responses[2].Score);
        Assert.NotNull(responses[2].ResponseTimeMilliseconds);
    }

    [Fact]
    public async Task Pattern_reasoning_incorrect_response_is_persisted_without_diagnostic_interpretation()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        await AddPatternReasoningQuestionAsync(factory);

        var (email, password) = await factory.CreateUserAsync(ApplicationRoles.Learner);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(client, email, password);
        await StartScreeningAsync(client);

        var questionHtml = await GetQuestionHtmlAsync(client, "Which option completes the original pattern?");
        Assert.Contains("educational activity", questionHtml, StringComparison.OrdinalIgnoreCase);
        var visibleQuestionText = Regex.Replace(
            questionHtml,
            "<script[^>]*>.*?</script>|<style[^>]*>.*?</style>|<[^>]+>",
            " ",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        Assert.DoesNotContain("IQ", System.Net.WebUtility.HtmlDecode(visibleQuestionText), StringComparison.OrdinalIgnoreCase);
        var response = await client.PostAsync(
            "/screening/respond",
            CreateAnswerPost(questionHtml, ("SelectedAnswer", "square")));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var savedResponse = await dbContext.ScreeningResponses.SingleAsync();
        Assert.Equal(ScreeningQuestionType.PatternReasoning, await dbContext.ScreeningQuestions
            .Where(question => question.Id == savedResponse.ScreeningQuestionId)
            .Select(question => question.QuestionType)
            .SingleAsync());
        Assert.Equal("square", savedResponse.SelectedAnswer);
        Assert.False(savedResponse.IsCorrect);
        Assert.Equal(0d, savedResponse.Score);
        Assert.NotNull(savedResponse.ResponseTimeMilliseconds);
        Assert.True(savedResponse.ResponseTimeMilliseconds >= 0);
    }

    [Fact]
    public async Task Pattern_reasoning_question_token_cannot_be_used_by_another_learner()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        await AddPatternReasoningQuestionAsync(factory);

        var firstCredentials = await factory.CreateUserAsync(ApplicationRoles.Learner);
        var secondCredentials = await factory.CreateUserAsync(ApplicationRoles.Learner);
        using var firstClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var secondClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(firstClient, firstCredentials.Email, firstCredentials.Password);
        await SignInAsync(secondClient, secondCredentials.Email, secondCredentials.Password);
        await StartScreeningAsync(firstClient);
        await StartScreeningAsync(secondClient);

        var firstQuestionHtml = await GetQuestionHtmlAsync(firstClient, "Which option completes the original pattern?");
        var secondQuestionHtml = await GetQuestionHtmlAsync(secondClient, "Which option completes the original pattern?");
        var foreignTokenPost = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiForgeryToken(secondQuestionHtml),
            ["QuestionToken"] = ExtractInputValue(firstQuestionHtml, "QuestionToken"),
            ["SelectedAnswer"] = "triangle"
        });

        var rejected = await secondClient.PostAsync("/screening/respond", foreignTokenPost);

        Assert.Equal(HttpStatusCode.Redirect, rejected.StatusCode);
        Assert.Equal("/screening/resume", rejected.Headers.Location?.OriginalString);
        Assert.Equal(0, await GetResponseCountAsync(factory));
    }

    [Fact]
    public async Task Working_memory_exact_sequence_is_marked_correct()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        await AddWorkingMemoryQuestionAsync(factory);

        var (email, password) = await factory.CreateUserAsync(ApplicationRoles.Learner);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(client, email, password);
        await StartScreeningAsync(client);

        var questionHtml = await GetQuestionHtmlAsync(client, "Remember the original sequence.");
        var response = await client.PostAsync(
            "/screening/respond",
            CreateAnswerPost(questionHtml, ("TextAnswer", "red blue green")));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var savedResponse = await dbContext.ScreeningResponses.SingleAsync();
        Assert.True(savedResponse.IsCorrect);
        Assert.Equal(1d, savedResponse.Score);
    }

    [Fact]
    public async Task Working_memory_incorrect_sequence_is_marked_incorrect()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        await AddWorkingMemoryQuestionAsync(factory);

        var (email, password) = await factory.CreateUserAsync(ApplicationRoles.Learner);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(client, email, password);
        await StartScreeningAsync(client);

        var questionHtml = await GetQuestionHtmlAsync(client, "Remember the original sequence.");
        var response = await client.PostAsync(
            "/screening/respond",
            CreateAnswerPost(questionHtml, ("TextAnswer", "red green blue")));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var savedResponse = await dbContext.ScreeningResponses.SingleAsync();
        Assert.False(savedResponse.IsCorrect);
        Assert.Equal(0d, savedResponse.Score);
    }

    [Fact]
    public async Task Working_memory_response_persists_attempt_question_answer_and_response_time()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        await AddWorkingMemoryQuestionAsync(factory);

        var (email, password) = await factory.CreateUserAsync(ApplicationRoles.Learner);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(client, email, password);
        await StartScreeningAsync(client);

        var questionHtml = await GetQuestionHtmlAsync(client, "Remember the original sequence.");
        var questionToken = ExtractInputValue(questionHtml, "QuestionToken");
        var response = await client.PostAsync(
            "/screening/respond",
            CreateAnswerPost(questionHtml, ("TextAnswer", "red blue green")));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var attempt = await dbContext.ScreeningAttempts.SingleAsync();
        var testAttempt = await dbContext.ScreeningTestAttempts.SingleAsync();
        var question = await dbContext.ScreeningQuestions.SingleAsync(item => item.Prompt == "Remember the original sequence.");
        var savedResponse = await dbContext.ScreeningResponses.SingleAsync();

        Assert.Equal(ScreeningAttemptStatus.InProgress, attempt.Status);
        Assert.Equal(attempt.Id, testAttempt.ScreeningAttemptId);
        Assert.Equal(testAttempt.Id, savedResponse.ScreeningTestAttemptId);
        Assert.Equal(question.Id, savedResponse.ScreeningQuestionId);
        Assert.Equal("red blue green", savedResponse.TextAnswer);
        Assert.NotNull(savedResponse.ResponseTimeMilliseconds);
        Assert.True(savedResponse.ResponseTimeMilliseconds >= 0);
        Assert.NotEqual(string.Empty, questionToken);
    }

    [Fact]
    public async Task Working_memory_question_token_cannot_be_used_by_another_learner()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        await AddWorkingMemoryQuestionAsync(factory);

        var firstCredentials = await factory.CreateUserAsync(ApplicationRoles.Learner);
        var secondCredentials = await factory.CreateUserAsync(ApplicationRoles.Learner);
        using var firstClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var secondClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(firstClient, firstCredentials.Email, firstCredentials.Password);
        await SignInAsync(secondClient, secondCredentials.Email, secondCredentials.Password);
        await StartScreeningAsync(firstClient);
        await StartScreeningAsync(secondClient);

        var firstQuestionHtml = await GetQuestionHtmlAsync(firstClient, "Remember the original sequence.");
        var secondQuestionHtml = await GetQuestionHtmlAsync(secondClient, "Remember the original sequence.");
        var foreignTokenPost = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiForgeryToken(secondQuestionHtml),
            ["QuestionToken"] = ExtractInputValue(firstQuestionHtml, "QuestionToken"),
            ["TextAnswer"] = "red blue green"
        });

        var rejected = await secondClient.PostAsync("/screening/respond", foreignTokenPost);

        Assert.Equal(HttpStatusCode.Redirect, rejected.StatusCode);
        Assert.Equal("/screening/resume", rejected.Headers.Location?.OriginalString);
        Assert.Equal(0, await GetResponseCountAsync(factory));
    }

    [Fact]
    public async Task Processing_speed_persists_server_calculated_counts_and_completes()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        await AddProcessingSpeedQuestionAsync(factory);

        var (email, password) = await factory.CreateUserAsync(ApplicationRoles.Learner);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(client, email, password);
        await StartScreeningAsync(client);

        var questionHtml = await GetQuestionHtmlAsync(client, "Match each item to its symbol.");
        Assert.Contains("Reference mapping", questionHtml);
        Assert.Contains("Item 1 of 4", questionHtml);
        Assert.Contains("Time remaining", questionHtml);
        Assert.Contains("data-time-limit-seconds=\"30\"", questionHtml);
        var response = await client.PostAsync(
            "/screening/respond",
            CreateProcessingSpeedAnswerPost(questionHtml, ["●", "■", "■", "●"]));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var completed = await client.GetAsync("/screening/resume");
        Assert.Equal(HttpStatusCode.Redirect, completed.StatusCode);
        Assert.StartsWith("/screening/results/", completed.Headers.Location?.OriginalString);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var savedResponse = await dbContext.ScreeningResponses.SingleAsync();
        Assert.False(savedResponse.IsCorrect);
        Assert.Equal(0.75d, savedResponse.Score);
        Assert.NotNull(savedResponse.ResponseTimeMilliseconds);
        Assert.True(savedResponse.ResponseTimeMilliseconds >= 0);
        using var details = JsonDocument.Parse(savedResponse.ResponseDetailsJson!);
        Assert.Equal(30, details.RootElement.GetProperty("timeLimitSeconds").GetInt32());
        Assert.False(details.RootElement.GetProperty("timeLimitReached").GetBoolean());
        Assert.Equal(4, details.RootElement.GetProperty("attemptedCount").GetInt32());
        Assert.Equal(3, details.RootElement.GetProperty("correctCount").GetInt32());
        Assert.Equal(1, details.RootElement.GetProperty("incorrectCount").GetInt32());
        Assert.Equal(4, details.RootElement.GetProperty("itemCount").GetInt32());

        var attempt = await dbContext.ScreeningAttempts.SingleAsync();
        Assert.Equal(ScreeningAttemptStatus.Completed, attempt.Status);
        var testAttempt = await dbContext.ScreeningTestAttempts.SingleAsync();
        Assert.Equal(75d, testAttempt.PercentageScore);
    }

    [Fact]
    public async Task Processing_speed_correct_and_incorrect_answers_are_persisted_as_activity_metrics()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        await AddProcessingSpeedQuestionAsync(factory);

        var (email, password) = await factory.CreateUserAsync(ApplicationRoles.Learner);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(client, email, password);
        await StartScreeningAsync(client);

        var questionHtml = await GetQuestionHtmlAsync(client, "Match each item to its symbol.");
        var response = await client.PostAsync(
            "/screening/respond",
            CreateProcessingSpeedAnswerPost(questionHtml, ["●", "▲", "■", "●"]));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var savedResponse = await dbContext.ScreeningResponses.SingleAsync();
        Assert.True(savedResponse.IsCorrect);
        Assert.Equal(1d, savedResponse.Score);
        using var details = JsonDocument.Parse(savedResponse.ResponseDetailsJson!);
        Assert.Equal(4, details.RootElement.GetProperty("attemptedCount").GetInt32());
        Assert.Equal(4, details.RootElement.GetProperty("correctCount").GetInt32());
        Assert.Equal(0, details.RootElement.GetProperty("incorrectCount").GetInt32());
    }

    [Fact]
    public async Task Processing_speed_question_token_cannot_be_used_by_another_learner()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        await AddProcessingSpeedQuestionAsync(factory);

        var firstCredentials = await factory.CreateUserAsync(ApplicationRoles.Learner);
        var secondCredentials = await factory.CreateUserAsync(ApplicationRoles.Learner);
        using var firstClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var secondClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(firstClient, firstCredentials.Email, firstCredentials.Password);
        await SignInAsync(secondClient, secondCredentials.Email, secondCredentials.Password);
        await StartScreeningAsync(firstClient);
        await StartScreeningAsync(secondClient);

        var firstQuestionHtml = await GetQuestionHtmlAsync(firstClient, "Match each item to its symbol.");
        var secondQuestionHtml = await GetQuestionHtmlAsync(secondClient, "Match each item to its symbol.");
        var foreignTokenPost = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiForgeryToken(secondQuestionHtml),
            ["QuestionToken"] = ExtractInputValue(firstQuestionHtml, "QuestionToken"),
            ["TimedMatchingAnswersJson"] = JsonSerializer.Serialize(new[] { "●", "▲", "■", "●" })
        });

        var rejected = await secondClient.PostAsync("/screening/respond", foreignTokenPost);

        Assert.Equal(HttpStatusCode.Redirect, rejected.StatusCode);
        Assert.Equal("/screening/resume", rejected.Headers.Location?.OriginalString);
        Assert.Equal(0, await GetResponseCountAsync(factory));
    }

    [Theory]
    [InlineData("Amber")]
    [InlineData("Red")]
    public async Task Amber_and_red_results_suggest_professional_review_without_implying_it_is_already_assigned(string tier)
    {
        using var factory = new LexiAssistWebApplicationFactory();
        var (email, password) = await factory.CreateUserAsync(ApplicationRoles.Learner);
        int attemptId;

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>();
            var learner = await userManager.FindByEmailAsync(email);
            Assert.NotNull(learner);

            var attempt = new ScreeningAttempt
            {
                UserId = learner.Id,
                StartedAtUtc = DateTime.UtcNow.AddMinutes(-5),
                CompletedAtUtc = DateTime.UtcNow,
                Status = ScreeningAttemptStatus.Completed,
                RiskTier = tier
            };
            dbContext.ScreeningAttempts.Add(attempt);
            await dbContext.SaveChangesAsync();
            attemptId = attempt.Id;
        }

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(client, email, password);
        var response = await client.GetAsync($"/screening/results/{attemptId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Screening/support tier", html);
        Assert.Contains(tier, html);
        Assert.Contains("A suitable support professional may review this screening according to the LexiAssist support workflow.", html);
        Assert.Contains("Additional learning support or a professional assessment could be useful.", html);
        Assert.DoesNotContain("You have dyslexia.", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Question_token_cannot_be_used_for_another_learners_attempt()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        await AddQuestionAsync(factory, new ScreeningQuestion
        {
            ScreeningTestId = 1,
            Prompt = "Private learner question",
            QuestionType = ScreeningQuestionType.TextResponse,
            SortOrder = 1
        });

        var firstCredentials = await factory.CreateUserAsync(ApplicationRoles.Learner);
        var secondCredentials = await factory.CreateUserAsync(ApplicationRoles.Learner);
        using var firstClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var secondClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(firstClient, firstCredentials.Email, firstCredentials.Password);
        await SignInAsync(secondClient, secondCredentials.Email, secondCredentials.Password);

        await StartScreeningAsync(firstClient);
        await StartScreeningAsync(secondClient);
        var firstQuestion = await GetQuestionHtmlAsync(firstClient, "Private learner question");
        var secondQuestion = await GetQuestionHtmlAsync(secondClient, "Private learner question");

        var foreignTokenPost = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiForgeryToken(secondQuestion),
            ["QuestionToken"] = ExtractInputValue(firstQuestion, "QuestionToken"),
            ["TextAnswer"] = "stolen response"
        });
        var rejected = await secondClient.PostAsync("/screening/respond", foreignTokenPost);
        Assert.Equal(HttpStatusCode.Redirect, rejected.StatusCode);
        Assert.Equal("/screening/resume", rejected.Headers.Location?.OriginalString);
        Assert.Equal(0, await GetResponseCountAsync(factory));

        var stillOnSameQuestion = await secondClient.GetAsync("/screening/resume");
        Assert.Contains("Private learner question", await stillOnSameQuestion.Content.ReadAsStringAsync());
        _ = secondQuestion;
    }

    private static async Task SeedQuestionSetAsync(LexiAssistWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.ScreeningQuestions.AddRange(
            new ScreeningQuestion
            {
                ScreeningTestId = 1,
                Prompt = "Which one is the sun?",
                QuestionType = ScreeningQuestionType.MultipleChoice,
                MetadataJson = "{\"choiceOptions\":[\"sun\",\"moon\"]}",
                CorrectAnswer = "sun",
                SortOrder = 1
            },
            new ScreeningQuestion
            {
                ScreeningTestId = 1,
                Prompt = "What did you notice?",
                QuestionType = ScreeningQuestionType.TextResponse,
                SortOrder = 2
            },
            new ScreeningQuestion
            {
                ScreeningTestId = 1,
                Prompt = "Type the word shown:",
                QuestionType = ScreeningQuestionType.Spelling,
                SortOrder = 3
            },
            new ScreeningQuestion
            {
                ScreeningTestId = 1,
                Prompt = "Which word begins with /m/?",
                QuestionType = ScreeningQuestionType.PhonologicalChoice,
                MetadataJson = "{\"choiceOptions\":[\"moon\",\"sun\"]}",
                CorrectAnswer = "moon",
                SortOrder = 4
            },
            new ScreeningQuestion
            {
                ScreeningTestId = 1,
                Prompt = "river",
                QuestionType = ScreeningQuestionType.WordReading,
                CorrectAnswer = "river",
                SortOrder = 5
            },
            new ScreeningQuestion
            {
                ScreeningTestId = 1,
                Prompt = "vop",
                QuestionType = ScreeningQuestionType.NonWordReading,
                CorrectAnswer = "vop",
                SortOrder = 6
            },
            new ScreeningQuestion
            {
                ScreeningTestId = 1,
                Prompt = "Inactive question",
                QuestionType = ScreeningQuestionType.TextResponse,
                SortOrder = 5,
                IsActive = false
            },
            new ScreeningQuestion
            {
                ScreeningTestId = 1,
                Prompt = "Unsupported question type",
                QuestionType = (ScreeningQuestionType)99,
                SortOrder = 8
            },
            new ScreeningQuestion
            {
                ScreeningTestId = 9,
                Prompt = "Disabled category question",
                QuestionType = ScreeningQuestionType.TextResponse,
                SortOrder = 1
            });
        await dbContext.SaveChangesAsync();
    }

    private static async Task AddQuestionAsync(LexiAssistWebApplicationFactory factory, ScreeningQuestion question)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.ScreeningQuestions.Add(question);
        await dbContext.SaveChangesAsync();
    }

    private static Task AddWorkingMemoryQuestionAsync(LexiAssistWebApplicationFactory factory) =>
        AddQuestionAsync(factory, new ScreeningQuestion
        {
            ScreeningTestId = 1,
            Prompt = "Remember the original sequence.",
            QuestionType = ScreeningQuestionType.WorkingMemorySequence,
            CorrectAnswer = "red blue green",
            SortOrder = 1,
            MetadataJson = JsonSerializer.Serialize(new ScreeningQuestionMetadata
            {
                WorkingMemorySequence = ["red", "blue", "green"],
                MemoryDisplayDurationMilliseconds = 2500
            })
        });

    private static Task AddProcessingSpeedQuestionAsync(LexiAssistWebApplicationFactory factory) =>
        AddQuestionAsync(factory, new ScreeningQuestion
        {
            ScreeningTestId = 1,
            Prompt = "Match each item to its symbol.",
            QuestionType = ScreeningQuestionType.TimedMatching,
            SortOrder = 1,
            MetadataJson = JsonSerializer.Serialize(new ScreeningQuestionMetadata
            {
                ChoiceOptions = ["●", "▲", "■"],
                TimeLimitSeconds = 30,
                TimedMatchingReferenceMapping = new Dictionary<string, string>
                {
                    ["A"] = "●",
                    ["B"] = "▲",
                    ["C"] = "■"
                },
                TimedMatchingItems = ["A", "B", "C", "A"]
            })
        });

    private static Task AddPatternReasoningQuestionAsync(LexiAssistWebApplicationFactory factory) =>
        AddQuestionAsync(factory, new ScreeningQuestion
        {
            ScreeningTestId = 1,
            Prompt = "Which option completes the original pattern?",
            QuestionType = ScreeningQuestionType.PatternReasoning,
            CorrectAnswer = "triangle",
            SortOrder = 1,
            MetadataJson = JsonSerializer.Serialize(new ScreeningQuestionMetadata
            {
                ChoiceOptions = ["triangle", "square"],
                PatternDifficultyLevel = 1
            })
        });

    private static async Task StartScreeningAsync(HttpClient client)
    {
        var startPage = await client.GetAsync("/screening/start");
        Assert.Equal(HttpStatusCode.OK, startPage.StatusCode);
        var html = await startPage.Content.ReadAsStringAsync();
        var result = await client.PostAsync("/screening/start", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiForgeryToken(html)
        }));
        Assert.Equal(HttpStatusCode.Redirect, result.StatusCode);
        Assert.Equal("/screening/resume", result.Headers.Location?.OriginalString);
    }

    private static async Task<string> GetQuestionHtmlAsync(HttpClient client, string prompt)
    {
        var response = await client.GetAsync("/screening/resume");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains(prompt, html);
        return html;
    }

    private static FormUrlEncodedContent CreateAnswerPost(
        string html,
        (string Name, string Value) answer)
    {
        return new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiForgeryToken(html),
            ["QuestionToken"] = ExtractInputValue(html, "QuestionToken"),
            [answer.Name] = answer.Value
        });
    }

    private static FormUrlEncodedContent CreateProcessingSpeedAnswerPost(
        string html,
        IReadOnlyList<string> answers) =>
        new(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiForgeryToken(html),
            ["QuestionToken"] = ExtractInputValue(html, "QuestionToken"),
            ["TimedMatchingAnswersJson"] = JsonSerializer.Serialize(answers)
        });

    private static async Task<int> GetResponseCountAsync(LexiAssistWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await dbContext.ScreeningResponses.CountAsync();
    }

    private static async Task SignInAsync(HttpClient client, string email, string password)
    {
        var loginPage = await client.GetAsync("/Account/Login");
        var loginHtml = await loginPage.Content.ReadAsStringAsync();
        var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiForgeryToken(loginHtml),
            ["Email"] = email,
            ["Password"] = password,
            ["RememberMe"] = "false"
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private static string ExtractAntiForgeryToken(string html) => ExtractInputValue(html, "__RequestVerificationToken");

    private static string ExtractInputValue(string html, string name)
    {
        var match = Regex.Match(
            html,
            $"<input[^>]*name=\\\"{Regex.Escape(name)}\\\"[^>]*value=\\\"([^\\\"]*)\\\"",
            RegexOptions.IgnoreCase);
        Assert.True(match.Success, $"The rendered page should include the '{name}' field.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
}
