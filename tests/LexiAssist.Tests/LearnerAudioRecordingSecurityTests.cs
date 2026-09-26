using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Activities;
using LexiAssist.Web.Models.Identity;
using LexiAssist.Web.Models.Recordings;
using LexiAssist.Web.Models.Referrals;
using LexiAssist.Web.Models.Reports;
using LexiAssist.Web.Models.Screening;
using LexiAssist.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LexiAssist.Tests;

public sealed class LearnerAudioRecordingSecurityTests
{
    [Fact]
    public async Task Screening_audio_requires_consent_validates_upload_and_limits_playback_to_authorized_people()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        var learner = await factory.CreateUserAsync(ApplicationRoles.Learner);
        var unrelatedLearner = await factory.CreateUserAsync(ApplicationRoles.Learner);
        var administrator = await factory.CreateUserAsync(ApplicationRoles.Administrator);
        var assignedPsychologist = await factory.CreateUserAsync(ApplicationRoles.Psychologist);
        var unrelatedPsychologist = await factory.CreateUserAsync(ApplicationRoles.Psychologist);
        var storageDirectory = Path.Combine(Path.GetTempPath(), $"lexiassist-recordings-{Guid.NewGuid():N}");
        int testAttemptId;
        int questionId;
        string learnerId;
        string assignedPsychologistId;

