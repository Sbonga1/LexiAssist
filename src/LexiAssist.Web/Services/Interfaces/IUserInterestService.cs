namespace LexiAssist.Web.Services.Interfaces;

public interface IUserInterestService
{
    Task<IReadOnlyList<string>> GetSelectedKeysAsync(
        string userId,
        CancellationToken cancellationToken = default);

    Task SaveSelectedKeysAsync(
        string userId,
        IEnumerable<string> interestKeys,
        CancellationToken cancellationToken = default);
}
