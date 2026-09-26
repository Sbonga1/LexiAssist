using System.Net;
using System.Text.RegularExpressions;
using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Audit;
using LexiAssist.Web.Models.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LexiAssist.Tests;

public sealed class AdminAuditViewerTests
{
    [Fact]
    public async Task Non_administrator_cannot_view_audit_log()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (email, password) = await factory.CreateUserAsync(ApplicationRoles.Learner);
        await SignInAsync(client, email, password);

        var response = await client.GetAsync("/admin/audit");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location;
        Assert.NotNull(location);
        Assert.StartsWith("/Account/AccessDenied", location!.IsAbsoluteUri ? location.PathAndQuery : location.OriginalString);
    }

    [Fact]
    public async Task Administrator_can_filter_and_view_safe_audit_metadata()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (email, password) = await factory.CreateUserAsync(ApplicationRoles.Administrator);
        await SignInAsync(client, email, password);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.AuditEvents.Add(new AuditEvent
            {
                OccurredAtUtc = DateTime.UtcNow,
                Action = AuditActions.ReferralAssigned,
                EntityType = AuditEntityTypes.Referral,
                EntityId = "42",
                Succeeded = true,
                DetailsJson = "{\"PsychologistId\":\"safe-id\"}"
            });
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync("/admin/audit?action=Referral.Assigned&entityType=Referral&succeeded=true");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Audit log", html);
        Assert.Contains("Referral.Assigned", html);
        Assert.Contains("PsychologistId", html);
        Assert.DoesNotContain("PasswordHash", html);
        Assert.DoesNotContain("Authentication token", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Administrator_audit_log_uses_five_row_pages_and_normalizes_page_numbers()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (email, password) = await factory.CreateUserAsync(ApplicationRoles.Administrator);
        await SignInAsync(client, email, password);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            for (var index = 0; index < 12; index++)
            {
                db.AuditEvents.Add(new AuditEvent
                {
                    OccurredAtUtc = DateTime.UtcNow.AddMinutes(-index),
                    Action = "Paging.Event",
                    EntityType = "Audit",
                    EntityId = index.ToString(),
                    Succeeded = true,
                    DetailsJson = "{\"source\":\"paging-test\"}"
                });
            }

            await db.SaveChangesAsync();
        }

        var firstPage = await client.GetAsync("/admin/audit?action=Paging.Event&page=1");
        var firstPageHtml = await firstPage.Content.ReadAsStringAsync();
        var lastPage = await client.GetAsync("/admin/audit?action=Paging.Event&page=999");
        var lastPageHtml = await lastPage.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, firstPage.StatusCode);
        Assert.Equal(5, Regex.Matches(firstPageHtml, "admin-audit-event-row").Count);
        Assert.Contains("Showing 1–5 of 12 audit events", firstPageHtml);
        Assert.Contains("aria-current=\"page\">1</span>", firstPageHtml);
        Assert.Contains("page=2", firstPageHtml);

        Assert.Equal(HttpStatusCode.OK, lastPage.StatusCode);
        Assert.Equal(2, Regex.Matches(lastPageHtml, "admin-audit-event-row").Count);
        Assert.Contains("Showing 11–12 of 12 audit events", lastPageHtml);
        Assert.Contains("aria-current=\"page\">3</span>", lastPageHtml);
        Assert.Contains("aria-disabled=\"true\">Next</span>", lastPageHtml);
    }

    [Fact]
    public async Task Audit_log_pager_preserves_action_entity_and_outcome_filters()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (email, password) = await factory.CreateUserAsync(ApplicationRoles.Administrator);
        await SignInAsync(client, email, password);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            for (var index = 0; index < 6; index++)
            {
                db.AuditEvents.Add(new AuditEvent
                {
                    OccurredAtUtc = DateTime.UtcNow.AddMinutes(-index),
                    Action = "Filter.Event",
                    EntityType = "User",
                    EntityId = index.ToString(),
                    Succeeded = true,
                    DetailsJson = "{\"source\":\"filter-test\"}"
                });
            }

            db.AuditEvents.Add(new AuditEvent
            {
                OccurredAtUtc = DateTime.UtcNow.AddHours(-1),
                Action = "Filter.Event",
                EntityType = "User",
                EntityId = "failed",
                Succeeded = false,
                DetailsJson = "{\"source\":\"filter-test\"}"
            });
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync("/admin/audit?action=Filter.Event&entityType=User&succeeded=true&page=2");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(Regex.Matches(html, "admin-audit-event-row"));
        Assert.Contains("Showing 6–6 of 6 audit events", html);
        Assert.Contains("value=\"Filter.Event\"", html);
        Assert.Contains("value=\"User\"", html);
        Assert.Contains("action=Filter.Event", html);
        Assert.Contains("entityType=User", html);
        Assert.Contains("succeeded=True", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("page=1", html);
    }

    private static async Task SignInAsync(HttpClient client, string email, string password)
    {
        var login = await client.GetAsync("/Account/Login");
        var html = await login.Content.ReadAsStringAsync();
        var match = Regex.Match(html, "name=\\\"__RequestVerificationToken\\\"[^>]*value=\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase);
        Assert.True(match.Success);
        var result = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = match.Groups[1].Value,
            ["Email"] = email,
            ["Password"] = password,
            ["RememberMe"] = "false"
        }));
        Assert.Equal(HttpStatusCode.Redirect, result.StatusCode);
    }
}
