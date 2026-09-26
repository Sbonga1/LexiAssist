using System.Text.Json;
using System.Text.RegularExpressions;
using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Activities;
using LexiAssist.Web.Models.Identity;
using LexiAssist.Web.Services;
using LexiAssist.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LexiAssist.Tests;

public sealed class PracticeStoryServiceTests
{
    [Fact]
    public void Template_generation_is_deterministic_and_contains_every_target_word()
    {
        var generator = new PracticeStoryTemplateGenerator();
        var first = generator.Generate("sh", ["ship", "shell", "fish"], "Space", 2);
        var second = generator.Generate("sh", ["ship", "shell", "fish"], "Space", 2);

        Assert.Equal(first.Theme, second.Theme);
        Assert.Equal(first.TargetPhoneticPattern, second.TargetPhoneticPattern);
        Assert.Equal(first.TargetWords, second.TargetWords);
        Assert.Equal(first.DifficultyLevel, second.DifficultyLevel);
        Assert.Equal(first.StoryText, second.StoryText);
        Assert.Equal(first.GenerationMethod, second.GenerationMethod);
        Assert.Equal("Space", first.Theme);
        Assert.Equal("sh", first.TargetPhoneticPattern);
        Assert.Equal(PracticeStoryTemplateGenerator.GenerationMethod, first.GenerationMethod);
        Assert.All(first.TargetWords, word => Assert.Contains(word, first.StoryText, StringComparison.Ordinal));
        Assert.Equal(3, Regex.Matches(first.StoryText, @"[.!?](?:\s|$)").Count);
        Assert.Contains("space mission", first.StoryText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Provider_neutral_interface_uses_the_template_default_and_supports_async_replacement()
    {
        IPracticeContentGenerator generator = new TemplatePracticeContentGenerator();
        var request = new PracticeContentGenerationRequest("sh", ["ship"], "Space", 2);

        var syncResult = generator.Generate(request);
        var asyncResult = await generator.GenerateAsync(request);

        Assert.IsType<TemplatePracticeContentGenerator>(generator);
        Assert.Equal(syncResult.StoryText, asyncResult.StoryText);
        Assert.Equal(syncResult.GenerationMethod, asyncResult.GenerationMethod);
    }

    [Fact]
    public void Application_registers_template_generator_as_the_default_provider()
    {
        using var factory = new LexiAssistWebApplicationFactory();

        var generator = factory.Services.GetRequiredService<IPracticeContentGenerator>();

        Assert.IsType<TemplatePracticeContentGenerator>(generator);
    }

    [Fact]
    public void Unknown_interests_use_a_generic_template_without_an_interest_enum()
    {
        var generator = new PracticeStoryTemplateGenerator();
        var draft = generator.Generate("ai", ["robot"], "Robotics", 4);

        Assert.Equal("Robotics", draft.Theme);
        Assert.Contains("Robotics", draft.StoryText, StringComparison.Ordinal);
        Assert.Contains("new sentence", draft.StoryText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Generated_story_persists_inputs_and_generation_metadata()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        var credentials = await factory.CreateUserAsync(ApplicationRoles.Learner);

        using (var scope = factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IPracticeStoryService>();
            var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>();
            var user = await userManager.FindByEmailAsync(credentials.Email);
            Assert.NotNull(user);

            var story = await service.GenerateAsync(
                user.Id,
                "igh",
                ["light", "bright"],
                "Technology",
                3);

            Assert.True(story.Id > 0);
            Assert.Equal(user.Id, story.LearnerId);
            Assert.Equal("Technology", story.Theme);
            Assert.Equal("igh", story.TargetPhoneticPattern);
            Assert.Equal(3, story.DifficultyLevel);
            Assert.Equal(PracticeStoryTemplateGenerator.GenerationMethod, story.GenerationMethod);
            Assert.Contains("light", story.StoryText, StringComparison.Ordinal);
            Assert.Contains("bright", story.StoryText, StringComparison.Ordinal);

            var saved = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
                .PracticeStories
                .SingleAsync(item => item.Id == story.Id);
            Assert.Equal(new[] { "light", "bright" },
                JsonSerializer.Deserialize<string[]>(saved.TargetWordsJson));
        }
    }

    [Fact]
    public void Invalid_story_inputs_are_rejected()
    {
        var generator = new PracticeStoryTemplateGenerator();

        Assert.Throws<ArgumentException>(() => generator.Generate("", ["word"], "Space", 1));
        Assert.Throws<ArgumentException>(() => generator.Generate("sh", [], "Space", 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => generator.Generate("sh", ["word"], "Space", 6));
    }
}
