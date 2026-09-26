using LexiAssist.Web.Models.Notifications;

namespace LexiAssist.Web.Services.Interfaces;

public interface IAdministratorAlertService
{
    Task<IReadOnlyList<AdministratorAlert>> GetForAdministratorAsync(
        string administratorId,
        CancellationToken cancellationToken = default);
}

public sealed record AdministratorAlert(
    int? Id,
    AdministratorAlertKind Kind,
    string Title,
    string Message,
    DateTime CreatedAtUtc,
    DateTime? ReadAtUtc,
    string? RelatedUrl,
    bool IsGenerated);
