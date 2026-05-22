using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Application.UseCases;
using MisaConnect.ESign.Application.Validation;
using MisaConnect.ESign.Client;
using MisaConnect.ESign.Domain.Authentication;
using MisaConnect.ESign.Infrastructure.Caching;
using MisaConnect.ESign.UnitTests.TestSupport;
using Xunit;

namespace MisaConnect.ESign.UnitTests.Authentication;

public class MisaESignClientUsernameContextTests
{
    private sealed class StaticKeySelector : ITokenCacheKeySelector
    {
        public string Compose() => "k";
    }

    private static MisaESignClient BuildClient(StubWireClient wire, InMemoryTokenCache cache)
    {
        var selector = new StaticKeySelector();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var correlation = new StubCorrelationIdAccessor("cid");
        var ensure = new EnsureAccessToken(wire, cache, selector, clock, () => ("u", "p"));
        var validator = new SignPdfRequestValidator(correlation);
        var signPdf = new SignPdf(
            ensureToken: ensure,
            listCerts: null!,
            certSelector: null!,
            hashPdf: null!,
            submitSignHash: null!,
            pollStatus: null!,
            attachSignature: null!,
            clock: clock,
            validator: validator,
            intervalAccessor: () => TimeSpan.Zero,
            totalTimeoutAccessor: () => TimeSpan.Zero);
        var exchange = new ExchangeOtp(wire, cache, selector, (k, f, ct) => f(ct));
        var resend = new ResendOtp(wire, () => "en-US");
        var otpValidator = new OtpSubmissionValidator(correlation);
        return new MisaESignClient(signPdf, signXml: null!, signWord: null!, signExcel: null!, exchange, resend, otpValidator);
    }

    [Fact]
    public async Task SignInWithOtpAsync_throws_when_no_pending_challenge_captured()
    {
        MisaESignClient.ClearPendingUserNameForTests();
        var wire = new StubWireClient();
        var cache = new InMemoryTokenCache();
        var client = BuildClient(wire, cache);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.SignInWithOtpAsync("123456", OtpDeliveryChannel.SmsOrEmail, false, CancellationToken.None));
    }

    [Fact]
    public async Task ResendOtpAsync_throws_when_no_pending_challenge_captured()
    {
        MisaESignClient.ClearPendingUserNameForTests();
        var wire = new StubWireClient();
        var cache = new InMemoryTokenCache();
        var client = BuildClient(wire, cache);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.ResendOtpAsync(language: null, ct: CancellationToken.None));
    }
}
