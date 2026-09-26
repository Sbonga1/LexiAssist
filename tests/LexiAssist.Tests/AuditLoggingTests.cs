using System.Net;
using System.Text.RegularExpressions;
using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Audit;
using LexiAssist.Web.Models.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LexiAssist.Tests;

public sealed class AuditLoggingTests
{
    [Fact]
    public async Task Administrative_user_creation_records_actor_role_and_safe_metadata()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        var (adminEmail, adminPassword) = await factory.CreateUserAsync(ApplicationRoles.Administrator);
        var (adminId, _) = await FindUserAsync(factory, adminEmail);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var loginPage = await client.GetAsync("/Account/Login");
        var token = ExtractAntiForgeryToken(await loginPage.Content.ReadAsStringAsync());
        var login = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Email"] = adminEmail,
            ["Password"] = adminPassword
        }));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        var createPage = await client.GetAsync("/admin/users/create");
        var createToken = ExtractAntiForgeryToken(await createPage.Content.ReadAsStringAsync());
        var create = await client.PostAsync("/admin/users/create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = createToken,
            ["FirstName"] = "Audit",
            ["LastName"] = "Learner",
            ["Email"] = "audit-learner@example.test",
            ["Password"] = "ValidPassword1",
            ["ConfirmPassword"] = "ValidPassword1",
            ["Role"] = "Learner"
        }));
        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var events = db.AuditEvents.Where(item => item.ActorUserId == adminId).OrderBy(item => item.Id).ToList();
        Assert.Contains(events, item => item.Action == AuditActions.UserCreated && item.EntityType == AuditEntityTypes.User && item.Succeeded);
        var roleEvent = Assert.Single(events, item => item.Action == AuditActions.RoleChanged);
        Assert.Equal(AuditEntityTypes.User, roleEvent.EntityType);
        Assert.Contains("Learner", roleEvent.DetailsJson);
        Assert.DoesNotContain("Password", roleEvent.DetailsJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Audit_service_persists_failed_action_without_sensitive_payload()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<LexiAssist.Web.Services.Interfaces.IAuditService>();

        await service.RecordAsync(AuditActions.AdministrativeActionFailed, AuditEntityTypes.User, "target", false,
            new { Operation = "Deactivate", Reason = "CurrentAdministrator", Password = "should-not-be-included" });

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var audit = Assert.Single(db.AuditEvents.Where(item => item.EntityId == "target"));
        Assert.False(audit.Succeeded);
        Assert.Contains("Deactivate", audit.DetailsJson);
        Assert.DoesNotContain("should-not-be-included", audit.DetailsJson);
    }

    private static async Task<(string Id, string Password)> FindUserAsync(LexiAssistWebApplicationFactory factory, string email)
    {
        using var scope = factory.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await manager.FindByEmailAsync(email);
        Assert.NotNull(user);
        return (user!.Id, "");
    }

    private static string ExtractAntiForgeryToken(string html)
    {
        var match = Regex.Match(html, "name=\\\"__RequestVerificationToken\\\" type=\\\"hidden\\\" value=\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase);
        Assert.True(match.Success, "The page did not contain an antiforgery token.");
        return match.Groups[1].Value;
    }
}
