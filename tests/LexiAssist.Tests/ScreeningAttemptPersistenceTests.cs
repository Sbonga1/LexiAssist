using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Identity;
using LexiAssist.Web.Models.Screening;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LexiAssist.Tests;

public sealed class ScreeningAttemptPersistenceTests
{
    [Fact]
    public async Task Attempt_test_attempt_and_response_are_persisted_with_nullable_scores()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        var (email, _) = await factory.CreateUserAsync(ApplicationRoles.Learner);
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.NotNull(user);

        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var screeningQuestion = new ScreeningQuestion
        {
            ScreeningTestId = 1,
            Prompt = "Test response prompt",
            QuestionType = ScreeningQuestionType.TextResponse,
            SortOrder = 1
        };
        dbContext.ScreeningQuestions.Add(screeningQuestion);
        await dbContext.SaveChangesAsync();

        var startedAt = new DateTime(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc);
        var screeningAttempt = new ScreeningAttempt
        {
            UserId = user.Id,
            StartedAtUtc = startedAt,
            Status = ScreeningAttemptStatus.InProgress,
            CurrentTestId = null,
            OverallScore = null,
            RiskTier = null
        };
        var testAttempt = new ScreeningTestAttempt
        {
            ScreeningAttempt = screeningAttempt,
            ScreeningTestId = 1,
            StartedAtUtc = startedAt,
            CompletedAtUtc = null,
            RawScore = null,
            PercentageScore = null,
            DurationMilliseconds = null
        };
        var response = new ScreeningResponse
        {
            ScreeningTestAttempt = testAttempt,
            ScreeningQuestionId = screeningQuestion.Id,
            TextAnswer = "A learner response",
            SelectedAnswer = null,
            IsCorrect = null,
            Score = null,
            ResponseTimeMilliseconds = null,
            AudioRecordingPath = null,
            CreatedAtUtc = startedAt.AddSeconds(30)
        };
        testAttempt.Responses.Add(response);
        screeningAttempt.TestAttempts.Add(testAttempt);

        dbContext.ScreeningAttempts.Add(screeningAttempt);
        await dbContext.SaveChangesAsync();

        var savedAttempt = await dbContext.ScreeningAttempts
            .AsNoTracking()
            .Include(attempt => attempt.TestAttempts)
            .ThenInclude(attempt => attempt.Responses)
            .SingleAsync(attempt => attempt.Id == screeningAttempt.Id);

        Assert.Equal(user.Id, savedAttempt.UserId);
        Assert.Equal(startedAt, savedAttempt.StartedAtUtc);
        Assert.Equal(ScreeningAttemptStatus.InProgress, savedAttempt.Status);
        Assert.Null(savedAttempt.CurrentTestId);
        Assert.Null(savedAttempt.CompletedAtUtc);
        Assert.Null(savedAttempt.OverallScore);
        Assert.Null(savedAttempt.RiskTier);

        var savedTestAttempt = Assert.Single(savedAttempt.TestAttempts);
        Assert.Equal(1, savedTestAttempt.ScreeningTestId);
        Assert.Null(savedTestAttempt.CompletedAtUtc);
        Assert.Null(savedTestAttempt.RawScore);
        Assert.Null(savedTestAttempt.PercentageScore);
        Assert.Null(savedTestAttempt.DurationMilliseconds);

        var savedResponse = Assert.Single(savedTestAttempt.Responses);
        Assert.Equal(screeningQuestion.Id, savedResponse.ScreeningQuestionId);
        Assert.Equal("A learner response", savedResponse.TextAnswer);
        Assert.Null(savedResponse.SelectedAnswer);
        Assert.Null(savedResponse.IsCorrect);
        Assert.Null(savedResponse.Score);
        Assert.Null(savedResponse.ResponseTimeMilliseconds);
        Assert.Null(savedResponse.AudioRecordingPath);
        Assert.Equal(startedAt.AddSeconds(30), savedResponse.CreatedAtUtc);
    }

    [Fact]
    public async Task Question_used_by_a_response_cannot_be_deleted()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        var (email, _) = await factory.CreateUserAsync(ApplicationRoles.Learner);
        int questionId;

        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByEmailAsync(email);
            Assert.NotNull(user);

            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var question = new ScreeningQuestion
            {
                ScreeningTestId = 1,
                Prompt = "Protected question",
                QuestionType = ScreeningQuestionType.WordReading,
                SortOrder = 1
            };
            dbContext.ScreeningQuestions.Add(question);
            await dbContext.SaveChangesAsync();
            questionId = question.Id;

            var attempt = new ScreeningAttempt
            {
                UserId = user.Id,
                StartedAtUtc = DateTime.UtcNow,
                Status = ScreeningAttemptStatus.Completed,
                CompletedAtUtc = DateTime.UtcNow
            };
            var testAttempt = new ScreeningTestAttempt
            {
                ScreeningAttempt = attempt,
                ScreeningTestId = 1,
                StartedAtUtc = DateTime.UtcNow,
                CompletedAtUtc = DateTime.UtcNow
            };
            var response = new ScreeningResponse
            {
                ScreeningTestAttempt = testAttempt,
                ScreeningQuestionId = question.Id,
                CreatedAtUtc = DateTime.UtcNow
            };
            testAttempt.Responses.Add(response);
            attempt.TestAttempts.Add(testAttempt);
            dbContext.ScreeningAttempts.Add(attempt);
            await dbContext.SaveChangesAsync();
        }

        using (var deletionScope = factory.Services.CreateScope())
        {
            var deletionDbContext = deletionScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var question = await deletionDbContext.ScreeningQuestions.SingleAsync(item => item.Id == questionId);
            deletionDbContext.ScreeningQuestions.Remove(question);
            await Assert.ThrowsAsync<DbUpdateException>(() => deletionDbContext.SaveChangesAsync());
        }

        using var verificationScope = factory.Services.CreateScope();
        var verificationDbContext = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.NotNull(await verificationDbContext.ScreeningQuestions.SingleOrDefaultAsync(question => question.Id == questionId));
        Assert.Single(await verificationDbContext.ScreeningResponses.Where(response => response.ScreeningQuestionId == questionId).ToListAsync());
    }
}
