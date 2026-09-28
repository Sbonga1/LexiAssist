using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Identity;
using LexiAssist.Web.Models.Screening;
using LexiAssist.Web.Services;
using LexiAssist.Web.Services.Interfaces;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LexiAssist.Tests;

public sealed class AdminAssessmentBuilderTests
{
    [Fact]
    public async Task Configure_and_review_are_administrator_only_and_show_the_shared_readiness_result()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        var testId = await CreateAssessmentAsync(factory, "configure-review-readiness");

        using var anonymousClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        foreach (var stage in new[] { "configure", "review" })
        {
            var anonymousResult = await anonymousClient.GetAsync($"/admin/screening-tests/{testId}/builder/{stage}");
            Assert.Equal(HttpStatusCode.Redirect, anonymousResult.StatusCode);
            Assert.StartsWith("/Account/Login", GetLocationPath(anonymousResult));
        }

        using var learnerClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (learnerEmail, learnerPassword) = await factory.CreateUserAsync(ApplicationRoles.Learner);
        await SignInAsync(learnerClient, learnerEmail, learnerPassword);
        foreach (var stage in new[] { "configure", "review" })
        {
            var learnerResult = await learnerClient.GetAsync($"/admin/screening-tests/{testId}/builder/{stage}");
            Assert.Equal(HttpStatusCode.Redirect, learnerResult.StatusCode);
            Assert.StartsWith("/Account/AccessDenied", GetLocationPath(learnerResult));
        }

        using var psychologistClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (psychologistEmail, psychologistPassword) = await factory.CreateUserAsync(ApplicationRoles.Psychologist);
        await SignInAsync(psychologistClient, psychologistEmail, psychologistPassword);
        var psychologistResult = await psychologistClient.GetAsync($"/admin/screening-tests/{testId}/builder/review");
        Assert.Equal(HttpStatusCode.Redirect, psychologistResult.StatusCode);
        Assert.StartsWith("/Account/AccessDenied", GetLocationPath(psychologistResult));

