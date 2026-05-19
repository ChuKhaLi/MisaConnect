namespace MisaConnect.ESign.Domain.Authentication;

public sealed record AuthSession(
    string AccessToken,
    string RemoteSigningAccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAtUtc,
    string UserId,
    string Username);
