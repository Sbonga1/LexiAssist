namespace LexiAssist.Web.Services.Interfaces;

public interface IAuditService
{
    Task RecordAsync(string action, string entityType, string? entityId, bool succeeded, object? details = null, CancellationToken cancellationToken = default);
}
