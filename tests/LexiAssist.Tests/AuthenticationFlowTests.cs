using System.Net;
using System.Text.RegularExpressions;
using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.TestHost;

namespace LexiAssist.Tests;

public sealed class AuthenticationFlowTests
{
    [Fact]
    public async Task Login_redirects_by_role_and_logout_removes_dashboard_access()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var roles = new (string Role, string Dashboard)[]
        {
            (ApplicationRoles.Learner, "/Dashboard/Learner"),
            (ApplicationRoles.Administrator, "/Dashboard/Administrator"),
            (ApplicationRoles.Psychologist, "/Dashboard/Psychologist")
        };

        foreach (var (role, dashboard) in roles)
        {
            var (email, password) = await factory.CreateUserAsync(role);

            var loginPage = await client.GetAsync("/Account/Login");
            Assert.Equal(HttpStatusCode.OK, loginPage.StatusCode);
            var loginPageHtml = await loginPage.Content.ReadAsStringAsync();
            Assert.Contains("Welcome back", loginPageHtml);

            var antiForgeryToken = ExtractAntiForgeryToken(loginPageHtml);
            var rejectedWithoutToken = await client.PostAsync(
                "/Account/Login",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["Email"] = email,
                    ["Password"] = password
                }));
            Assert.Equal(HttpStatusCode.BadRequest, rejectedWithoutToken.StatusCode);

            var loginResult = await client.PostAsync(
                "/Account/Login",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] = antiForgeryToken,
                    ["Email"] = email,
                    ["Password"] = password,
                    ["RememberMe"] = "true"
                }));

            Assert.Equal(HttpStatusCode.Redirect, loginResult.StatusCode);
            Assert.Equal(dashboard, loginResult.Headers.Location?.OriginalString);

            var dashboardResult = await client.GetAsync(dashboard);
            Assert.Equal(HttpStatusCode.OK, dashboardResult.StatusCode);
            var dashboardHtml = await dashboardResult.Content.ReadAsStringAsync();
            var authenticatedAntiForgeryToken = ExtractAntiForgeryToken(dashboardHtml);

            if (role == ApplicationRoles.Learner)
            {
                var learnerNavigation = new[]
                {
                    "href=\"/Dashboard/Learner\"",
                    "href=\"/screening\"",
                    "href=\"/activities\"",
                    "href=\"/Dashboard/Learner#progress\"",
                    "href=\"/Dashboard/Learner#resources\"",
                    "href=\"/Settings/Accessibility\""
                };
                foreach (var link in learnerNavigation)
                {
                    Assert.Contains(link, dashboardHtml);
                }

                Assert.Contains("Start or continue screening", dashboardHtml);
                Assert.Contains("Most recent screening result", dashboardHtml);
                Assert.Contains("Assigned learning activities", dashboardHtml);
                Assert.Contains("Recent progress", dashboardHtml);
                Assert.Contains("Educational resources", dashboardHtml);
                Assert.Contains("Daily practice reminder", dashboardHtml);
                Assert.Contains("data-reading-background=\"standard-light\"", dashboardHtml);
            }
            else
            {
                Assert.DoesNotContain("href=\"/screening\"", dashboardHtml);
                var forbiddenDashboard = await client.GetAsync("/Dashboard/Learner");
                Assert.Equal(HttpStatusCode.Redirect, forbiddenDashboard.StatusCode);
                Assert.StartsWith("/Account/AccessDenied", GetLocationPath(forbiddenDashboard));
            }

            var logoutResult = await client.PostAsync(
                "/Account/Logout",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] = authenticatedAntiForgeryToken
                }));

            Assert.Equal(HttpStatusCode.Redirect, logoutResult.StatusCode);
            Assert.Equal("/Account/Login", logoutResult.Headers.Location?.OriginalString);

            var protectedAfterLogout = await client.GetAsync(dashboard);
            Assert.Equal(HttpStatusCode.Redirect, protectedAfterLogout.StatusCode);
            Assert.StartsWith("/Account/Login", GetLocationPath(protectedAfterLogout));
        }
    }

    private static string ExtractAntiForgeryToken(string html)
    {
        var match = Regex.Match(
            html,
            "<input[^>]*name=\\\"__RequestVerificationToken\\\"[^>]*value=\\\"([^\\\"]+)\\\"",
            RegexOptions.IgnoreCase);

        Assert.True(match.Success, "The rendered form should include an antiforgery token.");
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

internal sealed class LexiAssistWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString = CreateTestConnectionString();

    public Action<IServiceCollection>? ConfigureAdditionalServices { get; set; }

    private static string CreateTestConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable("LEXIASSIST_TEST_SQLSERVER")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=LexiAssistTests;Trusted_Connection=True;TrustServerCertificate=True";
        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = $"LexiAssistTests_{Guid.NewGuid():N}"
        };
        return builder.ConnectionString;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DevelopmentAdminSeed:Enabled"] = "false"
            }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
            services.RemoveAll<ApplicationDbContext>();
            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(_connectionString));
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            ConfigureAdditionalServices?.Invoke(services);
        });
    }

    public async Task<(string Email, string Password)> CreateUserAsync(string role)
    {
        using var scope = Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var email = $"{Guid.NewGuid():N}@example.test";
        var password = $"Lexi!Assist{Guid.NewGuid():N}9";
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            IsActive = true
        };

        var createResult = await userManager.CreateAsync(user, password);
        Assert.True(createResult.Succeeded, string.Join("; ", createResult.Errors.Select(error => error.Description)));

        var roleResult = await userManager.AddToRoleAsync(user, role);
        Assert.True(roleResult.Succeeded, string.Join("; ", roleResult.Errors.Select(error => error.Description)));

        return (email, password);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            using var scope = Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureDeleted();
        }

        base.Dispose(disposing);
    }
}