        using var adminClient = await CreateAdministratorClientAsync(factory);
        var configureResponse = await adminClient.GetAsync($"/admin/screening-tests/{testId}/builder/configure");
        var configureHtml = await configureResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, configureResponse.StatusCode);
        Assert.Contains("Assessment behavior", configureHtml);
        Assert.Contains("No assessment-level settings need saving", configureHtml);
        Assert.Contains("Continue to review", configureHtml);
        Assert.Contains("aria-current=\"step\"", configureHtml);

        var reviewResponse = await adminClient.GetAsync($"/admin/screening-tests/{testId}/builder/review");
        var reviewHtml = await reviewResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, reviewResponse.StatusCode);
        Assert.Contains("ACTION REQUIRED", reviewHtml);
        Assert.Contains("Candidate Preview", reviewHtml);
        Assert.Contains("Questions", reviewHtml);
        Assert.Contains("Activate assessment", reviewHtml);
        Assert.Contains("disabled=\"disabled\"", reviewHtml);
        using (var serviceScope = factory.Services.CreateScope())
        {
            Assert.IsType<AssessmentReadinessService>(serviceScope.ServiceProvider.GetRequiredService<IAssessmentReadinessService>());
        }
    }

    [Fact]
    public async Task Review_uses_real_counts_ordered_questions_and_allows_inactive_drafts_without_blocking()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        int testId;
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var test = new ScreeningTest
            {
                Name = "Ready Configure Review assessment",
                Slug = "ready-configure-review-assessment",
                Description = "Actual persisted details.",
                Instructions = "Read each prompt and respond.",
                IsActive = false,
                SortOrder = 80
            };
            dbContext.ScreeningTests.Add(test);
            await dbContext.SaveChangesAsync();
            testId = test.Id;
            dbContext.ScreeningQuestions.AddRange(
                new ScreeningQuestion
                {
                    ScreeningTestId = testId,
                    Prompt = "Read the word aloud.",
                    QuestionType = ScreeningQuestionType.WordReading,
                    CorrectAnswer = "sun",
                    SortOrder = 1,
                    IsActive = true
                },
                new ScreeningQuestion
                {
                    ScreeningTestId = testId,
                    Prompt = "Choose the correct word.",
                    QuestionType = ScreeningQuestionType.MultipleChoice,
                    CorrectAnswer = "cat",
                    MetadataJson = JsonSerializer.Serialize(new ScreeningQuestionMetadata { ChoiceOptions = ["cat", "dog"] }),
                    SortOrder = 2,
                    IsActive = true
                },
                new ScreeningQuestion
                {
                    ScreeningTestId = testId,
                    Prompt = "Name a color.",
                    QuestionType = ScreeningQuestionType.TextResponse,
                    SortOrder = 3,
                    IsActive = true
                },
                new ScreeningQuestion
                {
                    ScreeningTestId = testId,
                    Prompt = "AI reviewed draft prompt.",
                    QuestionType = ScreeningQuestionType.TextResponse,
                    SortOrder = 4,
                    IsActive = false
                });
            await dbContext.SaveChangesAsync();
        }

        using var client = await CreateAdministratorClientAsync(factory);
        var response = await client.GetAsync($"/admin/screening-tests/{testId}/builder/review");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("READY TO ACTIVATE", html);
        Assert.Contains("Total questions", html);
        Assert.Contains("Inactive drafts", html);
        Assert.Contains("Multiple choice", html);
        Assert.Contains("Word reading", html);
        Assert.Contains("Text response", html);
        Assert.Contains("AI reviewed draft prompt", html);
        Assert.Contains("Inactive / Draft", html);
        Assert.Contains("Inactive draft · excluded from learners", html);

        using var scopeVerify = factory.Services.CreateScope();
        var dbContextVerify = scopeVerify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var questionRows = await dbContextVerify.ScreeningQuestions.Where(question => question.ScreeningTestId == testId).OrderBy(question => question.SortOrder).ToListAsync();
        Assert.Equal(4, questionRows.Count);
        Assert.Equal(3, questionRows.Count(question => question.IsActive));
        Assert.False(questionRows[^1].IsActive);

        var preview = await client.GetAsync($"/admin/screening-tests/{testId}/builder/preview?returnToReview=true");
        var previewHtml = await preview.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Contains("Back to review", previewHtml);
        Assert.Contains("Read the word aloud.", previewHtml);
        Assert.Contains("Choose the correct word.", previewHtml);
        Assert.DoesNotContain("AI reviewed draft prompt", previewHtml);
        Assert.Contains("This preview does not record answers or create an attempt", previewHtml);
        Assert.Equal(0, await dbContextVerify.ScreeningTestAttempts.CountAsync(attempt => attempt.ScreeningTestId == testId));
        Assert.Equal(0, await dbContextVerify.ScreeningResponses.CountAsync(response => response.ScreeningQuestion.ScreeningTestId == testId));
    }

    [Fact]
    public async Task Reviewed_generated_draft_changes_total_and_inactive_counts_without_changing_active_count()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        var testId = await CreateAssessmentAsync(factory, "generated-review-counts");
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.ScreeningQuestions.Add(new ScreeningQuestion
            {
                ScreeningTestId = testId,
                Prompt = "Which word names a sunny day?",
                QuestionType = ScreeningQuestionType.MultipleChoice,
                CorrectAnswer = "bright",
                MetadataJson = JsonSerializer.Serialize(new ScreeningQuestionMetadata { ChoiceOptions = ["bright", "dark"] }),
                SortOrder = 1,
                IsActive = true
            });
            await dbContext.SaveChangesAsync();
        }

        using var client = await CreateAdministratorClientAsync(factory);
        var builder = await client.GetAsync($"/admin/screening-tests/{testId}/builder");
        var token = ExtractAntiForgeryToken(await builder.Content.ReadAsStringAsync());
        using var generatedDraftForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["ScreeningTestId"] = testId.ToString(),
            ["SelectedIndexes[0]"] = "0",
            ["Questions[0].Prompt"] = "Which word means a quick look?",
            ["Questions[0].QuestionType"] = ((int)ScreeningQuestionType.MultipleChoice).ToString(),
            ["Questions[0].CorrectAnswer"] = "glance",
            ["Questions[0].Choices[0]"] = "glance",
            ["Questions[0].Choices[1]"] = "garden"
        });
        var saved = await client.PostAsync($"/admin/screening-tests/{testId}/builder/generate/add-selected", generatedDraftForm);
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);

        using var scopeVerify = factory.Services.CreateScope();
        var verifyDbContext = scopeVerify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var generatedQuestion = await verifyDbContext.ScreeningQuestions
            .SingleAsync(question => question.ScreeningTestId == testId && question.Prompt == "Which word means a quick look?");
        Assert.True(generatedQuestion.Id > 0);
        Assert.False(generatedQuestion.IsActive);
        Assert.Equal("glance", generatedQuestion.CorrectAnswer);

        var review = await client.GetAsync($"/admin/screening-tests/{testId}/builder/review");
        var reviewHtml = await review.Content.ReadAsStringAsync();
        Assert.Contains("READY TO ACTIVATE", reviewHtml);
        Assert.Contains("Which word means a quick look?", reviewHtml);
        Assert.Contains("Total questions", reviewHtml);
        Assert.Contains("Inactive drafts", reviewHtml);
        Assert.Contains("Inactive / Draft", reviewHtml);
        AssessmentReadinessResult? readiness;
        using (var readinessScope = factory.Services.CreateScope())
        {
            readiness = await readinessScope.ServiceProvider.GetRequiredService<IAssessmentReadinessService>().GetReadinessAsync(testId);
        }
        Assert.NotNull(readiness);
        Assert.Equal(2, readiness.TotalQuestionCount);
        Assert.Equal(1, readiness.ActiveQuestionCount);
        Assert.Equal(1, readiness.InactiveQuestionCount);
        Assert.True(readiness.IsReady);

        var preview = await client.GetAsync($"/admin/screening-tests/{testId}/builder/preview?returnToReview=true");
        var previewHtml = await preview.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Which word means a quick look?", previewHtml);
        Assert.Equal(0, await verifyDbContext.ScreeningTestAttempts.CountAsync(attempt => attempt.ScreeningTestId == testId));
    }

    [Fact]
    public async Task Both_activation_routes_block_invalid_active_question_and_allow_a_fixed_assessment()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        var testId = await CreateAssessmentAsync(factory, "invalid-mcq-readiness");
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.ScreeningQuestions.Add(new ScreeningQuestion
            {
                ScreeningTestId = testId,
                Prompt = "Pick the correct option.",
                QuestionType = ScreeningQuestionType.MultipleChoice,
                CorrectAnswer = "not listed",
                MetadataJson = JsonSerializer.Serialize(new ScreeningQuestionMetadata { ChoiceOptions = ["one", "two"] }),
                SortOrder = 1,
                IsActive = true
            });
            await dbContext.SaveChangesAsync();
        }

        using var client = await CreateAdministratorClientAsync(factory);
        var review = await client.GetAsync($"/admin/screening-tests/{testId}/builder/review");
        var reviewHtml = await review.Content.ReadAsStringAsync();
        Assert.Contains("ACTION REQUIRED", reviewHtml);
        Assert.Contains("Correct answer does not match an option", reviewHtml);
        Assert.Contains("Edit", reviewHtml);
        var token = ExtractAntiForgeryToken(reviewHtml);
        using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token });

        var builderActivation = await client.PostAsync($"/admin/screening-tests/{testId}/builder/activate", form);
        Assert.Equal(HttpStatusCode.Redirect, builderActivation.StatusCode);
        var builderAfter = await client.GetAsync($"/admin/screening-tests/{testId}/builder");
        Assert.Contains("Correct answer does not match an option", await builderAfter.Content.ReadAsStringAsync());

        var details = await client.GetAsync($"/admin/screening-tests/{testId}");
        var detailsHtml = await details.Content.ReadAsStringAsync();
        token = ExtractAntiForgeryToken(detailsHtml);
        using var secondForm = new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token });
        var alternateActivation = await client.PostAsync($"/admin/screening-tests/{testId}/activate", secondForm);
        Assert.Equal(HttpStatusCode.Redirect, alternateActivation.StatusCode);
        var detailsAfter = await client.GetAsync($"/admin/screening-tests/{testId}");
        Assert.Contains("Correct answer does not match an option", await detailsAfter.Content.ReadAsStringAsync());

        using var verifyScope = factory.Services.CreateScope();
        var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False((await verifyDbContext.ScreeningTests.SingleAsync(test => test.Id == testId)).IsActive);

        var question = await verifyDbContext.ScreeningQuestions.SingleAsync(item => item.ScreeningTestId == testId);
        question.CorrectAnswer = "one";
        await verifyDbContext.SaveChangesAsync();
        var readyReview = await client.GetAsync($"/admin/screening-tests/{testId}/builder/review");
        Assert.Contains("READY TO ACTIVATE", await readyReview.Content.ReadAsStringAsync());
        token = ExtractAntiForgeryToken(await readyReview.Content.ReadAsStringAsync());
        using var readyForm = new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token });
        var activated = await client.PostAsync($"/admin/screening-tests/{testId}/builder/activate", readyForm);
        Assert.Equal(HttpStatusCode.Redirect, activated.StatusCode);
        var activatedTest = await verifyDbContext.ScreeningTests.SingleAsync(test => test.Id == testId);
        await verifyDbContext.Entry(activatedTest).ReloadAsync();
        Assert.True(activatedTest.IsActive);
    }

    [Fact]
    public async Task Listening_readiness_requires_a_runtime_resolvable_stimulus()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        int testId;
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var test = new ScreeningTest { Name = "Listening readiness", Slug = "listening-readiness", Description = "Listening check", Instructions = "Listen.", IsActive = false, SortOrder = 90 };
            dbContext.ScreeningTests.Add(test);
            await dbContext.SaveChangesAsync();
            testId = test.Id;
            dbContext.ScreeningQuestions.Add(new ScreeningQuestion
            {
                ScreeningTestId = testId,
                Prompt = "What did the speaker say?",
                QuestionType = ScreeningQuestionType.ListeningResponse,
                AudioPath = "listening-not-present.wav",
                CorrectAnswer = "hello",
                AllowsAudioRecording = false,
                MetadataJson = JsonSerializer.Serialize(new ScreeningQuestionMetadata { ResponseType = "text-response" }),
                IsActive = true,
                SortOrder = 1
            });
            await dbContext.SaveChangesAsync();
        }

        using var client = await CreateAdministratorClientAsync(factory);
        var review = await client.GetAsync($"/admin/screening-tests/{testId}/builder/review");
        var html = await review.Content.ReadAsStringAsync();
        Assert.Contains("ACTION REQUIRED", html);
        Assert.Contains("Listening stimulus file is missing or unavailable", html);
        Assert.Contains($"/admin/screening-tests/{testId}/questions/", html);

        var token = ExtractAntiForgeryToken(html);
        using var builderActivationForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        });
        var builderActivation = await client.PostAsync($"/admin/screening-tests/{testId}/builder/activate", builderActivationForm);
        Assert.Equal(HttpStatusCode.Redirect, builderActivation.StatusCode);
        var details = await client.GetAsync($"/admin/screening-tests/{testId}");
        var detailsHtml = await details.Content.ReadAsStringAsync();
        token = ExtractAntiForgeryToken(detailsHtml);
        using var detailsActivationForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        });
        var detailsActivation = await client.PostAsync($"/admin/screening-tests/{testId}/activate", detailsActivationForm);
        Assert.Equal(HttpStatusCode.Redirect, detailsActivation.StatusCode);
        using var verifyScope = factory.Services.CreateScope();
        var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False((await verifyDbContext.ScreeningTests.SingleAsync(test => test.Id == testId)).IsActive);
    }

    [Fact]
    public async Task Builder_is_administrator_only_and_shows_real_question_order_and_read_only_preview()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        var testId = await GetFirstTestIdAsync(factory);

        using var anonymousClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var anonymousResult = await anonymousClient.GetAsync($"/admin/screening-tests/{testId}/builder");
        Assert.Equal(HttpStatusCode.Redirect, anonymousResult.StatusCode);
        Assert.StartsWith("/Account/Login", GetLocationPath(anonymousResult));

        using var learnerClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (learnerEmail, learnerPassword) = await factory.CreateUserAsync(ApplicationRoles.Learner);
        await SignInAsync(learnerClient, learnerEmail, learnerPassword);
        var deniedResult = await learnerClient.GetAsync($"/admin/screening-tests/{testId}/builder");
        Assert.Equal(HttpStatusCode.Redirect, deniedResult.StatusCode);
        Assert.StartsWith("/Account/AccessDenied", GetLocationPath(deniedResult));

        using var psychologistClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (psychologistEmail, psychologistPassword) = await factory.CreateUserAsync(ApplicationRoles.Psychologist);
        await SignInAsync(psychologistClient, psychologistEmail, psychologistPassword);
        var psychologistDeniedResult = await psychologistClient.GetAsync($"/admin/screening-tests/{testId}/builder");
        Assert.Equal(HttpStatusCode.Redirect, psychologistDeniedResult.StatusCode);
        Assert.StartsWith("/Account/AccessDenied", GetLocationPath(psychologistDeniedResult));

        int attemptsBefore;
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.ScreeningQuestions.AddRange(
                new ScreeningQuestion
                {
                    ScreeningTestId = testId,
                    Prompt = "Second builder prompt",
                    QuestionType = ScreeningQuestionType.TextResponse,
                    CorrectAnswer = "second",
                    SortOrder = 2
                },
                new ScreeningQuestion
                {
                    ScreeningTestId = testId,
                    Prompt = "First builder prompt",
                    QuestionType = ScreeningQuestionType.WordReading,
                    CorrectAnswer = "first",
                    SortOrder = 1,
                    MetadataJson = JsonSerializer.Serialize(new ScreeningQuestionMetadata { ChoiceOptions = ["one", "two"] })
                });
            await dbContext.SaveChangesAsync();
            attemptsBefore = await dbContext.ScreeningTestAttempts.CountAsync();
        }

        using var adminClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (adminEmail, adminPassword) = await factory.CreateUserAsync(ApplicationRoles.Administrator);
        await SignInAsync(adminClient, adminEmail, adminPassword);
        var builderResponse = await adminClient.GetAsync($"/admin/screening-tests/{testId}/builder");
        Assert.Equal(HttpStatusCode.OK, builderResponse.StatusCode);
        var builderHtml = await builderResponse.Content.ReadAsStringAsync();
        Assert.Contains("First builder prompt", builderHtml);
        Assert.Contains("Second builder prompt", builderHtml);
        Assert.True(builderHtml.IndexOf("First builder prompt", StringComparison.Ordinal) < builderHtml.IndexOf("Second builder prompt", StringComparison.Ordinal));
        Assert.Contains("Section navigator", builderHtml);
        Assert.Contains("Generate with AI", builderHtml);
        Assert.Contains("Import CSV", builderHtml);
        Assert.Contains("data-focus-mode", builderHtml);
        Assert.Contains("assessment-workspace.css", builderHtml);
        Assert.Contains("Candidate preview", builderHtml);

        var previewResponse = await adminClient.GetAsync($"/admin/screening-tests/{testId}/builder/preview");
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        var previewHtml = await previewResponse.Content.ReadAsStringAsync();
        Assert.Contains("First builder prompt", previewHtml);
        Assert.Contains("one", previewHtml);
        Assert.Contains("This preview does not record answers or create an attempt", previewHtml);
        Assert.DoesNotContain("Submit answer", previewHtml, StringComparison.OrdinalIgnoreCase);

        using var verifyScope = factory.Services.CreateScope();
        var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(attemptsBefore, await verifyDbContext.ScreeningTestAttempts.CountAsync());
    }

    [Fact]
    public async Task Candidate_preview_renders_real_question_types_and_pager_controls_without_persisting_responses()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        var testId = await CreateAssessmentAsync(factory, "candidate-preview-pager-types");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.ScreeningQuestions.AddRange(
                new ScreeningQuestion { ScreeningTestId = testId, SortOrder = 1, IsActive = true, Prompt = "Choose the right sound.", QuestionType = ScreeningQuestionType.MultipleChoice, CorrectAnswer = "m", MetadataJson = JsonSerializer.Serialize(new ScreeningQuestionMetadata { ChoiceOptions = ["m", "s"] }) },
                new ScreeningQuestion { ScreeningTestId = testId, SortOrder = 2, IsActive = true, Prompt = "Tell us what you think.", QuestionType = ScreeningQuestionType.TextResponse },
                new ScreeningQuestion { ScreeningTestId = testId, SortOrder = 3, IsActive = true, Prompt = "Read this word aloud: map", QuestionType = ScreeningQuestionType.WordReading, CorrectAnswer = "map", AllowsAudioRecording = true },
                new ScreeningQuestion { ScreeningTestId = testId, SortOrder = 4, IsActive = true, Prompt = "Read this made-up word aloud: mip", QuestionType = ScreeningQuestionType.NonWordReading, CorrectAnswer = "mip" },
                new ScreeningQuestion { ScreeningTestId = testId, SortOrder = 5, IsActive = true, Prompt = "Choose the word with the same first sound.", QuestionType = ScreeningQuestionType.PhonologicalChoice, MetadataJson = JsonSerializer.Serialize(new ScreeningQuestionMetadata { ChoiceOptions = ["sun", "map"] }) },
                new ScreeningQuestion { ScreeningTestId = testId, SortOrder = 6, IsActive = true, Prompt = "Name each picture.", QuestionType = ScreeningQuestionType.TimedNaming, ImagePath = "/images/naming.png", MetadataJson = JsonSerializer.Serialize(new ScreeningQuestionMetadata { TimedNamingItems = Enumerable.Range(1, 20).Select(number => $"Item {number}").ToList(), TimeLimitSeconds = 30 }) },
                new ScreeningQuestion { ScreeningTestId = testId, SortOrder = 7, IsActive = true, Prompt = "Spell the word.", QuestionType = ScreeningQuestionType.Spelling, CorrectAnswer = "nest" },
                new ScreeningQuestion { ScreeningTestId = testId, SortOrder = 8, IsActive = true, Prompt = "What happened in the passage?", QuestionType = ScreeningQuestionType.ReadingPassage, MetadataJson = JsonSerializer.Serialize(new ScreeningQuestionMetadata { PassageText = "A child planted a seed." }) },
                new ScreeningQuestion { ScreeningTestId = testId, SortOrder = 9, IsActive = true, Prompt = "Answer after listening.", QuestionType = ScreeningQuestionType.ListeningResponse, AllowsAudioRecording = true, MetadataJson = JsonSerializer.Serialize(new ScreeningQuestionMetadata { ResponseType = "audio-response", Transcript = "A source accessibility transcript." }) },
                new ScreeningQuestion { ScreeningTestId = testId, SortOrder = 10, IsActive = true, Prompt = "Read the passage aloud.", QuestionType = ScreeningQuestionType.ReadingFluency, MetadataJson = JsonSerializer.Serialize(new ScreeningQuestionMetadata { PassageText = "One two three four.", ReferenceWordCount = 4, TargetWordsPerMinute = 40 }) },
                new ScreeningQuestion { ScreeningTestId = testId, SortOrder = 11, IsActive = true, Prompt = "Remember this order.", QuestionType = ScreeningQuestionType.WorkingMemorySequence, MetadataJson = JsonSerializer.Serialize(new ScreeningQuestionMetadata { WorkingMemorySequence = ["A", "7"], MemoryDisplayDurationMilliseconds = 3000 }) },
                new ScreeningQuestion { ScreeningTestId = testId, SortOrder = 12, IsActive = true, Prompt = "Match each item to its symbol.", QuestionType = ScreeningQuestionType.TimedMatching, MetadataJson = JsonSerializer.Serialize(new ScreeningQuestionMetadata { ChoiceOptions = ["●", "▲"], TimedMatchingItems = ["one", "two"], TimedMatchingReferenceMapping = new Dictionary<string, string> { ["one"] = "●", ["two"] = "▲" }, TimeLimitSeconds = 60 }) },
                new ScreeningQuestion { ScreeningTestId = testId, SortOrder = 13, IsActive = true, Prompt = "Complete the pattern.", QuestionType = ScreeningQuestionType.PatternReasoning, MetadataJson = JsonSerializer.Serialize(new ScreeningQuestionMetadata { ChoiceOptions = ["circle", "square"], PatternDifficultyLevel = 2 }) });
            await db.SaveChangesAsync();
        }

        using var client = await CreateAdministratorClientAsync(factory);
        var html = await client.GetStringAsync($"/admin/screening-tests/{testId}/builder/preview");

        Assert.Contains("List view", html);
        Assert.Contains("Pager view", html);
        Assert.Contains("data-preview-question-count", html);
        Assert.Contains("data-preview-progress", html);
        Assert.Contains("data-preview-previous disabled", html);
        Assert.Contains("data-preview-next", html);
        Assert.Equal(13, Regex.Matches(html, "data-preview-question data-question-index=").Count);
        Assert.Contains("Live transcription", html);
        Assert.Contains("no speech-recognition provider is configured", html);
        Assert.Contains("NLP analysis (preview)", html);
        Assert.Contains("Read this word aloud: <strong class=\"assessment-preview-target\">map</strong>", html);
        Assert.Contains("Enter the sequence in order", html);
        Assert.Contains("Match each item to its symbol.", html);
        Assert.DoesNotContain("Correct answer", html, StringComparison.OrdinalIgnoreCase);

        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await verifyDb.ScreeningTestAttempts.Where(item => item.ScreeningTestId == testId).ToListAsync());
        Assert.Empty(await verifyDb.ScreeningResponses.Where(item => item.ScreeningQuestion.ScreeningTestId == testId).ToListAsync());
    }

    [Fact]
    public async Task Assessment_activation_is_antiforgery_protected_and_requires_an_active_question()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        using var adminClient = await CreateAdministratorClientAsync(factory);
        int testId;
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var test = new ScreeningTest
            {
                Name = "Empty activation readiness test",
                Slug = "empty-activation-readiness-test",
                Description = "A disposable test for activation readiness.",
                Instructions = "Answer the active question.",
                IsActive = false,
                SortOrder = 90
            };
            dbContext.ScreeningTests.Add(test);
            await dbContext.SaveChangesAsync();
            testId = test.Id;
        }

        var activationPath = $"/admin/screening-tests/{testId}/builder/activate";
        using var missingTokenForm = new FormUrlEncodedContent(new Dictionary<string, string>());
        var antiforgeryResponse = await adminClient.PostAsync(activationPath, missingTokenForm);
        Assert.Equal(HttpStatusCode.BadRequest, antiforgeryResponse.StatusCode);

        var builderResponse = await adminClient.GetAsync($"/admin/screening-tests/{testId}/builder");
        var builderHtml = await builderResponse.Content.ReadAsStringAsync();
        var token = ExtractAntiForgeryToken(builderHtml);
        using var activationForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        });
        var blockedResponse = await adminClient.PostAsync(activationPath, activationForm);
        Assert.Equal(HttpStatusCode.Redirect, blockedResponse.StatusCode);

        var blockedBuilder = await adminClient.GetAsync($"/admin/screening-tests/{testId}/builder");
        Assert.Contains("Add and activate at least one question before publishing this assessment.", await blockedBuilder.Content.ReadAsStringAsync());
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.False((await dbContext.ScreeningTests.SingleAsync(test => test.Id == testId)).IsActive);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.ScreeningQuestions.Add(new ScreeningQuestion
            {
                ScreeningTestId = testId,
                Prompt = "Inactive draft must not satisfy readiness.",
                QuestionType = ScreeningQuestionType.TextResponse,
                CorrectAnswer = "draft",
                SortOrder = 1,
                IsActive = false
            });
            await dbContext.SaveChangesAsync();
        }

        var inactiveBuilderResponse = await adminClient.GetAsync($"/admin/screening-tests/{testId}/builder");
        var inactiveBuilderHtml = await inactiveBuilderResponse.Content.ReadAsStringAsync();
        Assert.Contains("Inactive draft must not satisfy readiness.", inactiveBuilderHtml);
        using (var readinessScope = factory.Services.CreateScope())
        {
            var readiness = await readinessScope.ServiceProvider.GetRequiredService<IAssessmentReadinessService>().GetReadinessAsync(testId);
            Assert.NotNull(readiness);
            Assert.Equal(1, readiness.TotalQuestionCount);
            Assert.Equal(0, readiness.ActiveQuestionCount);
            Assert.Equal(1, readiness.InactiveQuestionCount);
            Assert.False(readiness.IsReady);
        }
        token = ExtractAntiForgeryToken(inactiveBuilderHtml);
        using var inactiveActivationForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        });
        var inactiveBlockedResponse = await adminClient.PostAsync(activationPath, inactiveActivationForm);
        Assert.Equal(HttpStatusCode.Redirect, inactiveBlockedResponse.StatusCode);
        var afterInactiveAttempt = await adminClient.GetAsync($"/admin/screening-tests/{testId}/builder");
        var afterInactiveHtml = await afterInactiveAttempt.Content.ReadAsStringAsync();
        Assert.Contains("Add and activate at least one question before publishing this assessment.", afterInactiveHtml);
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.False((await dbContext.ScreeningTests.SingleAsync(test => test.Id == testId)).IsActive);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.ScreeningQuestions.Add(new ScreeningQuestion
            {
                ScreeningTestId = testId,
                Prompt = "Choose a correct answer.",
                QuestionType = ScreeningQuestionType.MultipleChoice,
                CorrectAnswer = "yes",
                MetadataJson = JsonSerializer.Serialize(new ScreeningQuestionMetadata { ChoiceOptions = ["yes", "no"] }),
                SortOrder = 2,
                IsActive = true
            });
            await dbContext.SaveChangesAsync();
        }

        var readyBuilderResponse = await adminClient.GetAsync($"/admin/screening-tests/{testId}/builder");
        token = ExtractAntiForgeryToken(await readyBuilderResponse.Content.ReadAsStringAsync());
        using var readyActivationForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        });
        var readyResponse = await adminClient.PostAsync(activationPath, readyActivationForm);
        Assert.Equal(HttpStatusCode.Redirect, readyResponse.StatusCode);
        using var verifyScope = factory.Services.CreateScope();
        var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True((await verifyDbContext.ScreeningTests.SingleAsync(test => test.Id == testId)).IsActive);
    }

    [Fact]
    public async Task Builder_AI_timeout_returns_a_useful_message_without_saving_questions()
    {
        using var factory = new LexiAssistWebApplicationFactory
        {
            ConfigureAdditionalServices = services =>
            {
                services.RemoveAll<IAssessmentDraftGenerator>();
                services.AddSingleton<IAssessmentDraftGenerator, TimeoutAssessmentDraftGenerator>();
            }
        };
        using var client = await CreateAdministratorClientAsync(factory);
        var testId = await GetFirstTestIdAsync(factory);
        var builderResponse = await client.GetAsync($"/admin/screening-tests/{testId}/builder");
        var builderHtml = await builderResponse.Content.ReadAsStringAsync();
        var token = ExtractAntiForgeryToken(builderHtml);
        int questionsBefore;
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            questionsBefore = await dbContext.ScreeningQuestions.CountAsync(question => question.ScreeningTestId == testId);
        }

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["QuestionCount"] = "2",
            ["DifficultyMix"] = "Balanced"
        });
        var response = await client.PostAsync($"/admin/screening-tests/{testId}/builder/generate", form);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var returnedBuilder = await client.GetAsync($"/admin/screening-tests/{testId}/builder");
        Assert.Contains("AI question generation timed out. Please try again.", await returnedBuilder.Content.ReadAsStringAsync());
        using var verifyScope = factory.Services.CreateScope();
        var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(questionsBefore, await verifyDbContext.ScreeningQuestions.CountAsync(question => question.ScreeningTestId == testId));
    }

    [Fact]
    public async Task Csv_import_validates_every_row_before_saving_and_keeps_imported_questions_inactive()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        using var client = await CreateAdministratorClientAsync(factory);
        var testId = await GetFirstTestIdAsync(factory);
        var builderResponse = await client.GetAsync($"/admin/screening-tests/{testId}/builder");
        var token = ExtractAntiForgeryToken(await builderResponse.Content.ReadAsStringAsync());
        const string validCsv = "QuestionType,Prompt,CorrectAnswer,Choices,AcceptedAnswers\r\nMultipleChoice,\"Pick, the word\",cat,\"cat|dog\",\r\nTextResponse,Name the word,word,,\"word|term\"\r\n";

        using var validContent = BuildCsvForm(token, validCsv);
        var validResponse = await client.PostAsync($"/admin/screening-tests/{testId}/builder/import", validContent);
        Assert.Equal(HttpStatusCode.OK, validResponse.StatusCode);
        Assert.Contains("Imported 2 questions", await validResponse.Content.ReadAsStringAsync());

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var imported = await dbContext.ScreeningQuestions.Where(question => question.ScreeningTestId == testId).OrderBy(question => question.SortOrder).ToListAsync();
            Assert.Equal(2, imported.Count);
            Assert.All(imported, question => Assert.False(question.IsActive));
            Assert.Equal("Pick, the word", imported[0].Prompt);
        }

        const string invalidCsv = "QuestionType,Prompt,CorrectAnswer,Choices,AcceptedAnswers\r\nTextResponse,This row would be valid,answer,,\r\nMultipleChoice,Invalid choice,missing,one|two,\r\n";
        using var invalidContent = BuildCsvForm(token, invalidCsv);
        var invalidResponse = await client.PostAsync($"/admin/screening-tests/{testId}/builder/import", invalidContent);
        Assert.Equal(HttpStatusCode.OK, invalidResponse.StatusCode);
        Assert.Contains("The CSV was not imported", await invalidResponse.Content.ReadAsStringAsync());

        using var verifyScope = factory.Services.CreateScope();
        var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(2, await verifyDbContext.ScreeningQuestions.CountAsync(question => question.ScreeningTestId == testId));
    }

    [Fact]
    public async Task Ai_generated_questions_populate_the_builder_and_interpreted_csv_content_is_preserved()
    {
        var generator = new WorkspaceDraftGenerator();
        using var factory = new LexiAssistWebApplicationFactory
        {
            ConfigureAdditionalServices = services =>
            {
                services.RemoveAll<IAssessmentDraftGenerator>();
                services.AddSingleton<IAssessmentDraftGenerator>(generator);
            }
        };
        using var client = await CreateAdministratorClientAsync(factory);
        var testId = await GetFirstTestIdAsync(factory);
        var page = await client.GetStringAsync($"/admin/screening-tests/{testId}/builder");
        var token = ExtractAntiForgeryToken(page);
        var requestIdMatch = Regex.Match(page, "name=\\\"RequestId\\\" value=\\\"([^\\\"]+)\\\"");
        Assert.True(requestIdMatch.Success, "The builder should issue an idempotency key for AI generation.");
        var requestId = requestIdMatch.Groups[1].Value;
        using var generation = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["RequestId"] = requestId,
            ["QuestionCount"] = "1",
            ["DifficultyMix"] = "Progressive challenge"
        });
        var response = await client.PostAsync($"/admin/screening-tests/{testId}/builder/generate", generation);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var html = await client.GetStringAsync(GetLocationPath(response));
        Assert.Contains("Generated 1 question in the builder as inactive", html);
        Assert.Contains("Read the word.", html);
        Assert.DoesNotContain("Review generated questions", html);
        Assert.Equal("Progressive challenge", generator.LastGeneration?.DifficultyMix);
        using (var generatedScope = factory.Services.CreateScope())
        {
            var generatedDb = generatedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var generatedQuestion = await generatedDb.ScreeningQuestions.SingleAsync(question => question.ScreeningTestId == testId);
            Assert.Equal("Read the word.", generatedQuestion.Prompt);
            Assert.Equal(ScreeningQuestionType.TextResponse, generatedQuestion.QuestionType);
            Assert.False(generatedQuestion.IsActive);
            var receipt = await generatedDb.AssessmentDrafts.SingleAsync(draft => draft.ScreeningTestId == testId);
            using var receiptContent = JsonDocument.Parse(receipt.ContentJson);
            Assert.Equal(requestId, receiptContent.RootElement.GetProperty("generationRequestId").GetString());
            Assert.Equal(1, receiptContent.RootElement.GetProperty("addedIndexes").GetArrayLength());
        }

        using (var retry = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["RequestId"] = requestId,
            ["QuestionCount"] = "1",
            ["DifficultyMix"] = "Progressive challenge"
        }))
        {
            var duplicate = await client.PostAsync($"/admin/screening-tests/{testId}/builder/generate", retry);
            Assert.Equal(HttpStatusCode.Redirect, duplicate.StatusCode);
            Assert.Contains("No duplicates were created", await client.GetStringAsync(GetLocationPath(duplicate)));
        }
        Assert.Equal(1, generator.GenerationCalls);
        using (var verifyScope = factory.Services.CreateScope())
        {
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(1, await verifyDb.ScreeningQuestions.CountAsync(question => question.ScreeningTestId == testId));
        }

        const string csv = "Question,Answer\r\n\"Keep this\r\nline break\",word\r\n";
        using var import = BuildCsvForm(token, csv);
        var interpreted = await client.PostAsync($"/admin/screening-tests/{testId}/builder/import", import);
        Assert.Equal(HttpStatusCode.Redirect, interpreted.StatusCode);
        Assert.Contains("Review imported questions", await client.GetStringAsync(GetLocationPath(interpreted)));
        Assert.Equal(csv, generator.LastCsv);
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await dbContext.ScreeningQuestions.CountAsync(question => question.ScreeningTestId == testId));
        Assert.Equal(2, await dbContext.AssessmentDrafts.CountAsync(draft => draft.ScreeningTestId == testId));
    }

    [Fact]
    public async Task Draft_review_saves_edits_validates_before_add_and_makes_retry_idempotent()
    {
        var generator = new WorkspaceDraftGenerator();
        using var factory = new LexiAssistWebApplicationFactory
        {
            ConfigureAdditionalServices = services =>
            {
                services.RemoveAll<IAssessmentDraftGenerator>();
                services.AddSingleton<IAssessmentDraftGenerator>(generator);
            }
        };
        using var client = await CreateAdministratorClientAsync(factory);
        var testId = await GetFirstTestIdAsync(factory);
        var builder = await client.GetStringAsync($"/admin/screening-tests/{testId}/builder");
        var token = ExtractAntiForgeryToken(builder);
        using var import = BuildCsvForm(token, "Question,Answer\r\nRead this word,word\r\n");
        var generated = await client.PostAsync($"/admin/screening-tests/{testId}/builder/import", import);
        Assert.Equal(HttpStatusCode.Redirect, generated.StatusCode);
        var draftUrl = GetLocationPath(generated);
        var reviewHtml = await client.GetStringAsync(draftUrl);
        Assert.Contains("Review imported questions", reviewHtml);
        Assert.Contains("These are review drafts. Only selected questions are added, and they remain inactive until an administrator activates them.", reviewHtml);
        token = ExtractAntiForgeryToken(reviewHtml);
        var draftMatch = Regex.Match(draftUrl, @"/drafts/([0-9a-f-]{36})", RegexOptions.IgnoreCase);
        Assert.True(draftMatch.Success, "Generation should create a durable review draft.");
        var draftId = Guid.Parse(draftMatch.Groups[1].Value);
        var route = $"/admin/screening-tests/{testId}/builder/drafts/{draftId}";
        var learnerPreview = await client.GetStringAsync(route + "/questions/0/preview");
        Assert.Contains("Read the word.", learnerPreview);
        Assert.DoesNotContain("name=\"CorrectAnswer\"", learnerPreview);
        Assert.DoesNotContain("Answer the active questions.", learnerPreview);
        var editedPrompt = "Read the reviewed vocabulary word.";

        using var save = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["ScreeningTestId"] = testId.ToString(),
            ["Version"] = "0",
            ["ActiveQuestionIndex"] = "0",
            ["DifficultyMix"] = "Balanced",
            ["SelectedIndexes[0]"] = "0",
            ["Questions[0].Prompt"] = editedPrompt,
            ["Questions[0].QuestionType"] = ((int)ScreeningQuestionType.TextResponse).ToString(),
            ["Questions[0].CorrectAnswer"] = "word",
            ["Questions[0].ChoicesText"] = "",
            ["Questions[0].AcceptedAnswersText"] = "",
            ["Questions[0].Weight"] = "2.5"
        });
        save.Headers.Add("X-Requested-With", "XMLHttpRequest");
        var saved = await client.PostAsync(route + "/save", save);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        using (var saveJson = JsonDocument.Parse(await saved.Content.ReadAsStringAsync()))
        {
            Assert.True(saveJson.RootElement.GetProperty("saved").GetBoolean());
            Assert.Equal(1, saveJson.RootElement.GetProperty("version").GetInt32());
        }

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(0, await dbContext.ScreeningQuestions.CountAsync(question => question.ScreeningTestId == testId));
            var draft = await dbContext.AssessmentDrafts.SingleAsync(item => item.Id == draftId);
            Assert.Contains(editedPrompt, draft.ContentJson);
            using var draftContent = JsonDocument.Parse(draft.ContentJson);
            Assert.Equal(2.5, draftContent.RootElement.GetProperty("questions")[0].GetProperty("weight").GetDouble());
            Assert.Equal(1, draft.Version);
        }
        var savedReview = await client.GetStringAsync(route);
        Assert.Contains("id=\"question-weight-0\" name=\"Questions[0].Weight\" type=\"number\" min=\"0.1\" max=\"100\" step=\"0.1\" value=\"2.5\"", savedReview);

        async Task<HttpResponseMessage> AddAsync(string prompt, int version)
        {
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["ScreeningTestId"] = testId.ToString(),
                ["Version"] = version.ToString(),
                ["ActiveQuestionIndex"] = "0",
                ["DifficultyMix"] = "Balanced",
                ["SelectedIndexes[0]"] = "0",
                ["Questions[0].Prompt"] = prompt,
                ["Questions[0].QuestionType"] = ((int)ScreeningQuestionType.TextResponse).ToString(),
                ["Questions[0].CorrectAnswer"] = "word",
                ["Questions[0].ChoicesText"] = "",
                ["Questions[0].AcceptedAnswersText"] = "",
                ["Questions[0].Weight"] = "1"
            });
            form.Headers.Add("X-Requested-With", "XMLHttpRequest");
            return await client.PostAsync(route + "/add-selected", form);
        }

        using (var invalidAdd = await AddAsync("Hi", 1))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, invalidAdd.StatusCode);
            using var invalidJson = JsonDocument.Parse(await invalidAdd.Content.ReadAsStringAsync());
            Assert.Equal(0, invalidJson.RootElement.GetProperty("invalidIndexes")[0].GetInt32());
            Assert.Equal(2, invalidJson.RootElement.GetProperty("version").GetInt32());
        }
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(0, await dbContext.ScreeningQuestions.CountAsync(question => question.ScreeningTestId == testId));
        }

        using (var add = await AddAsync(editedPrompt, 2))
        {
            Assert.Equal(HttpStatusCode.OK, add.StatusCode);
            using var addJson = JsonDocument.Parse(await add.Content.ReadAsStringAsync());
            Assert.Equal(1, addJson.RootElement.GetProperty("added").GetInt32());
            Assert.False(addJson.RootElement.TryGetProperty("duplicate", out _));
        }
        using (var retry = await AddAsync(editedPrompt, 2))
        {
            Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
            using var retryJson = JsonDocument.Parse(await retry.Content.ReadAsStringAsync());
            Assert.True(retryJson.RootElement.GetProperty("duplicate").GetBoolean());
        }
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var added = await dbContext.ScreeningQuestions.Where(question => question.ScreeningTestId == testId).ToListAsync();
            Assert.Single(added);
            Assert.Equal(editedPrompt, added[0].Prompt);
            Assert.False(added[0].IsActive);
            Assert.True((await dbContext.AssessmentDrafts.SingleAsync(item => item.Id == draftId)).ContentJson.Contains("addedIndexes", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task Draft_regeneration_failure_keeps_the_original_and_a_retry_replaces_only_that_question()
    {
        var generator = new RegenerationRetryDraftGenerator();
        using var factory = new LexiAssistWebApplicationFactory
        {
            ConfigureAdditionalServices = services =>
            {
                services.RemoveAll<IAssessmentDraftGenerator>();
                services.AddSingleton<IAssessmentDraftGenerator>(generator);
            }
        };
        using var client = await CreateAdministratorClientAsync(factory);
        var testId = await GetFirstTestIdAsync(factory);
        var builderHtml = await client.GetStringAsync($"/admin/screening-tests/{testId}/builder");
        var token = ExtractAntiForgeryToken(builderHtml);
        using var import = BuildCsvForm(token, "Question,Answer\r\nRead the original word aloud,map\r\n");
        var generated = await client.PostAsync($"/admin/screening-tests/{testId}/builder/import", import);
        Assert.Equal(HttpStatusCode.Redirect, generated.StatusCode);
        var draftUrl = GetLocationPath(generated);
        var match = Regex.Match(draftUrl, @"/drafts/([0-9a-f-]{36})", RegexOptions.IgnoreCase);
        Assert.True(match.Success);
        var draftId = Guid.Parse(match.Groups[1].Value);
        var review = await client.GetStringAsync(draftUrl);
        token = ExtractAntiForgeryToken(review);
        var route = $"/admin/screening-tests/{testId}/builder/drafts/{draftId}/regenerate";

        async Task<HttpResponseMessage> RegenerateAsync(int version)
        {
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["index"] = "0",
                ["version"] = version.ToString()
            });
            form.Headers.Add("X-Requested-With", "XMLHttpRequest");
            return await client.PostAsync(route, form);
        }

        string originalJson;
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            originalJson = (await dbContext.AssessmentDrafts.SingleAsync(item => item.Id == draftId)).ContentJson;
        }
        using (var failed = await RegenerateAsync(0))
        {
            Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
            using var payload = JsonDocument.Parse(await failed.Content.ReadAsStringAsync());
            Assert.Contains("original draft was kept", payload.RootElement.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
        }
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var unchanged = await dbContext.AssessmentDrafts.SingleAsync(item => item.Id == draftId);
            Assert.Equal(originalJson, unchanged.ContentJson);
            Assert.Equal(0, unchanged.Version);
            Assert.Equal(0, await dbContext.ScreeningQuestions.CountAsync(question => question.ScreeningTestId == testId));
        }

        using (var retried = await RegenerateAsync(0))
        {
            Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
            using var payload = JsonDocument.Parse(await retried.Content.ReadAsStringAsync());
            Assert.True(payload.RootElement.GetProperty("regenerated").GetBoolean());
            Assert.Equal(1, payload.RootElement.GetProperty("version").GetInt32());
        }
        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var replacement = await verifyDb.AssessmentDrafts.SingleAsync(item => item.Id == draftId);
        Assert.Contains("Read the replacement word aloud: sun.", replacement.ContentJson);
        Assert.Equal(1, replacement.Version);
        Assert.Equal(2, generator.GenerationCalls);
    }

    [Fact]
    public async Task Gzip_csv_import_preserves_question_text_and_inactive_status()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        using var client = await CreateAdministratorClientAsync(factory);
        var testId = await GetFirstTestIdAsync(factory);
        var token = ExtractAntiForgeryToken(await client.GetStringAsync($"/admin/screening-tests/{testId}/builder"));
        const string prompt = "Read this word.\r\nKeep the next line, too.";
        var csv = $"QuestionType,Prompt,CorrectAnswer,Choices,AcceptedAnswers\r\nTextResponse,\"{prompt}\",word,,\r\n";
        using var bytes = new MemoryStream();
        using (var gzip = new System.IO.Compression.GZipStream(bytes, System.IO.Compression.CompressionMode.Compress, leaveOpen: true))
        {
            await gzip.WriteAsync(Encoding.UTF8.GetBytes(csv));
        }
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(token), "__RequestVerificationToken");
        form.Add(new StringContent("true"), "csvCompressed");
        form.Add(new ByteArrayContent(bytes.ToArray()), "csvFile", "questions.csv");
        var response = await client.PostAsync($"/admin/screening-tests/{testId}/builder/import", form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var question = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .ScreeningQuestions.SingleAsync(question => question.ScreeningTestId == testId);
        Assert.Equal(prompt, question.Prompt);
        Assert.False(question.IsActive);
    }

    [Fact]
    public async Task Question_bank_copy_uses_a_new_identity_and_preserves_question_configuration()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        using var client = await CreateAdministratorClientAsync(factory);
        int targetTestId;
        int sourceTestId;
        int sourceQuestionId;
        const string originalPrompt = "Choose the matching word from the sound.";
        const string metadata = "{\"choiceOptions\":[\"cat\",\"dog\"]}";
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tests = await dbContext.ScreeningTests.OrderBy(test => test.Id).Take(2).ToListAsync();
            targetTestId = tests[0].Id;
            sourceTestId = tests[1].Id;
            var source = new ScreeningQuestion
            {
                ScreeningTestId = sourceTestId,
                Prompt = originalPrompt,
                QuestionType = ScreeningQuestionType.MultipleChoice,
                CorrectAnswer = "cat",
                MetadataJson = metadata,
                Weight = 2.5,
                SortOrder = 3,
                IsActive = true
            };
            dbContext.ScreeningQuestions.Add(source);
            await dbContext.SaveChangesAsync();
            sourceQuestionId = source.Id;
        }

        var bankResponse = await client.GetAsync($"/admin/screening-tests/{targetTestId}/builder/question-bank");
        Assert.Equal(HttpStatusCode.OK, bankResponse.StatusCode);
        var token = ExtractAntiForgeryToken(await bankResponse.Content.ReadAsStringAsync());
        using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token });
        var copyResponse = await client.PostAsync($"/admin/screening-tests/{targetTestId}/builder/question-bank/{sourceQuestionId}/copy", form);
        Assert.Equal(HttpStatusCode.Redirect, copyResponse.StatusCode);

        using var verifyScope = factory.Services.CreateScope();
        var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var copied = await verifyDbContext.ScreeningQuestions.SingleAsync(question => question.ScreeningTestId == targetTestId);
        var sourceAfterCopy = await verifyDbContext.ScreeningQuestions.SingleAsync(question => question.Id == sourceQuestionId);
        Assert.NotEqual(sourceQuestionId, copied.Id);
        Assert.Equal(originalPrompt, copied.Prompt);
        Assert.Equal(ScreeningQuestionType.MultipleChoice, copied.QuestionType);
        Assert.Equal("cat", copied.CorrectAnswer);
        Assert.Equal(metadata, copied.MetadataJson);
        Assert.Equal(2.5, copied.Weight);
        Assert.False(copied.IsActive);
        Assert.Equal(originalPrompt, sourceAfterCopy.Prompt);
        Assert.True(sourceAfterCopy.IsActive);
    }

    [Fact]
    public async Task Astra_draft_generator_uses_responses_api_and_structured_output_without_network_access()
    {
        var structuredText = JsonSerializer.Serialize(new
        {
            questions = new[]
            {
                new { prompt = "Which word rhymes with cat?", questionType = "MultipleChoice", correctAnswer = "hat", choices = new[] { "hat", "sun" }, acceptedAnswers = Array.Empty<string>() }
            }
        });
        var responseText = JsonSerializer.Serialize(new
        {
            output = new[]
            {
                new { content = new[] { new { type = "output_text", text = structuredText } } }
            }
        });
        using var handler = new StubResponseHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseText, Encoding.UTF8, "application/json")
        }));
        using var httpClient = new HttpClient(handler);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OPENAI_API_KEY"] = "test-key-only"
        }).Build();
        IAssessmentDraftGenerator generator = new OpenAiAssessmentDraftGenerator(httpClient, configuration);

        var generated = await generator.GenerateAsync(new AssessmentDraftGenerationInput(
            "Word Reading",
            "Recognise common words.",
            "Choose one answer.",
            1,
            "Balanced",
            []));

        Assert.True(generator.IsConfigured);
        Assert.Single(generated);
        Assert.Equal(ScreeningQuestionType.MultipleChoice, generated[0].QuestionType);
        Assert.Equal("hat", generated[0].CorrectAnswer);
        Assert.Equal("https://api.openai.com/v1/responses", handler.RequestUri);
        Assert.Equal("Bearer test-key-only", handler.Authorization);
        using var sentRequest = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("gpt-6-astra", sentRequest.RootElement.GetProperty("model").GetString());
        Assert.Equal("json_schema", sentRequest.RootElement.GetProperty("text").GetProperty("format").GetProperty("type").GetString());
    }

    [Fact]
    public async Task Astra_draft_generator_rejects_a_missing_api_key_before_sending()
    {
        using var handler = new StubResponseHandler((_, _) => throw new InvalidOperationException("The provider should not be called."));
        using var httpClient = new HttpClient(handler);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        IAssessmentDraftGenerator generator = new OpenAiAssessmentDraftGenerator(httpClient, configuration);

        Assert.False(generator.IsConfigured);
        await Assert.ThrowsAsync<InvalidOperationException>(() => generator.GenerateAsync(CreateDraftGenerationInput()));
        Assert.Null(handler.RequestUri);
    }

    [Fact]
    public async Task Astra_draft_generator_preserves_provider_http_status_without_exposing_response_body()
    {
        using var handler = new StubResponseHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("sensitive provider detail")
        }));
        using var httpClient = new HttpClient(handler);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OPENAI_API_KEY"] = "test-key-only"
        }).Build();
        IAssessmentDraftGenerator generator = new OpenAiAssessmentDraftGenerator(httpClient, configuration);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => generator.GenerateAsync(CreateDraftGenerationInput()));

        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
        Assert.DoesNotContain("sensitive provider detail", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Astra_draft_generator_rejects_malformed_structured_output()
    {
        var responseText = JsonSerializer.Serialize(new
        {
            output = new[]
            {
                new { content = new[] { new { type = "output_text", text = "{ malformed" } } }
            }
        });
        using var handler = new StubResponseHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseText, Encoding.UTF8, "application/json")
        }));
        using var httpClient = new HttpClient(handler);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OPENAI_API_KEY"] = "test-key-only"
        }).Build();
        IAssessmentDraftGenerator generator = new OpenAiAssessmentDraftGenerator(httpClient, configuration);

        await Assert.ThrowsAsync<JsonException>(() => generator.GenerateAsync(CreateDraftGenerationInput()));
    }

    [Fact]
    public async Task Astra_draft_generator_rejects_an_empty_structured_response()
    {
        var responseText = JsonSerializer.Serialize(new { output = Array.Empty<object>() });
        using var handler = new StubResponseHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseText, Encoding.UTF8, "application/json")
        }));
        using var httpClient = new HttpClient(handler);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OPENAI_API_KEY"] = "test-key-only"
        }).Build();
        IAssessmentDraftGenerator generator = new OpenAiAssessmentDraftGenerator(httpClient, configuration);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => generator.GenerateAsync(CreateDraftGenerationInput()));

        Assert.Equal("OpenAI did not return structured question text.", error.Message);
    }

    private static AssessmentDraftGenerationInput CreateDraftGenerationInput() => new(
        "Word Reading",
        "Recognise common words.",
        "Choose one answer.",
        1,
        "Balanced",
        []);

    private static async Task<int> CreateAssessmentAsync(LexiAssistWebApplicationFactory factory, string slug)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var test = new ScreeningTest
        {
            Name = "Configure Review " + slug,
            Slug = slug,
            Description = "A disposable assessment for Configure and Review.",
            Instructions = "Answer the active questions.",
            IsActive = false,
            SortOrder = 99
        };
        dbContext.ScreeningTests.Add(test);
        await dbContext.SaveChangesAsync();
        return test.Id;
    }

    private static async Task<HttpClient> CreateAdministratorClientAsync(LexiAssistWebApplicationFactory factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (email, password) = await factory.CreateUserAsync(ApplicationRoles.Administrator);
        await SignInAsync(client, email, password);
        return client;
    }

    private static async Task<int> GetFirstTestIdAsync(LexiAssistWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await dbContext.ScreeningTests.OrderBy(test => test.SortOrder).Select(test => test.Id).FirstAsync();
    }

    private static MultipartFormDataContent BuildCsvForm(string token, string csv)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(token), "__RequestVerificationToken");
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(fileContent, "csvFile", "questions.csv");
        return form;
    }

    private static async Task SignInAsync(HttpClient client, string email, string password)
    {
        var loginPage = await client.GetAsync("/Account/Login");
        var token = ExtractAntiForgeryToken(await loginPage.Content.ReadAsStringAsync());
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Email"] = email,
            ["Password"] = password,
            ["RememberMe"] = "false"
        });
        var result = await client.PostAsync("/Account/Login", form);
        Assert.Equal(HttpStatusCode.Redirect, result.StatusCode);
    }

    private static string ExtractAntiForgeryToken(string html)
    {
        var match = Regex.Match(html, "<input[^>]*name=\\\"__RequestVerificationToken\\\"[^>]*value=\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase);
        Assert.True(match.Success, "The rendered page should include an antiforgery token.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static string GetLocationPath(HttpResponseMessage response)
    {
        var location = response.Headers.Location;
        return location is null ? string.Empty : location.IsAbsoluteUri ? location.PathAndQuery : location.OriginalString;
    }

    private sealed class StubResponseHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync) : HttpMessageHandler
    {
        public string? RequestUri { get; private set; }
        public string? Authorization { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri?.ToString();
            Authorization = request.Headers.Authorization?.ToString();
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return await sendAsync(request, cancellationToken);
        }
    }

    private sealed class WorkspaceDraftGenerator : IAssessmentDraftGenerator
    {
        public bool IsConfigured => true;
        public string? LastCsv { get; private set; }
        public AssessmentDraftGenerationInput? LastGeneration { get; private set; }
        public int GenerationCalls { get; private set; }
        private static IReadOnlyList<AssessmentDraftQuestion> Drafts =>
            [new("Read the word.", ScreeningQuestionType.TextResponse, "word", [], [])];
        public Task<IReadOnlyList<AssessmentDraftQuestion>> GenerateAsync(AssessmentDraftGenerationInput input, CancellationToken cancellationToken = default)
        {
            GenerationCalls++;
            LastGeneration = input;
            return Task.FromResult(Drafts);
        }
        public Task<IReadOnlyList<AssessmentDraftQuestion>> InterpretCsvAsync(CsvQuestionInterpretationInput input, CancellationToken cancellationToken = default)
        {
            LastCsv = input.CsvText;
            return Task.FromResult(Drafts);
        }
    }

    private sealed class RegenerationRetryDraftGenerator : IAssessmentDraftGenerator
    {
        public bool IsConfigured => true;
        public int GenerationCalls { get; private set; }

        public Task<IReadOnlyList<AssessmentDraftQuestion>> GenerateAsync(
            AssessmentDraftGenerationInput input,
            CancellationToken cancellationToken = default)
        {
            GenerationCalls++;
            if (GenerationCalls == 1)
            {
                return Task.FromException<IReadOnlyList<AssessmentDraftQuestion>>(new HttpRequestException("The test provider failed."));
            }

            IReadOnlyList<AssessmentDraftQuestion> drafts = [new("Read the replacement word aloud: sun.", ScreeningQuestionType.WordReading, "sun", [], [])];
            return Task.FromResult(drafts);
        }

        public Task<IReadOnlyList<AssessmentDraftQuestion>> InterpretCsvAsync(
            CsvQuestionInterpretationInput input,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<AssessmentDraftQuestion> drafts = [new("Read the original word aloud: map.", ScreeningQuestionType.WordReading, "map", [], [])];
            return Task.FromResult(drafts);
        }
    }

    private sealed class TimeoutAssessmentDraftGenerator : IAssessmentDraftGenerator
    {
        public bool IsConfigured => true;

        public Task<IReadOnlyList<AssessmentDraftQuestion>> GenerateAsync(
            AssessmentDraftGenerationInput input,
            CancellationToken cancellationToken = default) =>
            Task.FromException<IReadOnlyList<AssessmentDraftQuestion>>(new TaskCanceledException("Provider timeout."));

        public Task<IReadOnlyList<AssessmentDraftQuestion>> InterpretCsvAsync(
            CsvQuestionInterpretationInput input,
            CancellationToken cancellationToken = default) =>
            Task.FromException<IReadOnlyList<AssessmentDraftQuestion>>(new NotSupportedException("CSV interpretation is not configured for this test helper."));
    }
}
