using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Application.UseCases;
using MisaConnect.ESign.Application.Validation;
using MisaConnect.ESign.Domain.Authentication;
using MisaConnect.ESign.Domain.Certificates;
using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.Domain.Signing;
using MisaConnect.ESign.Infrastructure.Caching;
using MisaConnect.ESign.UnitTests.TestSupport;
using Xunit;

namespace MisaConnect.ESign.UnitTests.Authentication;

public class OtpProviderTransparentFlowTests
{
    private sealed class StaticKeySelector : ITokenCacheKeySelector
    {
        public string Compose() => "k";
    }

    private sealed class FirstCertSelector : ICertificateSelector
    {
        public Task<Certificate> SelectAsync(IReadOnlyList<Certificate> certs, CancellationToken ct) =>
            Task.FromResult(certs[0]);
    }

    private sealed class FakeOtpProvider : IOtpProvider
    {
        public int ProvideCalls;
        public int ResendCalls;
        public OtpChallenge? LastChallenge;
        public Task<OtpSubmission> ProvideAsync(OtpChallenge challenge, CancellationToken ct)
        {
            ProvideCalls++;
            LastChallenge = challenge;
            return Task.FromResult(new OtpSubmission("OTP-1", OtpDeliveryChannel.SmsOrEmail, true));
        }
        public Task<OtpResendResult> RequestResendAsync(OtpChallenge challenge, string? language, CancellationToken ct)
        {
            ResendCalls++;
            return Task.FromResult(new OtpResendResult(true, null, null, null, challenge.CorrelationId));
        }
    }

    private static Certificate ActiveCert() => new(
        UserId: "user-id",
        KeyAlias: "key-alias-1",
        AppName: "misa",
        KeyStatus: KeyStatus.ACTIVE,
        CertStatus: "ACTIVE",
        CertificateValue: "BASE64CERT",
        CertificateChain: new CertificateChain("SIGN", "INTERMEDIATE", "ROOT"),
        EffectiveDate: DateTimeOffset.UtcNow.AddYears(-1),
        ExpirationDate: DateTimeOffset.UtcNow.AddYears(1),
        EmailName: "alice@example.com",
        IsAutoSign: false);

    [Fact]
    public async Task SignPdf_with_provider_catches_122_invokes_provider_runs_exchange_and_recurses_once()
    {
        var loginCalls = 0;
        var twoFactorCalls = 0;
        var attachCalls = 0;
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var correlation = new StubCorrelationIdAccessor("cid-flow");
        var cache = new InMemoryTokenCache();
        var selector = new StaticKeySelector();

        var wire = new StubWireClient
        {
            OnLogin = (u, _, _) =>
            {
                loginCalls++;
                throw new AuthenticationFailedException(
                    rawCode: "122",
                    detail: "2FA required",
                    correlationId: "cid-flow",
                    requires2FA: true,
                    username: u);
            },
            OnTwoFactorAuth = (_, _, _, _, _) =>
            {
                twoFactorCalls++;
                return Task.FromResult(new AuthSession(
                    AccessToken: "raw",
                    RemoteSigningAccessToken: "rs",
                    RefreshToken: "rt",
                    ExpiresAtUtc: clock.UtcNow.AddMinutes(60),
                    UserId: "user-id",
                    Username: "alice"));
            },
            OnListCerts = (_, _) => Task.FromResult<IReadOnlyList<Certificate>>(new[] { ActiveCert() }),
            OnHash = (_, _, _, _, _, _) => Task.FromResult(new PdfHashOutput(
                DocumentId: "doc-1",
                DocumentBytes: "DOCBYTES",
                DocumentHash: "DOCHASH",
                Sh: "SH",
                SignatureName: "SigName",
                Digest: "DIGEST")),
            OnSubmitSignHash = (_, _, _, _, _, _, _) => Task.FromResult(new SignTransaction("tx-1", clock.UtcNow)),
            OnGetStatus = (_, _, _) => Task.FromResult(new SignStatusSnapshot(
                Status: SignStatus.SUCCESS,
                ErrorCode: null,
                ErrorDescription: null,
                TransactionId: "tx-1",
                FirstSignatureData: "SIG-BYTES")),
            OnAttach = (_, _, _, _, _) =>
            {
                attachCalls++;
                return Task.FromResult(new byte[] { 0x25, 0x50, 0x44, 0x46 });
            },
        };

        var ensureToken = new EnsureAccessToken(
            wire: wire,
            cache: cache,
            keySelector: selector,
            clock: clock,
            credentialsAccessor: () => ("alice", "pass"));
        var listCerts = new ListActiveCertificates(wire, correlation);
        var hashPdf = new HashPdfDocument(wire, correlation);
        var submitSignHash = new SubmitSignHash(wire);
        var pollStatus = new PollSignStatus(wire, new FakeDelayer(), clock, correlation);
        var attach = new AttachSignature(wire);
        var validator = new SignPdfRequestValidator(correlation);
        var otpValidator = new OtpSubmissionValidator(correlation);
        var provider = new FakeOtpProvider();
        var exchange = new ExchangeOtp(
            wire: wire,
            cache: cache,
            keySelector: selector,
            singleFlight: (key, factory, ct) => factory(ct));

        var sut = new SignPdf(
            ensureToken: ensureToken,
            listCerts: listCerts,
            certSelector: new FirstCertSelector(),
            hashPdf: hashPdf,
            submitSignHash: submitSignHash,
            pollStatus: pollStatus,
            attachSignature: attach,
            clock: clock,
            validator: validator,
            intervalAccessor: () => TimeSpan.FromMilliseconds(1),
            totalTimeoutAccessor: () => TimeSpan.FromSeconds(1),
            otpProvider: provider,
            exchangeOtp: exchange,
            otpSubmissionValidator: otpValidator,
            correlation: correlation);

        var request = new SignPdfWorkRequest(
            Pdf: new PdfDocument(new byte[] { 0x25, 0x50 }),
            SignatureInfo: new SignatureInfo(
                SignatureName: "sig",
                HashAlgorithm: HashAlgorithm.SHA256,
                LogoImage: "logo",
                SignatureDescription: new SignatureDescription(
                    SignedBy: "Alice",
                    Location: "Hanoi",
                    Reason: "Test",
                    Contact: "alice@example.com"),
                RenderingMode: 1),
            DocumentName: "doc",
            DocumentId: "doc-1",
            DataToBeDisplayed: "to-display");

        var result = await sut.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal(1, loginCalls);
        Assert.Equal(1, twoFactorCalls);
        Assert.Equal(1, attachCalls);
        Assert.Equal(1, provider.ProvideCalls);
        Assert.NotNull(provider.LastChallenge);
        Assert.Equal("alice", provider.LastChallenge!.UserName);
        Assert.Equal("cid-flow", provider.LastChallenge.CorrelationId);
        Assert.NotNull(result.SignedPdf);
    }
}
