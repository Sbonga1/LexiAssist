using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Audit;
using LexiAssist.Web.Services.Interfaces;

namespace LexiAssist.Web.Services;

public sealed class AuditService(ApplicationDbContext dbContext, IHttpContextAccessor httpContextAccessor, TimeProvider timeProvider) : IAuditService
{
    private const int MaxDetailsLength = 4000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task RecordAsync(string action, string entityType, string? entityId, bool succeeded, object? details = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(action) || string.IsNullOrWhiteSpace(entityType))
        {
            throw new ArgumentException("Audit action and entity type are required.");
        }

        var detailsJson = details is null ? null : SanitizeDetails(details);
        if (detailsJson?.Length > MaxDetailsLength)
        {
            detailsJson = "{\"truncated\":true}";
        }

        dbContext.AuditEvents.Add(new AuditEvent
        {
            OccurredAtUtc = timeProvider.GetUtcNow().UtcDateTime,
            ActorUserId = httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier),
            Action = action.Trim(),
            EntityType = entityType.Trim(),
            EntityId = string.IsNullOrWhiteSpace(entityId) ? null : entityId.Trim(),
            Succeeded = succeeded,
            DetailsJson = detailsJson
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string SanitizeDetails(object details)
    {
        var node = JsonNode.Parse(JsonSerializer.Serialize(details, JsonOptions));
        Redact(node);
        return node?.ToJsonString(JsonOptions) ?? "{}";
    }

    private static void Redact(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (var property in obj.ToList())
            {
                var name = property.Key.ToLowerInvariant();
                if (name.Contains("password") || name.Contains("secret") || name.Contains("token") ||
                    name.Contains("answer") || name.Contains("note") || name.Contains("path") || name.Contains("email"))
                {
                    obj[property.Key] = "[REDACTED]";
                }
                else
                {
                    Redact(property.Value);
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                Redact(item);
            }
        }
    }
}
