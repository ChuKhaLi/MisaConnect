namespace MisaConnect.ESign.Application.Abstractions;

public sealed record AccessToken(
    string Value,
    string RawAccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAtUtc,
    string UserId,
    string Username);
