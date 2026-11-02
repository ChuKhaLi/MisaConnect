using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Application.UseCases;
using MisaConnect.ESign.Application.Validation;
using MisaConnect.ESign.Domain.Authentication;
using MisaConnect.ESign.Domain.Certificates;
using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.Domain.Signing;
using MisaConnect.ESign.Infrastructure.Caching;
using MisaConnect.ESign.UnitTests.TestSupport;
using Xunit;

namespace MisaConnect.ESign.UnitTests.Signing.Word;

public class SignWordOrchestratorTests
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
    public async Task End_to_end_word_pipeline_returns_signed_bytes_with_word_format()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var correlation = new StubCorrelationIdAccessor("cid-w");
        var cache = new InMemoryTokenCache();
        var keySelector = new StaticKeySelector();
        DocumentFormat? attachedFormat = null;

        var wire = new StubWireClient
        {
            OnLogin = (_, _, _) => Task.FromResult(new AuthSession("raw", "rs", "rt", clock.UtcNow.AddMinutes(60), "user-id", "alice")),
            OnListCerts = (_, _) => Task.FromResult<IReadOnlyList<Certificate>>(new[] { Cert() }),
            OnHashWord = (_, _, _, _, _, _) => Task.FromResult(new WordExcelHashOutput("doc-1", "DOCB", "sig-w", "DIGEST", "MAIN")),
            OnSubmitSignHash = (_, _, _, _, _, _, _) => Task.FromResult(new SignTransaction("tx-1", clock.UtcNow)),
            OnGetStatus = (_, _, _) => Task.FromResult(new SignStatusSnapshot(SignStatus.SUCCESS, null, null, "tx-1", "SIG")),
            OnAttachWordExcel = (_, _, _, _, fmt, _) =>
            {
                attachedFormat = fmt;
                return Task.FromResult(new byte[] { 0x50, 0x4B });
            },
        };

        var ensure = new EnsureAccessToken(wire, cache, keySelector, clock, () => ("alice", "pw"));
        var listCerts = new ListActiveCertificates(wire, correlation);
        var hashWord = new HashWordDocument(wire, correlation);
        var submit = new SubmitSignHash(wire);
        var poll = new PollSignStatus(wire, new FakeDelayer(), clock, correlation);
        var attach = new AttachSignatureToWordExcel(wire, correlation);
        var validator = new SignWordRequestValidator(correlation);

        var sut = new SignWord(ensure, listCerts, new FirstCertSelector(), hashWord, submit, poll, attach, clock, validator,
            intervalAccessor: () => TimeSpan.FromMilliseconds(1),
            totalTimeoutAccessor: () => TimeSpan.FromSeconds(1),
            correlation: correlation);

        var request = new SignWordWorkRequest(
            Word: new byte[] { 0x50, 0x4B, 0x03, 0x04 },
            SignatureInfo: new SignatureInfo("sig", HashAlgorithm.SHA256, "logo", new SignatureDescription("a", "h", "t", "c"), 1),
            DocumentId: "doc-1",
            DocumentName: "doc.docx",
            DataToBeDisplayed: "data");

        var result = await sut.ExecuteAsync(request, CancellationToken.None);
        Assert.Equal(DocumentFormat.Word, result.Format);
        Assert.Equal(DocumentFormat.Word, attachedFormat);
        Assert.Equal(1, wire.HashWordCalls);
        Assert.Equal(0, wire.HashExcelCalls);
        Assert.Equal(0, wire.HashCalls);
        Assert.Equal(1, wire.AttachWordExcelCalls);
    }
}
