using MisaConnect.ESign.Domain.Authentication;
using MisaConnect.ESign.Infrastructure.ESign.Wire;

namespace MisaConnect.ESign.Infrastructure.ESign.Mapping;

internal static class AuthSessionMapper
{
    /// <summary>Lifetime assumed when MISA omits <c>expiresIn</c>.</summary>
    internal const int DefaultExpiresInSeconds = 300;

    /// <summary>Shortest lifetime accepted for a positive <c>expiresIn</c>, so the 30 s
    /// proactive-refresh skew still leaves a usable window.</summary>
    internal const int MinExpiresInSeconds = 60;

    /// <summary>
    /// Missing → <see cref="DefaultExpiresInSeconds"/>; an explicit value of 0 or less is
    /// honoured (already expired, so an expired refresh result still falls back to login);
    /// 1–59 → <see cref="MinExpiresInSeconds"/>; anything else as-is.
    /// </summary>
    internal static int LifetimeSeconds(int? expiresIn) => expiresIn switch
    {
        null => DefaultExpiresInSeconds,
        > 0 and < MinExpiresInSeconds => MinExpiresInSeconds,
        { } explicitValue => explicitValue,
    };

    public static AuthSession FromTwoFactorAuthResponse(LoginResponseDto? dto, DateTimeOffset now) =>
        FromLoginResponse(dto, now);

    public static AuthSession FromLoginResponse(LoginResponseDto? dto, DateTimeOffset now)
    {
        if (dto?.Data is null)
        {
            throw new InvalidOperationException("Login response missing data block.");
        }
        var data = dto.Data;
        var expiresIn = LifetimeSeconds(data.ExpiresIn);
        return new AuthSession(
            AccessToken: data.AccessToken ?? string.Empty,
            RemoteSigningAccessToken: data.RemoteSigningAccessToken ?? string.Empty,
            RefreshToken: data.RefreshToken ?? string.Empty,
            ExpiresAtUtc: now.AddSeconds(expiresIn),
            UserId: data.User?.Id ?? string.Empty,
            Username: data.User?.Username ?? string.Empty);
    }

    /// <summary>
    /// Handles both envelope and flat shapes — per wire-envelopes §E2 the doc shows
    /// flat but the live sandbox may return the envelope. When the envelope is
    /// populated, prefer its <c>data</c> block; otherwise fall back to the
    /// top-level flat fields and preserve any existing identity info from the
    /// previous session (the wire client passes it through).
    /// </summary>
    public static AuthSession FromRefreshResponse(
        RefreshTokenResponseDto? dto,
        DateTimeOffset now,
        string previousUserId,
        string previousUsername)
    {
        if (dto?.Data is not null)
        {
            var data = dto.Data;
            var expiresIn = LifetimeSeconds(data.ExpiresIn);
            return new AuthSession(
                AccessToken: data.AccessToken ?? string.Empty,
                RemoteSigningAccessToken: data.RemoteSigningAccessToken ?? string.Empty,
                RefreshToken: data.RefreshToken ?? string.Empty,
                ExpiresAtUtc: now.AddSeconds(expiresIn),
                UserId: data.User?.Id ?? previousUserId,
                Username: data.User?.Username ?? previousUsername);
        }

        if (dto is not null && (dto.AccessToken is not null || dto.RemoteSigningAccessToken is not null))
        {
            return new AuthSession(
                AccessToken: dto.AccessToken ?? string.Empty,
                RemoteSigningAccessToken: dto.RemoteSigningAccessToken ?? string.Empty,
                RefreshToken: dto.RefreshToken ?? string.Empty,
                ExpiresAtUtc: now.AddSeconds(LifetimeSeconds(dto.ExpiresIn)),
                UserId: previousUserId,
                Username: previousUsername);
        }

        throw new InvalidOperationException("Refresh response missing both envelope and flat shapes.");
    }
}