        try
        {
            using (var scope = factory.Services.CreateScope())
            {
                scope.ServiceProvider.GetRequiredService<IOptions<LearnerAudioRecordingOptions>>().Value.StorageDirectory = storageDirectory;
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var learnerUser = await userManager.FindByEmailAsync(learner.Email);
                var psychologistUser = await userManager.FindByEmailAsync(assignedPsychologist.Email);
                Assert.NotNull(learnerUser);
                Assert.NotNull(psychologistUser);
                learnerId = learnerUser.Id;
                assignedPsychologistId = psychologistUser.Id;

                var attempt = new ScreeningAttempt
                {
                    UserId = learnerId,
                    Status = ScreeningAttemptStatus.InProgress,
                    StartedAtUtc = DateTime.UtcNow
                };
                dbContext.ScreeningAttempts.Add(attempt);
                var question = new ScreeningQuestion
                {
                    ScreeningTestId = 1,
                    Prompt = "DEMO: say the word aloud",
                    QuestionType = ScreeningQuestionType.WordReading,
                    AllowsAudioRecording = true,
                    SortOrder = 1
                };
                dbContext.ScreeningQuestions.Add(question);
                await dbContext.SaveChangesAsync();
                var testAttempt = new ScreeningTestAttempt
                {
                    ScreeningAttemptId = attempt.Id,
                    ScreeningTestId = 1,
                    StartedAtUtc = DateTime.UtcNow
                };
                dbContext.ScreeningTestAttempts.Add(testAttempt);
                await dbContext.SaveChangesAsync();
                testAttemptId = testAttempt.Id;
                questionId = question.Id;
            }

            using var learnerClient = await SignInAsync(factory, learner.Email, learner.Password);
            var runnerResponse = await learnerClient.GetAsync("/screening/resume");
            Assert.Equal(HttpStatusCode.OK, runnerResponse.StatusCode);
            var runnerHtml = await runnerResponse.Content.ReadAsStringAsync();
            Assert.Contains("I understand and consent", runnerHtml);
            Assert.Contains("Start recording", runnerHtml);
            Assert.Contains("Stop and save", runnerHtml);
            var token = ExtractAntiForgeryToken(runnerHtml);

            var consentRejected = await learnerClient.PostAsync(
                $"/learner-audio-recordings/screening-test-attempts/{testAttemptId}/questions/{questionId}/sessions",
                Form(token, consentAccepted: false));
            Assert.Equal(HttpStatusCode.BadRequest, consentRejected.StatusCode);

            var sessionResponse = await learnerClient.PostAsync(
                $"/learner-audio-recordings/screening-test-attempts/{testAttemptId}/questions/{questionId}/sessions",
                Form(token, consentAccepted: true));
            Assert.Equal(HttpStatusCode.OK, sessionResponse.StatusCode);
            using var sessionJson = JsonDocument.Parse(await sessionResponse.Content.ReadAsStringAsync());
            var completeUrl = sessionJson.RootElement.GetProperty("completeUrl").GetString();
            Assert.NotNull(completeUrl);
            var recordingId = sessionJson.RootElement.GetProperty("recordingId").GetInt32();

            var invalidDuration = await UploadAsync(
                learnerClient, completeUrl, token, "audio/webm", [0x1A, 0x45, 0xDF, 0xA3], durationMilliseconds: 120_001);
            Assert.Equal(HttpStatusCode.BadRequest, invalidDuration.StatusCode);

            var invalidAudio = await UploadAsync(learnerClient, completeUrl, token, "audio/wav", [0x01, 0x02, 0x03, 0x04]);
            Assert.Equal(HttpStatusCode.BadRequest, invalidAudio.StatusCode);

            var oversizedAudio = new byte[4 * 1024 * 1024 + 1];
            oversizedAudio[0] = 0x1A;
            oversizedAudio[1] = 0x45;
            oversizedAudio[2] = 0xDF;
            oversizedAudio[3] = 0xA3;
            var oversized = await UploadAsync(learnerClient, completeUrl, token, "audio/webm", oversizedAudio);
            Assert.Equal(HttpStatusCode.BadRequest, oversized.StatusCode);

            var saved = await UploadAsync(
                learnerClient,
                completeUrl,
                token,
                "audio/webm;codecs=opus",
                [0x1A, 0x45, 0xDF, 0xA3, 0x01, 0x02, 0x03]);
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            using var savedJson = JsonDocument.Parse(await saved.Content.ReadAsStringAsync());
            var playbackUrl = savedJson.RootElement.GetProperty("playbackUrl").GetString();
            Assert.NotNull(playbackUrl);

            using (var scope = factory.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var recording = await dbContext.LearnerAudioRecordings.SingleAsync(item => item.Id == recordingId);
                Assert.Equal(LearnerAudioRecordingStatus.Available, recording.Status);
                Assert.Equal(learnerId, recording.UserId);
                Assert.Equal("audio/webm", recording.ContentType);
                Assert.Equal(7, recording.FileSizeBytes);
                Assert.Equal(250, recording.DurationMilliseconds);
                Assert.NotEqual(default, recording.ConsentAcceptedAtUtc);
                Assert.Equal(LearnerAudioRecordingService.ConsentNoticeVersion, recording.ConsentNoticeVersion);
                Assert.Matches(@"\A[0-9a-f]{32}\.webm\z", recording.StorageFileName);
                Assert.True(File.Exists(Path.Combine(storageDirectory, recording.StorageFileName!)));
                var environment = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
                Assert.False(Path.GetFullPath(Path.Combine(storageDirectory, recording.StorageFileName!))
                    .StartsWith(Path.GetFullPath(environment.WebRootPath!), StringComparison.OrdinalIgnoreCase));

                var screeningAttemptId = await dbContext.ScreeningTestAttempts
                    .Where(item => item.Id == testAttemptId)
                    .Select(item => item.ScreeningAttemptId)
                    .SingleAsync();
                var attempt = await dbContext.ScreeningAttempts.SingleAsync(item => item.Id == screeningAttemptId);
                var report = new ScreeningReport
                {
                    ScreeningAttemptId = attempt.Id,
                    UserId = learnerId,
                    GeneratedAtUtc = DateTime.UtcNow,
                    RiskTier = "Red",
                    Summary = "Stored support summary.",
                    FlaggedAreasJson = "[]",
                    ScoringVersion = "test",
                    IsReferralRecommended = true,
                    SkillAreasJson = "[]",
                    RecommendedNextAction = "Professional review"
                };
                dbContext.ScreeningReports.Add(report);
                await dbContext.SaveChangesAsync();
                dbContext.Referrals.Add(new Referral
                {
                    ScreeningReportId = report.Id,
                    LearnerId = learnerId,
                    AssignedPsychologistId = assignedPsychologistId,
                    Status = ReferralStatus.Assigned,
                    CreatedAtUtc = DateTime.UtcNow
                });
                await dbContext.SaveChangesAsync();
            }

            var ownerPlayback = await learnerClient.GetAsync(playbackUrl);
            Assert.Equal(HttpStatusCode.OK, ownerPlayback.StatusCode);
            Assert.Contains("private", ownerPlayback.Headers.CacheControl?.ToString());
            Assert.Contains("no-store", ownerPlayback.Headers.CacheControl?.ToString());
            Assert.Equal("nosniff", ownerPlayback.Headers.GetValues("X-Content-Type-Options").Single());
            Assert.Equal("audio/webm", ownerPlayback.Content.Headers.ContentType?.MediaType);

            using var unrelatedClient = await SignInAsync(factory, unrelatedLearner.Email, unrelatedLearner.Password);
            var unrelatedPlayback = await unrelatedClient.GetAsync(playbackUrl);
            Assert.Equal(HttpStatusCode.NotFound, unrelatedPlayback.StatusCode);

            using var assignedClient = await SignInAsync(factory, assignedPsychologist.Email, assignedPsychologist.Password);
            var assignedPlayback = await assignedClient.GetAsync(playbackUrl);
            Assert.Equal(HttpStatusCode.OK, assignedPlayback.StatusCode);

            // Defense-in-depth: a malformed referral association must not grant access
            // to a recording belonging to a different learner.
            using (var scope = factory.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var unrelatedLearnerUser = await dbContext.Users.SingleAsync(user => user.Email == unrelatedLearner.Email);
                var referral = await dbContext.Referrals.SingleAsync(item => item.AssignedPsychologistId == assignedPsychologistId);
                referral.LearnerId = unrelatedLearnerUser.Id;
                await dbContext.SaveChangesAsync();
            }

            var mismatchedReferralPlayback = await assignedClient.GetAsync(playbackUrl);
            Assert.Equal(HttpStatusCode.NotFound, mismatchedReferralPlayback.StatusCode);

            using var unassignedClient = await SignInAsync(factory, unrelatedPsychologist.Email, unrelatedPsychologist.Password);
            var unassignedPlayback = await unassignedClient.GetAsync(playbackUrl);
            Assert.Equal(HttpStatusCode.NotFound, unassignedPlayback.StatusCode);

            using var adminClient = await SignInAsync(factory, administrator.Email, administrator.Password);
            var adminPlayback = await adminClient.GetAsync(playbackUrl);
            Assert.Equal(HttpStatusCode.OK, adminPlayback.StatusCode);
        }
        finally
        {
            if (Directory.Exists(storageDirectory))
            {
                Directory.Delete(storageDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Activity_recording_requires_explicit_activity_permission_and_assignment_ownership()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        var learner = await factory.CreateUserAsync(ApplicationRoles.Learner);
        var unrelatedLearner = await factory.CreateUserAsync(ApplicationRoles.Learner);
        int activityAttemptId;
        int assignmentId;
        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var learnerUser = await userManager.FindByEmailAsync(learner.Email);
            Assert.NotNull(learnerUser);
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var activity = await dbContext.LearningActivities.SingleAsync(item => item.Id == 1);
            Assert.False(activity.AllowsAudioRecording);
            activity.AllowsAudioRecording = true;
            var assignment = new ActivityAssignment
            {
                LearnerId = learnerUser.Id,
                LearningActivityId = activity.Id,
                AssignedAtUtc = DateTime.UtcNow
            };
            dbContext.ActivityAssignments.Add(assignment);
            await dbContext.SaveChangesAsync();
            assignmentId = assignment.Id;
            var attempt = new ActivityAttempt
            {
                ActivityAssignmentId = assignment.Id,
                StartedAtUtc = DateTime.UtcNow
            };
            dbContext.ActivityAttempts.Add(attempt);
            await dbContext.SaveChangesAsync();
            activityAttemptId = attempt.Id;
        }

        using var ownerClient = await SignInAsync(factory, learner.Email, learner.Password);
        var runnerResponse = await ownerClient.GetAsync($"/activities/{assignmentId}/sound-builder");
        Assert.Equal(HttpStatusCode.OK, runnerResponse.StatusCode);
        var token = ExtractAntiForgeryToken(await runnerResponse.Content.ReadAsStringAsync());
        var ownerStart = await ownerClient.PostAsync(
            $"/learner-audio-recordings/activity-attempts/{activityAttemptId}/sessions",
            Form(token, consentAccepted: true));
        Assert.Equal(HttpStatusCode.OK, ownerStart.StatusCode);

        using var unrelatedClient = await SignInAsync(factory, unrelatedLearner.Email, unrelatedLearner.Password);
        var unrelatedStartPage = await unrelatedClient.GetAsync("/Settings/Accessibility");
        var unrelatedToken = ExtractAntiForgeryToken(await unrelatedStartPage.Content.ReadAsStringAsync());
        var unrelatedStart = await unrelatedClient.PostAsync(
            $"/learner-audio-recordings/activity-attempts/{activityAttemptId}/sessions",
            Form(unrelatedToken, consentAccepted: true));
        Assert.Equal(HttpStatusCode.NotFound, unrelatedStart.StatusCode);
    }

    private static FormUrlEncodedContent Form(string token, bool consentAccepted) => new(new Dictionary<string, string>
    {
        ["__RequestVerificationToken"] = token,
        ["consentAccepted"] = consentAccepted ? "true" : "false"
    });

    private static async Task<HttpResponseMessage> UploadAsync(
        HttpClient client,
        string completeUrl,
        string token,
        string contentType,
        byte[] audio,
        long durationMilliseconds = 250)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(audio);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType.Split(';')[0]);
        form.Add(file, "AudioFile", "../../client-controlled-name.wav");
        form.Add(new StringContent(durationMilliseconds.ToString()), "DurationMilliseconds");
        form.Add(new StringContent(token), "__RequestVerificationToken");
        return await client.PostAsync(completeUrl, form);
    }

    private static async Task<HttpClient> SignInAsync(LexiAssistWebApplicationFactory factory, string email, string password)
    {
        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var loginPage = await client.GetAsync("/Account/Login");
        var token = ExtractAntiForgeryToken(await loginPage.Content.ReadAsStringAsync());
        var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Email"] = email,
            ["Password"] = password,
            ["RememberMe"] = "false"
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }

    private static string ExtractAntiForgeryToken(string html)
    {
        var match = Regex.Match(html,
            "<input[^>]*name=\\\"__RequestVerificationToken\\\"[^>]*value=\\\"([^\\\"]+)\\\"",
            RegexOptions.IgnoreCase);
        Assert.True(match.Success, "An antiforgery token should be present on the rendered page.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
}
