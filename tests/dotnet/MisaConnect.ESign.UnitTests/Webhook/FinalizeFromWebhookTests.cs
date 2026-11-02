using Microsoft.Extensions.Logging.Abstractions;
using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Application.Sessions;
using MisaConnect.ESign.Application.UseCases;
using MisaConnect.ESign.Application.Webhook;
using MisaConnect.ESign.Domain.Authentication;
using MisaConnect.ESign.Domain.Certificates;
using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.Domain.Webhook;
using MisaConnect.ESign.Infrastructure.Caching;
using MisaConnect.ESign.Infrastructure.Sessions;
using MisaConnect.ESign.UnitTests.TestSupport;
using Xunit;

namespace MisaConnect.ESign.UnitTests.Webhook;

public class FinalizeFromWebhookTests
{
    private sealed class RecordingHook : IWebhookDeliveryHook
    {
        public List<WebhookOutcome> Outcomes { get; } = new();
        public Task DeliverAsync(WebhookOutcome outcome, CancellationToken ct)
        {
            Outcomes.Add(outcome);
            return Task.CompletedTask;
        }
    }

    [Theory]
    [InlineData(DocumentFormat.Pdf)]
    [InlineData(DocumentFormat.Xml)]
    [InlineData(DocumentFormat.Word)]
    [InlineData(DocumentFormat.Excel)]
    public async Task RunAsync_dispatches_to_correct_attach_use_case_per_format(DocumentFormat format)
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var store = new InMemorySigningSessionStore(clock);
        var wire = new StubWireClient();
        wire.OnAttach = (_, _, _, _, _) => Task.FromResult(new byte[] { 0xA0 });
        wire.OnAttachXml = (_, _, _, _, _) => Task.FromResult(new byte[] { 0xA1 });
        wire.OnAttachWordExcel = (_, _, _, _, _, _) => Task.FromResult(new byte[] { 0xA2 });

        var session = BuildSessionForFormat(format);
        await store.RegisterAsync(session, CancellationToken.None);

        var hook = new RecordingHook();
        var sut = BuildSut(wire, store, hook, clock);

        var envelope = new WebhookEnvelope(
            MessageId: "m-1",
            ClientId: session.ClientId,
            ExtraData: null,
            Status: WebhookStatus.Success,
            ErrorCode: null,
            TransactionId: session.TransactionId,
            Signatures: new[] { new WebhookSignature("doc-1", "SIG-BYTES") });

        var result = await sut.RunAsync(session, envelope, "cid-test", CancellationToken.None);

        Assert.Equal("0", result.Ack.ErrorCode);
        var outcome = Assert.IsType<WebhookOutcome.SuccessWithSignedBytes>(result.Outcome);
        Assert.Equal(format, outcome.Format);
        Assert.Single(hook.Outcomes);
        switch (format)
        {
            case DocumentFormat.Pdf:
                Assert.Equal(1, wire.AttachCalls);
                Assert.Equal(0, wire.AttachXmlCalls);
                Assert.Equal(0, wire.AttachWordExcelCalls);
                break;
            case DocumentFormat.Xml:
                Assert.Equal(1, wire.AttachXmlCalls);
                Assert.Equal(0, wire.AttachCalls);
                Assert.Equal(0, wire.AttachWordExcelCalls);
                break;
            case DocumentFormat.Word:
            case DocumentFormat.Excel:
                Assert.Equal(1, wire.AttachWordExcelCalls);
                Assert.Equal(0, wire.AttachCalls);
                Assert.Equal(0, wire.AttachXmlCalls);
                break;
        }
    }

    private static SigningSession BuildSessionForFormat(DocumentFormat format)
    {
        PerFormatHashPayload payload = format switch
        {
            DocumentFormat.Pdf => new PerFormatHashPayload.Pdf(new PdfHashOutput("doc-1", "DOCBYTES", "DOCHASH", "SH", "n", "DIGEST")),
            DocumentFormat.Xml => new PerFormatHashPayload.Xml(new XmlHashOutput("doc-1", "<doc/>", "sig-x", "DIGEST", "SH")),
            DocumentFormat.Word => new PerFormatHashPayload.Word(new WordExcelHashOutput("doc-1", "WORDBYTES", "sig-w", "DIGEST", "WORDMAIN")),
            DocumentFormat.Excel => new PerFormatHashPayload.Excel(new WordExcelHashOutput("doc-1", "EXCELBYTES", "sig-e", "DIGEST", "EXCELMAIN")),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };
        return new SigningSession(
            ClientId: "client-1",
            TransactionId: "tx-1",
            Format: format,
            HashPayload: payload,
            RecordedDocumentIds: new[] { "doc-1" },
            CreatedAtUtc: DateTimeOffset.UtcNow,
            Ttl: TimeSpan.FromMinutes(15),
            ObservedMessageIds: new HashSet<string>(),
            CachedSuccess: null);
    }

    private static FinalizeFromWebhook BuildSut(
        StubWireClient wire,
        InMemorySigningSessionStore store,
        IWebhookDeliveryHook hook,
        FakeClock clock)
    {
        wire.OnLogin = (_, _, _) => Task.FromResult(new AuthSession("raw", "rs", "rt", clock.UtcNow.AddHours(1), "user-id", "alice"));
        wire.OnListCerts = (_, _) => Task.FromResult<IReadOnlyList<Certificate>>(new[] { BeginSignFixtures.ActiveCert() });

        var cache = new InMemoryTokenCache();
        var keySelector = new BeginSignFixtures.StaticKeySelector();
        var correlation = new StubCorrelationIdAccessor("cid-test");
        var ensureToken = new EnsureAccessToken(wire, cache, keySelector, clock, () => ("alice", "pass"));
        var listCerts = new ListActiveCertificates(wire, correlation);
        var attachPdf = new AttachSignature(wire);
        var attachXml = new AttachSignatureToXml(wire, correlation);
        var attachWordExcel = new AttachSignatureToWordExcel(wire, correlation);
        return new FinalizeFromWebhook(
            ensureToken: ensureToken,
            listCerts: listCerts,
            certSelector: new BeginSignFixtures.FirstCertSelector(),
            attachPdf: attachPdf,
            attachXml: attachXml,
            attachWordExcel: attachWordExcel,
            sessionStore: store,
            lockOwner: store,
            deliveryHook: hook,
            logger: NullLogger<FinalizeFromWebhook>.Instance,
            successAckCodeAccessor: () => "0");
    }
}
