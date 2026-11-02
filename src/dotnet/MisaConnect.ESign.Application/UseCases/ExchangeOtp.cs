using MisaConnect.ESign.Application.Abstractions;

namespace MisaConnect.ESign.Application.UseCases;

public sealed class ExchangeOtp
{
    public delegate Task<AccessToken> SingleFlightDelegate(
        string cacheKey,
        Func<CancellationToken, Task<AccessToken>> factory,
        CancellationToken ct);

    private readonly IMisaESignWireClient _wire;
    private readonly ITokenCache _cache;
    private readonly ITokenCacheKeySelector _keySelector;
    private readonly SingleFlightDelegate _singleFlight;

    public ExchangeOtp(
        IMisaESignWireClient wire,
        ITokenCache cache,
        ITokenCacheKeySelector keySelector,
        SingleFlightDelegate singleFlight)
    {
        _wire = wire;
        _cache = cache;
        _keySelector = keySelector;
        _singleFlight = singleFlight;
    }

    public Task<AccessToken> ExecuteAsync(
        string userName,
        OtpSubmission submission,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(submission);

        var cacheKey = _keySelector.Compose();

        return _singleFlight(cacheKey, async innerCt =>
        {
            var session = await _wire.TwoFactorAuthAsync(
                userName,
                submission.Code,
                submission.OtpType,
                submission.Remember,
                innerCt).ConfigureAwait(false);
            var token = EnsureAccessToken.ToAccessToken(session);
            await _cache.SetAsync(cacheKey, token, innerCt).ConfigureAwait(false);
            return token;
        }, ct);
    }
}
