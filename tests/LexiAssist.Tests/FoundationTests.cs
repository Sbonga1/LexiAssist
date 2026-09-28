using System.Net;
using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LexiAssist.Tests;

public sealed class FoundationTests
{
    [Fact]
    public async Task Application_starts_with_required_roles_and_a_sql_server_database()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var connectionString = dbContext.Database.GetConnectionString();

        Assert.Contains("LexiAssistTests_", connectionString, StringComparison.OrdinalIgnoreCase);
        Assert.True(await roleManager.RoleExistsAsync(ApplicationRoles.Learner));
        Assert.True(await roleManager.RoleExistsAsync(ApplicationRoles.Administrator));
        Assert.True(await roleManager.RoleExistsAsync(ApplicationRoles.Psychologist));
        Assert.Equal(3, await roleManager.Roles.CountAsync());
    }

    [Fact]
    public async Task Development_initialization_is_idempotent_and_does_not_create_demo_users()
    {
        using var factory = new LexiAssistWebApplicationFactory();

        // The host initializes this fresh SQL Server database once. Running the same
        // initialization service again must not duplicate roles or categories.
        using (var scope = factory.Services.CreateScope())
        {
            var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializationService>();
            await initializer.InitializeAsync();
        }

        using var verifyScope = factory.Services.CreateScope();
        var roleManager = verifyScope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var dbContext = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.Equal(3, await roleManager.Roles.CountAsync());
        Assert.Equal(9, await dbContext.ScreeningTests.CountAsync());
        Assert.Equal(9, await dbContext.ScreeningTests.Select(test => test.Name).Distinct().CountAsync());
        Assert.Equal(9, await dbContext.ScreeningTests.Select(test => test.Slug).Distinct().CountAsync());
        Assert.Equal(5, await dbContext.LearningActivities.CountAsync());
        Assert.Equal(5, await dbContext.LearningActivities.Select(activity => activity.Name).Distinct().CountAsync());
        Assert.Equal(0, await dbContext.Users.CountAsync());
        Assert.Equal(0, await dbContext.ScreeningQuestions.CountAsync());
    }

    [Fact]
    public async Task Unauthenticated_learner_dashboard_access_redirects_to_login()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/Dashboard/Learner");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location;
        Assert.NotNull(location);
        Assert.Equal("/Account/Login", location.IsAbsoluteUri ? location.AbsolutePath : location.OriginalString.Split('?')[0]);
        var query = location.IsAbsoluteUri ? location.Query : location.OriginalString;
        Assert.Contains("Dashboard%2FLearner", query, StringComparison.OrdinalIgnoreCase);
    }
}
