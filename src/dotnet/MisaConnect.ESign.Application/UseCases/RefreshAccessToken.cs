using MisaConnect.ESign.Application.Abstractions;

namespace MisaConnect.ESign.Application.UseCases;

public sealed class RefreshAccessToken
{
    private readonly IMisaESignWireClient _wire;

    public RefreshAccessToken(IMisaESignWireClient wire)
    {
        _wire = wire;
    }

    public async Task<AccessToken> ExecuteAsync(string refreshToken, string previousUserId, string previousUsername, CancellationToken ct)
    {
        var session = await _wire.RefreshAsync(refreshToken, ct).ConfigureAwait(false);
        return new AccessToken(
            Value: session.RemoteSigningAccessToken,
            RawAccessToken: session.AccessToken,
            RefreshToken: string.IsNullOrEmpty(session.RefreshToken) ? refreshToken : session.RefreshToken,
            ExpiresAtUtc: session.ExpiresAtUtc,
            UserId: string.IsNullOrEmpty(session.UserId) ? previousUserId : session.UserId,
            Username: string.IsNullOrEmpty(session.Username) ? previousUsername : session.Username);
    }
}
