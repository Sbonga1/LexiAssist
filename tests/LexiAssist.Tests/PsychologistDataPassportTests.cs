using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Identity;
using LexiAssist.Web.Models.Referrals;
using LexiAssist.Web.Models.Reports;
using LexiAssist.Web.Models.Recordings;
using LexiAssist.Web.Models.Screening;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LexiAssist.Tests;

public sealed class PsychologistDataPassportTests
{
    [Fact]
    public async Task Assigned_psychologist_sees_only_stored_passport_data_and_can_play_authorized_recordings()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        var (psychologistId, psychologistEmail, psychologistPassword) = await CreateUserAsync(
            factory,
            ApplicationRoles.Psychologist,
            "Morgan",
            "Professional");
        var (_, otherPsychologistEmail, otherPsychologistPassword) = await CreateUserAsync(
            factory,
            ApplicationRoles.Psychologist,
            "Taylor",
            "Professional");
        var (learnerId, learnerEmail, learnerPassword) = await CreateUserAsync(
            factory,
            ApplicationRoles.Learner,
            "Jamie",
            "Reader",
            "Jay");

        var recordingName = $"passport-{Guid.NewGuid():N}.wav";
        string? recordingPath = null;
        string? privateRecordingPath = null;
        string? unflaggedRecordingPath = null;
        var privateRecordingName = $"{Guid.NewGuid():N}.wav";
        var privateAudioBytes = new byte[] { 0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00, 0x57, 0x41, 0x56, 0x45 };
        var unflaggedRecordingName = $"{Guid.NewGuid():N}.wav";
        int referralId;
        int recordingResponseId;
        int timedNamingResponseId;
        int privateRecordingId;
        try
        {
            using (var scope = factory.Services.CreateScope())
            {
                var environment = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
                var recordingDirectory = Path.Combine(environment.ContentRootPath, "App_Data", "ScreeningRecordings");
                Directory.CreateDirectory(recordingDirectory);
                recordingPath = Path.Combine(recordingDirectory, recordingName);
                await File.WriteAllBytesAsync(recordingPath, [0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00]);
                var privateRecordingDirectory = Path.Combine(environment.ContentRootPath, "App_Data", "PrivateAudioRecordings");
                Directory.CreateDirectory(privateRecordingDirectory);
                privateRecordingPath = Path.Combine(privateRecordingDirectory, privateRecordingName);
                await File.WriteAllBytesAsync(privateRecordingPath, privateAudioBytes);
                unflaggedRecordingPath = Path.Combine(privateRecordingDirectory, unflaggedRecordingName);
                await File.WriteAllBytesAsync(unflaggedRecordingPath, privateAudioBytes);

                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var learner = await userManager.FindByIdAsync(learnerId);
                Assert.NotNull(learner);
                learner.DateOfBirth = new DateOnly(2014, 3, 12);
                Assert.True((await userManager.UpdateAsync(learner)).Succeeded);

                var now = new DateTime(2026, 8, 25, 11, 30, 0, DateTimeKind.Utc);
                var attempt = new ScreeningAttempt
                {
                    UserId = learnerId,
                    StartedAtUtc = now.AddMinutes(-25),
                    CompletedAtUtc = now,
                    Status = ScreeningAttemptStatus.Completed,
                    OverallScore = 68,
                    RiskTier = "Red"
                };
                dbContext.ScreeningAttempts.Add(attempt);
                await dbContext.SaveChangesAsync();

                var earlierAttempt = new ScreeningAttempt
                {
                    UserId = learnerId,
                    StartedAtUtc = now.AddMonths(-6).AddMinutes(-20),
                    CompletedAtUtc = now.AddMonths(-6),
                    Status = ScreeningAttemptStatus.Completed,
                    OverallScore = 54,
                    RiskTier = "Amber"
                };
                dbContext.ScreeningAttempts.Add(earlierAttempt);

                var screeningTest = new ScreeningTest
                {
                    Name = "Passport Demo Category",
                    Slug = $"passport-demo-{Guid.NewGuid():N}",
                    Description = "Test data for the screening data passport.",
                    Instructions = "Use only stored test data.",
                    SortOrder = 1
                };
                dbContext.ScreeningTests.Add(screeningTest);
                await dbContext.SaveChangesAsync();

                var testAttempt = new ScreeningTestAttempt
                {
                    ScreeningAttemptId = attempt.Id,
                    ScreeningTestId = screeningTest.Id,
                    StartedAtUtc = now.AddMinutes(-20),
                    CompletedAtUtc = now.AddMinutes(-10),
                    RawScore = 2,
                    PercentageScore = 50,
                    HasFlaggedSkillArea = true,
                    DurationMilliseconds = 600_000
                };
                dbContext.ScreeningTestAttempts.Add(testAttempt);
                var multipleChoiceQuestion = new ScreeningQuestion
                {
                    ScreeningTestId = screeningTest.Id,
                    Prompt = "DEMO: select one answer",
                    QuestionType = ScreeningQuestionType.MultipleChoice
                };
                var timedNamingQuestion = new ScreeningQuestion
                {
                    ScreeningTestId = screeningTest.Id,
                    Prompt = "DEMO: name symbols",
                    QuestionType = ScreeningQuestionType.TimedNaming
                };
                var fluencyQuestion = new ScreeningQuestion
                {
                    ScreeningTestId = screeningTest.Id,
                    Prompt = "DEMO: read a passage",
                    QuestionType = ScreeningQuestionType.ReadingFluency
                };
                dbContext.ScreeningQuestions.AddRange(multipleChoiceQuestion, timedNamingQuestion, fluencyQuestion);
                await dbContext.SaveChangesAsync();

                var privateRecording = new LearnerAudioRecording
                {
                    UserId = learnerId,
                    ScreeningTestAttemptId = testAttempt.Id,
                    ScreeningQuestionId = multipleChoiceQuestion.Id,
                    CreatedAtUtc = now.AddMinutes(-19),
                    ConsentAcceptedAtUtc = now.AddMinutes(-20),
                    ConsentNoticeVersion = "learner-audio-v1",
                    StartedAtUtc = now.AddMinutes(-20),
                    UploadedAtUtc = now.AddMinutes(-19),
                    DurationMilliseconds = 1_200,
                    FileSizeBytes = privateAudioBytes.Length,
                    ContentType = "audio/wav",
                    StorageFileName = privateRecordingName,
                    Status = LearnerAudioRecordingStatus.Available
                };
                dbContext.LearnerAudioRecordings.Add(privateRecording);
                await dbContext.SaveChangesAsync();
                privateRecordingId = privateRecording.Id;

                var unflaggedTest = new ScreeningTest
                {
                    Name = "Unflagged Demo Category",
                    Slug = $"unflagged-demo-{Guid.NewGuid():N}",
                    Description = "Test data for unflagged recording filtering.",
                    Instructions = "Use only stored test data.",
                    SortOrder = 2
                };
                dbContext.ScreeningTests.Add(unflaggedTest);
                await dbContext.SaveChangesAsync();
                var unflaggedQuestion = new ScreeningQuestion
                {
                    ScreeningTestId = unflaggedTest.Id,
                    Prompt = "DEMO: unflagged item",
                    QuestionType = ScreeningQuestionType.WordReading
                };
                var unflaggedTestAttempt = new ScreeningTestAttempt
                {
                    ScreeningAttemptId = attempt.Id,
                    ScreeningTestId = unflaggedTest.Id,
                    StartedAtUtc = now.AddMinutes(-9),
                    CompletedAtUtc = now.AddMinutes(-8),
                    PercentageScore = 95,
                    HasFlaggedSkillArea = false,
                    DurationMilliseconds = 60_000
                };
                dbContext.ScreeningQuestions.Add(unflaggedQuestion);
                dbContext.ScreeningTestAttempts.Add(unflaggedTestAttempt);
                await dbContext.SaveChangesAsync();
                dbContext.LearnerAudioRecordings.Add(new LearnerAudioRecording
                {
                    UserId = learnerId,
                    ScreeningTestAttemptId = unflaggedTestAttempt.Id,
                    ScreeningQuestionId = unflaggedQuestion.Id,
                    CreatedAtUtc = now.AddMinutes(-9),
                    ConsentAcceptedAtUtc = now.AddMinutes(-9),
                    ConsentNoticeVersion = "learner-audio-v1",
                    StartedAtUtc = now.AddMinutes(-9),
                    UploadedAtUtc = now.AddMinutes(-8),
                    DurationMilliseconds = 1_000,
                    FileSizeBytes = privateAudioBytes.Length,
                    ContentType = "audio/wav",
                    StorageFileName = unflaggedRecordingName,
                    Status = LearnerAudioRecordingStatus.Available
                });
                await dbContext.SaveChangesAsync();

                dbContext.ScreeningResponses.AddRange(
                    new ScreeningResponse
                    {
                        ScreeningTestAttemptId = testAttempt.Id,
                        ScreeningQuestionId = multipleChoiceQuestion.Id,
                        SelectedAnswer = "A",
                        IsCorrect = false,
                        Score = 0,
                        ResponseTimeMilliseconds = 1_200,
                        AudioRecordingPath = recordingName,
                        CreatedAtUtc = now.AddMinutes(-19)
                    },
                    new ScreeningResponse
                    {
                        ScreeningTestAttemptId = testAttempt.Id,
                        ScreeningQuestionId = timedNamingQuestion.Id,
                        IsCorrect = null,
                        ResponseTimeMilliseconds = 20_000,
                        ResponseDetailsJson = "{\"SelfReportedErrorIndexes\":[1,3]}",
                        CreatedAtUtc = now.AddMinutes(-18)
                    },
                    new ScreeningResponse
                    {
                        ScreeningTestAttemptId = testAttempt.Id,
                        ScreeningQuestionId = fluencyQuestion.Id,
                        IsCorrect = null,
                        ResponseTimeMilliseconds = 30_000,
                        ResponseDetailsJson = "{\"SelfReportedCorrectionCount\":2}",
                        CreatedAtUtc = now.AddMinutes(-17)
                    });
                await dbContext.SaveChangesAsync();
                recordingResponseId = await dbContext.ScreeningResponses
                    .Where(response => response.ScreeningQuestionId == multipleChoiceQuestion.Id)
                    .Select(response => response.Id)
                    .SingleAsync();
                timedNamingResponseId = await dbContext.ScreeningResponses
                    .Where(response => response.ScreeningQuestionId == timedNamingQuestion.Id)
                    .Select(response => response.Id)
                    .SingleAsync();

                var report = new ScreeningReport
                {
                    ScreeningAttemptId = attempt.Id,
                    UserId = learnerId,
                    GeneratedAtUtc = now.AddMinutes(1),
                    RiskTier = "Red",
                    Summary = "Stored educational screening summary; not a diagnosis.",
                    FlaggedAreasJson = "[\"Word Reading\"]",
                    ScoringVersion = "passport-test-v1",
                    IsReferralRecommended = true,
                    SkillAreasJson = JsonSerializer.Serialize(new[]
                    {
                        new ScreeningReportSkillAreaSnapshot("Word Reading", 50m, false, true, "Stored area for support review."),
                        new ScreeningReportSkillAreaSnapshot("Memory and Broader Thinking Skills", 67m, false, true, "Stored optional screening indicator.")
                        {
                            ActivityMetrics =
                            [
                                new ScreeningActivityMetric(ScreeningQuestionType.WorkingMemorySequence, "Working Memory", 3, 2, 1, 3, 66.67m, 3, null, null, null, 2m, 3m, 66.67m),
                                new ScreeningActivityMetric(ScreeningQuestionType.TimedMatching, "Processing Speed", 4, 3, 1, 4, 75m, null, 1200, null, null, 3m, 4m, 75m),
                                new ScreeningActivityMetric(ScreeningQuestionType.PatternReasoning, "Pattern Reasoning", 2, 1, 1, 2, 50m, null, null, null, null, 1m, 2m, 50m)
                            ]
                        }
                    }),
                    RecommendedNextAction = "Professional review"
                };
                dbContext.ScreeningReports.Add(report);
                await dbContext.SaveChangesAsync();

                dbContext.ScreeningReports.Add(new ScreeningReport
                {
                    ScreeningAttemptId = earlierAttempt.Id,
                    UserId = learnerId,
                    GeneratedAtUtc = now.AddMonths(-6).AddMinutes(1),
                    RiskTier = "Amber",
                    Summary = "Earlier stored screening summary.",
                    ScoringVersion = "passport-test-v1",
                    SkillAreasJson = JsonSerializer.Serialize(new[]
                    {
                        new ScreeningReportSkillAreaSnapshot("Memory and Broader Thinking Skills", 50m, false, false, "Earlier optional screening indicator.")
                        {
                            ActivityMetrics =
                            [
                                new ScreeningActivityMetric(ScreeningQuestionType.WorkingMemorySequence, "Working Memory", 3, 1, 2, 3, 33.33m, 2, null, null, null, 1m, 3m, 33.33m),
                                new ScreeningActivityMetric(ScreeningQuestionType.TimedMatching, "Processing Speed", 4, 2, 2, 4, 50m, null, 1500, null, null, 2m, 4m, 50m),
                                new ScreeningActivityMetric(ScreeningQuestionType.PatternReasoning, "Pattern Reasoning", 2, 1, 1, 2, 50m, null, null, null, null, 1m, 2m, 50m)
                            ]
                        }
                    })
                });
                await dbContext.SaveChangesAsync();

                var referral = new Referral
                {
                    ScreeningReportId = report.Id,
                    LearnerId = learnerId,
                    AssignedPsychologistId = psychologistId,
                    Status = ReferralStatus.Assigned,
                    CreatedAtUtc = now.AddDays(1),
                    Notes = "Professional note for a support discussion."
                };
                dbContext.Referrals.Add(referral);
                await dbContext.SaveChangesAsync();
                referralId = referral.Id;
            }

            using var psychologistClient = CreateClient(factory);
            await SignInAsync(psychologistClient, psychologistEmail, psychologistPassword);

            var passport = await psychologistClient.GetAsync($"/psychologist/referrals/{referralId}/data-passport");
            Assert.Equal(HttpStatusCode.OK, passport.StatusCode);
            var html = await passport.Content.ReadAsStringAsync();
            Assert.Contains("Diagnostic Data Passport", html);
            Assert.Contains("not an automated medical diagnosis", html);
            Assert.Contains("Jay", html);
            Assert.Contains(learnerEmail, html);
            Assert.Contains("12 March 2014", html);
            Assert.Contains("Word Reading", html);
            Assert.Contains("50%", html);
            Assert.Contains("Flagged for support review", html);
            Assert.Contains("Memory and Broader Thinking Skills", html);
            Assert.Contains("Working Memory", html);
            Assert.Contains("Processing Speed", html);
            Assert.Contains("Pattern Reasoning", html);
            Assert.Contains("Screening indicator", html);
            Assert.Contains("Additional professional interpretation may be appropriate.", html);
            Assert.Contains("Comparable previous screening indicators", html);
            Assert.Matches(@"Elapsed: 1(?:[,\s]*)200 ms", WebUtility.HtmlDecode(html));
            Assert.DoesNotContain("intellectual disability", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Self-reported naming errors", html);
            Assert.Contains("Self-reported reading corrections", html);
            var responseSectionStart = html.IndexOf("Response and error categories", StringComparison.Ordinal);
            var responseSection = WebUtility.HtmlDecode(responseSectionStart < 0 ? html : html[responseSectionStart..]);
            Assert.Matches(@"1(?:[,\s]*)200 ms", responseSection);
            Assert.Matches(@"600(?:[,\s]*)000 ms", WebUtility.HtmlDecode(html));
            Assert.Contains("54%", html);
            Assert.Contains("Amber", html);
            Assert.Contains("Professional note for a support discussion.", html);
            Assert.Contains($"/psychologist/referrals/{referralId}/recordings/{recordingResponseId}", html);
            Assert.Contains("Audio snippet bank", html);
            Assert.Contains("DEMO: select one answer", html);
            Assert.DoesNotContain("DEMO: unflagged item", html);
            Assert.Contains($"/learner-audio-recordings/{privateRecordingId}/play", html);
            Assert.DoesNotContain(privateRecordingName, html);

            var taggingToken = ExtractAntiForgeryToken(html);
            var firstTagSave = await SaveCategoriesAsync(
                psychologistClient,
                referralId,
                recordingResponseId,
                taggingToken,
                ScreeningErrorCategory.ShortVowel,
                ScreeningErrorCategory.Omission);
            var secondTagSave = await SaveCategoriesAsync(
                psychologistClient,
                referralId,
                timedNamingResponseId,
                taggingToken,
                ScreeningErrorCategory.ShortVowel);
            Assert.Equal(HttpStatusCode.Redirect, firstTagSave.StatusCode);
            Assert.Equal(HttpStatusCode.Redirect, secondTagSave.StatusCode);

            var privateNoteSave = await psychologistClient.PostAsync(
                $"/psychologist/referrals/{referralId}/recordings/{privateRecordingId}/note",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] = taggingToken,
                    ["ProfessionalNote"] = "Listen for the initial consonant blend."
                }));
            var responseNoteSave = await psychologistClient.PostAsync(
                $"/psychologist/referrals/{referralId}/recordings/responses/{recordingResponseId}/note",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] = taggingToken,
                    ["ProfessionalNote"] = "The response includes a useful short vowel example."
                }));
            Assert.Equal(HttpStatusCode.Redirect, privateNoteSave.StatusCode);
            Assert.Equal(HttpStatusCode.Redirect, responseNoteSave.StatusCode);
            var tooLongNote = await psychologistClient.PostAsync(
                $"/psychologist/referrals/{referralId}/recordings/{privateRecordingId}/note",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] = taggingToken,
                    ["ProfessionalNote"] = new string('x', 501)
                }));
            Assert.Equal(HttpStatusCode.BadRequest, tooLongNote.StatusCode);
            Assert.Equal(privateAudioBytes, await File.ReadAllBytesAsync(privateRecordingPath!));

            using (var verifyScope = factory.Services.CreateScope())
            {
                var verifyContext = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var savedRecording = await verifyContext.LearnerAudioRecordings.SingleAsync(item => item.Id == privateRecordingId);
                var savedResponse = await verifyContext.ScreeningResponses.SingleAsync(item => item.Id == recordingResponseId);
                Assert.Equal("Listen for the initial consonant blend.", savedRecording.ProfessionalNote);
                Assert.NotNull(savedRecording.ProfessionalNoteUpdatedAtUtc);
                Assert.Equal("The response includes a useful short vowel example.", savedResponse.ProfessionalAudioNote);
                Assert.NotNull(savedResponse.ProfessionalAudioNoteUpdatedAtUtc);
            }

            var updatedPassport = await psychologistClient.GetAsync($"/psychologist/referrals/{referralId}/data-passport");
            Assert.Contains("Listen for the initial consonant blend.", await updatedPassport.Content.ReadAsStringAsync());

            using (var verifyScope = factory.Services.CreateScope())
            {
                var verifyContext = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                Assert.Equal(3, await verifyContext.ScreeningResponseErrorCategories.CountAsync());
            }

            var analyticsResponse = await psychologistClient.GetAsync("/psychologist/referrals/error-analytics");
            Assert.Equal(HttpStatusCode.OK, analyticsResponse.StatusCode);
            var analyticsHtml = await analyticsResponse.Content.ReadAsStringAsync();
            var analyticsText = Regex.Replace(WebUtility.HtmlDecode(analyticsHtml), "<[^>]+>", " ");
            Assert.Matches(@"2\s+distinct tagged response\(s\)", analyticsText);
            Assert.Contains("Short vowel", analyticsHtml);
            Assert.Contains("Omission", analyticsHtml);
            Assert.Contains("100%", analyticsHtml);
            Assert.Contains("50%", analyticsHtml);

            var recording = await psychologistClient.GetAsync($"/psychologist/referrals/{referralId}/recordings/{recordingResponseId}");
            Assert.Equal(HttpStatusCode.OK, recording.StatusCode);
            Assert.Equal("audio/wav", recording.Content.Headers.ContentType?.MediaType);
            Assert.Equal("nosniff", recording.Headers.GetValues("X-Content-Type-Options").Single());
            Assert.Contains("private", recording.Headers.CacheControl?.ToString());
            Assert.Contains("no-store", recording.Headers.CacheControl?.ToString());
            Assert.Equal(new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0 }, await recording.Content.ReadAsByteArrayAsync());

            var privatePlayback = await psychologistClient.GetAsync($"/learner-audio-recordings/{privateRecordingId}/play");
            Assert.Equal(HttpStatusCode.OK, privatePlayback.StatusCode);
            Assert.Equal(privateAudioBytes, await privatePlayback.Content.ReadAsByteArrayAsync());

            using var otherPsychologistClient = CreateClient(factory);
            var otherToken = await SignInAsync(otherPsychologistClient, otherPsychologistEmail, otherPsychologistPassword);
            Assert.Equal(HttpStatusCode.NotFound, (await otherPsychologistClient.GetAsync($"/psychologist/referrals/{referralId}/data-passport")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await otherPsychologistClient.GetAsync($"/psychologist/referrals/{referralId}/recordings/{recordingResponseId}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await otherPsychologistClient.GetAsync($"/learner-audio-recordings/{privateRecordingId}/play")).StatusCode);
            var otherReferralList = await otherPsychologistClient.GetAsync("/psychologist/referrals");
            Assert.Equal(HttpStatusCode.OK, otherReferralList.StatusCode);
            otherToken = ExtractAntiForgeryToken(await otherReferralList.Content.ReadAsStringAsync());
            var otherNoteSave = await otherPsychologistClient.PostAsync(
                $"/psychologist/referrals/{referralId}/recordings/{privateRecordingId}/note",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] = otherToken,
                    ["ProfessionalNote"] = "Not authorized"
                }));
            Assert.Equal(HttpStatusCode.NotFound, otherNoteSave.StatusCode);
            var emptyAnalytics = await otherPsychologistClient.GetAsync("/psychologist/referrals/error-analytics");
            Assert.Equal(HttpStatusCode.OK, emptyAnalytics.StatusCode);
            Assert.Contains("No assigned screening responses have been tagged", await emptyAnalytics.Content.ReadAsStringAsync());

            using var learnerClient = CreateClient(factory);
            await SignInAsync(learnerClient, learnerEmail, learnerPassword);
            var learnerAccess = await learnerClient.GetAsync($"/psychologist/referrals/{referralId}/data-passport");
            Assert.Equal(HttpStatusCode.Redirect, learnerAccess.StatusCode);
            Assert.StartsWith("/Account/AccessDenied", GetLocationPath(learnerAccess));
            var learnerAnalytics = await learnerClient.GetAsync("/psychologist/referrals/error-analytics");
            Assert.Equal(HttpStatusCode.Redirect, learnerAnalytics.StatusCode);
            Assert.StartsWith("/Account/AccessDenied", GetLocationPath(learnerAnalytics));
        }
        finally
        {
            if (recordingPath is not null && File.Exists(recordingPath))
            {
                File.Delete(recordingPath);
            }

            if (privateRecordingPath is not null && File.Exists(privateRecordingPath))
            {
                File.Delete(privateRecordingPath);
            }

            if (unflaggedRecordingPath is not null && File.Exists(unflaggedRecordingPath))
            {
                File.Delete(unflaggedRecordingPath);
            }
        }
    }

    [Fact]
    public async Task Passport_route_is_limited_to_assigned_psychologists()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        using var client = CreateClient(factory);
        var response = await client.GetAsync("/psychologist/referrals/1/data-passport");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/Login", GetLocationPath(response));
    }

    private static async Task<(string Id, string Email, string Password)> CreateUserAsync(
        LexiAssistWebApplicationFactory factory,
        string role,
        string firstName,
        string lastName,
        string? preferredName = null)
    {
        var (email, password) = await factory.CreateUserAsync(role);
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.NotNull(user);
        user.FirstName = firstName;
        user.LastName = lastName;
        user.PreferredName = preferredName;
        var result = await userManager.UpdateAsync(user);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(error => error.Description)));
        return (user.Id, email, password);
    }

    private static HttpClient CreateClient(LexiAssistWebApplicationFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static Task<HttpResponseMessage> SaveCategoriesAsync(
        HttpClient client,
        int referralId,
        int responseId,
        string token,
        params ScreeningErrorCategory[] categories)
    {
        var formValues = new List<KeyValuePair<string, string>>
        {
            new("__RequestVerificationToken", token)
        };
        formValues.AddRange(categories.Select(category => new KeyValuePair<string, string>("ErrorCategories", category.ToString())));
        return client.PostAsync(
            $"/psychologist/referrals/{referralId}/responses/{responseId}/error-categories",
            new FormUrlEncodedContent(formValues));
    }

    private static async Task<string> SignInAsync(HttpClient client, string email, string password)
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
        return token;
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
        return location is null
            ? string.Empty
            : location.IsAbsoluteUri
                ? location.PathAndQuery
                : location.OriginalString;
    }
}
