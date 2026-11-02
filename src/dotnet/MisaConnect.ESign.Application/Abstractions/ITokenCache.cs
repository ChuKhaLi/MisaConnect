namespace MisaConnect.ESign.Application.Abstractions;

public interface ITokenCache
{
    Task<AccessToken?> TryGetAsync(string key, CancellationToken ct);
    Task SetAsync(string key, AccessToken value, CancellationToken ct);
    Task RemoveAsync(string key, CancellationToken ct);
}
