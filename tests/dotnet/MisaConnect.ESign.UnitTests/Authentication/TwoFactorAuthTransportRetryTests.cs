using System.Net;
using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Application.Errors;
using MisaConnect.ESign.Application.UseCases;
using MisaConnect.ESign.Domain.Authentication;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.Infrastructure.Caching;
using MisaConnect.ESign.UnitTests.TestSupport;
using Xunit;

namespace MisaConnect.ESign.UnitTests.Authentication;

public class TwoFactorAuthTransportRetryTests
{
    private sealed class StaticKeySelector : ITokenCacheKeySelector
    {
        public string Compose() => "k";
    }

    private static ExchangeOtp BuildExchange(StubWireClient wire, InMemoryTokenCache cache) =>
        new(
            wire: wire,
            cache: cache,
            keySelector: new StaticKeySelector(),
            singleFlight: (key, factory, ct) => factory(ct));

    [Fact]
    public async Task Five_xx_response_propagates_transport_exception_and_does_not_write_cache()
    {
        var wire = new StubWireClient
        {
            OnTwoFactorAuth = (_, _, _, _, _) =>
                Task.FromException<AuthSession>(new ESignTransportException(
                    lastStatusCode: HttpStatusCode.BadGateway,
                    attemptCount: 3,
                    detail: "5xx",
                    correlationId: "cid")),
        };
        var cache = new InMemoryTokenCache();
        var sut = BuildExchange(wire, cache);

        await Assert.ThrowsAsync<ESignTransportException>(() =>
            sut.ExecuteAsync(
                "alice",
                new OtpSubmission("123456", OtpDeliveryChannel.SmsOrEmail, false),
                CancellationToken.None));

        var cached = await cache.TryGetAsync("k", CancellationToken.None);
        Assert.Null(cached);
    }

    [Fact]
    public void Mapper_recognizes_429_as_transport()
    {
        var ex = OtpErrorMapper.MapTwoFactor(
            statusCode: 429,
            envelope: null,
            correlationId: "cid",
            attemptCount: 4,
            lastStatusCode: (HttpStatusCode)429);

        Assert.IsType<ESignTransportException>(ex);
    }

    [Fact]
    public void Mapper_401_surfaces_as_typed_otp_rejection_not_refresh()
    {
        var envelope = new ResponseError(true, "401", DevMsg: null, UserMsg: null);
        var ex = OtpErrorMapper.MapTwoFactor(401, envelope, "cid");
        Assert.IsType<OtpRejectedException>(ex);
    }
}
