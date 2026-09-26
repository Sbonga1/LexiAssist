using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Screening;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LexiAssist.Tests;

public sealed class ScreeningDomainTests
{
    [Fact]
    public async Task Screening_categories_are_seeded_once_with_optional_category_disabled()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await dbContext.Database.MigrateAsync();
        await dbContext.Database.MigrateAsync();

        var categories = await dbContext.ScreeningTests
            .AsNoTracking()
            .OrderBy(test => test.SortOrder)
            .ToListAsync();

        Assert.Equal(
            new[]
            {
                "Word Reading",
                "Decoding / Non-word Reading",
                "Phonological Awareness",
                "Rapid Naming",
                "Spelling",
                "Reading Fluency",
                "Reading Comprehension",
                "Oral Language and Listening Comprehension",
                "Memory and Broader Thinking Skills"
            },
            categories.Select(test => test.Name));
        Assert.Equal(9, categories.Select(test => test.Slug).Distinct().Count());
        Assert.All(categories.Take(8), category => Assert.True(category.IsActive));
        Assert.False(categories[^1].IsActive);
        Assert.Contains("Optional", categories[^1].Description);
        Assert.Empty(await dbContext.ScreeningQuestions.ToListAsync());
    }

    [Fact]
    public async Task Screening_test_stores_multiple_question_types_and_cascades_questions_on_delete()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var questionTypes = Enum.GetValues<ScreeningQuestionType>();

        Assert.NotEmpty(questionTypes);

        var screeningTest = new ScreeningTest
        {
            Name = "Foundation test",
            Slug = $"foundation-{Guid.NewGuid():N}",
            Description = "A test record for screening schema coverage.",
            Instructions = "Answer each question.",
            Questions = questionTypes.Select((questionType, index) => new ScreeningQuestion
            {
                Prompt = $"Question {index + 1}",
                QuestionType = questionType,
                CorrectAnswer = index == 0 ? "A" : null,
                AcceptedAnswersJson = index == 1 ? "[\"sample\"]" : null,
                SortOrder = index
            }).ToList()
        };

        dbContext.ScreeningTests.Add(screeningTest);
        await dbContext.SaveChangesAsync();

        var savedTest = await dbContext.ScreeningTests
            .Include(test => test.Questions)
            .SingleAsync(test => test.Id == screeningTest.Id);
        Assert.Equal(questionTypes.Length, savedTest.Questions.Count);
        Assert.Equal(questionTypes.Order(), savedTest.Questions.Select(question => question.QuestionType).Order());
        Assert.Contains(savedTest.Questions, question => question.CorrectAnswer == "A");
        Assert.Contains(savedTest.Questions, question => question.AcceptedAnswersJson == "[\"sample\"]");

        dbContext.ScreeningTests.Remove(savedTest);
        await dbContext.SaveChangesAsync();

        Assert.Empty(await dbContext.ScreeningQuestions.ToListAsync());
    }
}
