using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Application.UseCases;
using MisaConnect.ESign.Application.Validation;
using MisaConnect.ESign.Domain.Authentication;
using MisaConnect.ESign.Domain.Certificates;
using MisaConnect.ESign.Domain.Signing;
using MisaConnect.ESign.Infrastructure.Caching;
using MisaConnect.ESign.UnitTests.TestSupport;
using Xunit;

namespace MisaConnect.ESign.UnitTests.Signing.Xml;

public class SignXmlOrchestratorTests
{
    private sealed class StaticKeySelector : ITokenCacheKeySelector { public string Compose() => "k"; }
    private sealed class FirstCertSelector : ICertificateSelector
    {
        public Task<Certificate> SelectAsync(IReadOnlyList<Certificate> certs, CancellationToken ct) => Task.FromResult(certs[0]);
    }

    private static Certificate Cert() => new(
        UserId: "u",
        KeyAlias: "ka",
        AppName: "misa",
        KeyStatus: KeyStatus.ACTIVE,
        CertStatus: "ACTIVE",
        CertificateValue: "BASE64CERT",
        CertificateChain: new CertificateChain("SIGN", "INT", "ROOT"),
        EffectiveDate: DateTimeOffset.UtcNow.AddYears(-1),
        ExpirationDate: DateTimeOffset.UtcNow.AddYears(1),
        EmailName: "alice@example.com",
        IsAutoSign: false);

    [Fact]
    public async Task End_to_end_pipeline_returns_signed_xml_bytes()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var correlation = new StubCorrelationIdAccessor("cid-x");
        var cache = new InMemoryTokenCache();
        var keySelector = new StaticKeySelector();

        var wire = new StubWireClient
        {
            OnLogin = (_, _, _) => Task.FromResult(new AuthSession(
                AccessToken: "raw",
                RemoteSigningAccessToken: "rs",
                RefreshToken: "rt",
                ExpiresAtUtc: clock.UtcNow.AddMinutes(60),
                UserId: "user-id",
                Username: "alice")),
            OnListCerts = (_, _) => Task.FromResult<IReadOnlyList<Certificate>>(new[] { Cert() }),
            OnHashXml = (_, _, _, _, _, _) => Task.FromResult(new XmlHashOutput("doc-1", "<d/>", "sig-x", "DIGEST", "SH")),
            OnSubmitSignHash = (_, _, _, _, _, _, _) => Task.FromResult(new SignTransaction("tx-1", clock.UtcNow)),
            OnGetStatus = (_, _, _) => Task.FromResult(new SignStatusSnapshot(SignStatus.SUCCESS, null, null, "tx-1", "SIG-BYTES")),
            OnAttachXml = (_, _, _, _, _) => Task.FromResult(System.Text.Encoding.UTF8.GetBytes("<signed/>")),
        };

        var ensure = new EnsureAccessToken(wire, cache, keySelector, clock, () => ("alice", "pw"));
        var listCerts = new ListActiveCertificates(wire, correlation);
        var hashXml = new HashXmlDocument(wire, correlation);
        var submit = new SubmitSignHash(wire);
        var poll = new PollSignStatus(wire, new FakeDelayer(), clock, correlation);
        var attach = new AttachSignatureToXml(wire, correlation);
        var validator = new SignXmlRequestValidator(correlation);

        var sut = new SignXml(
            ensure, listCerts, new FirstCertSelector(), hashXml, submit, poll, attach, clock, validator,
            intervalAccessor: () => TimeSpan.FromMilliseconds(1),
            totalTimeoutAccessor: () => TimeSpan.FromSeconds(1),
            correlation: correlation);

        var request = new SignXmlWorkRequest(
            Xml: "<root/>",
            SignatureContext: new XmlSignatureContext("sig", HashAlgorithm.SHA256, new SignatureDescription("a", "h", "t", "c")),
            DocumentId: "doc-1",
            DocumentName: "doc.xml",
            DataToBeDisplayed: "data");

        var result = await sut.ExecuteAsync(request, CancellationToken.None);
        Assert.Equal("<signed/>", System.Text.Encoding.UTF8.GetString(result.SignedXml.Bytes));
        Assert.Equal("tx-1", result.TransactionId);
        Assert.Equal(MisaConnect.ESign.Domain.Documents.DocumentFormat.Xml, result.Format);
        Assert.Equal(1, wire.HashXmlCalls);
        Assert.Equal(0, wire.HashCalls);
        Assert.Equal(0, wire.HashWordCalls);
        Assert.Equal(0, wire.HashExcelCalls);
        Assert.Equal(1, wire.AttachXmlCalls);
    }
}
