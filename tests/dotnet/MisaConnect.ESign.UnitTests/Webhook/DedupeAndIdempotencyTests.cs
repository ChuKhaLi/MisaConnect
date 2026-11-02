using Microsoft.Extensions.Logging.Abstractions;
using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Application.Sessions;
using MisaConnect.ESign.Application.UseCases;
using MisaConnect.ESign.Application.Validation;
using MisaConnect.ESign.Application.Webhook;
using MisaConnect.ESign.Domain.Authentication;
using MisaConnect.ESign.Domain.Certificates;
using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.Domain.Webhook;
using MisaConnect.ESign.Infrastructure.Caching;
using MisaConnect.ESign.Infrastructure.Sessions;
using MisaConnect.ESign.UnitTests.TestSupport;
using Xunit;

namespace MisaConnect.ESign.UnitTests.Webhook;

public class DedupeAndIdempotencyTests
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

    private static SigningSession SampleSession() => new(
        ClientId: "client-1",
        TransactionId: "tx-1",
        Format: DocumentFormat.Pdf,
        HashPayload: new PerFormatHashPayload.Pdf(new PdfHashOutput("doc-1", "AAA", "BBB", "CC", "n", "DD")),
        RecordedDocumentIds: new[] { "doc-1" },
        CreatedAtUtc: DateTimeOffset.UtcNow,
        Ttl: TimeSpan.FromMinutes(15),
        ObservedMessageIds: new HashSet<string>(),
        CachedSuccess: null);

    private static WebhookEnvelope Envelope(string messageId = "m-1") => new(
        MessageId: messageId,
        ClientId: "client-1",
        ExtraData: null,
        Status: WebhookStatus.Success,
        ErrorCode: null,
        TransactionId: "tx-1",
        Signatures: new[] { new WebhookSignature("doc-1", "SIG") });

    private static StubWireClient BuildWire(FakeClock clock, Func<int, byte[]>? attachOverride = null)
    {
        var wire = new StubWireClient();
        wire.OnLogin = (_, _, _) => Task.FromResult(new AuthSession("raw", "rs", "rt", clock.UtcNow.AddHours(1), "user-id", "alice"));
        wire.OnListCerts = (_, _) => Task.FromResult<IReadOnlyList<Certificate>>(new[] { BeginSignFixtures.ActiveCert() });
        wire.OnAttach = (_, _, _, _, _) =>
        {
            var idx = wire.AttachCalls;
            return Task.FromResult(attachOverride is null ? new byte[] { 0xA0, 0xA1 } : attachOverride(idx));
        };
        return wire;
    }

    [Fact]
    public async Task Five_identical_deliveries_call_attachment_exactly_once_and_hook_once()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var store = new InMemorySigningSessionStore(clock);
        await store.RegisterAsync(SampleSession(), CancellationToken.None);
        var wire = BuildWire(clock);
        var hook = new RecordingHook();
        var sut = BuildSut(wire, store, hook);

        for (int i = 0; i < 5; i++)
        {
            var result = await sut.HandleAsync(Envelope("m-" + i), CancellationToken.None);
            Assert.Equal("0", result.Ack.ErrorCode);
        }

        Assert.Equal(1, wire.AttachCalls);
        Assert.Single(hook.Outcomes);
    }

    [Fact]
    public async Task Eight_parallel_deliveries_call_attachment_exactly_once_under_single_flight()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var store = new InMemorySigningSessionStore(clock);
        await store.RegisterAsync(SampleSession(), CancellationToken.None);
        var wire = BuildWire(clock);
        var hook = new RecordingHook();
        var sut = BuildSut(wire, store, hook);

        var tasks = Enumerable.Range(0, 8)
            .Select(i => sut.HandleAsync(Envelope("m-" + i), CancellationToken.None))
            .ToArray();
        await Task.WhenAll(tasks);

        Assert.All(tasks, t => Assert.Equal("0", t.Result.Ack.ErrorCode));
        Assert.Equal(1, wire.AttachCalls);
        Assert.Single(hook.Outcomes);
    }

    [Fact]
    public async Task Failure_then_success_calls_attachment_twice_and_hook_only_on_success()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var store = new InMemorySigningSessionStore(clock);
        await store.RegisterAsync(SampleSession(), CancellationToken.None);
        var correlation = new StubCorrelationIdAccessor("cid");
        var wire = new StubWireClient();
        wire.OnLogin = (_, _, _) => Task.FromResult(new AuthSession("raw", "rs", "rt", clock.UtcNow.AddHours(1), "user-id", "alice"));
        wire.OnListCerts = (_, _) => Task.FromResult<IReadOnlyList<Certificate>>(new[] { BeginSignFixtures.ActiveCert() });
        wire.OnAttach = (_, _, _, _, _) =>
        {
            if (wire.AttachCalls == 1)
            {
                throw new ESignGeneralException(ESignErrorCategory.AttachmentRejected, "FakeFail", "first attempt fails", correlation.Current);
            }
            return Task.FromResult(new byte[] { 0xB0 });
        };
        var hook = new RecordingHook();
        var sut = BuildSut(wire, store, hook);

        var first = await sut.HandleAsync(Envelope("m-1"), CancellationToken.None);
        Assert.Equal("webhook.finalize_failed", first.Ack.ErrorCode);
        Assert.Empty(hook.Outcomes);

        var second = await sut.HandleAsync(Envelope("m-2"), CancellationToken.None);
        Assert.Equal("0", second.Ack.ErrorCode);
        Assert.Equal(2, wire.AttachCalls);
        Assert.Single(hook.Outcomes);
    }

    [Fact]
    public async Task Cached_success_short_circuits_when_messageId_is_brand_new()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var store = new InMemorySigningSessionStore(clock);
        var session = SampleSession();
        await store.RegisterAsync(session, CancellationToken.None);
        var ack = WebhookAck.Success("0");
        await store.CacheSuccessAsync(session.ClientId, session.TransactionId, "m-first", ack, new byte[] { 0xC0 }, CancellationToken.None);

        var wire = BuildWire(clock);
        var hook = new RecordingHook();
        var sut = BuildSut(wire, store, hook);

        var result = await sut.HandleAsync(Envelope("m-NEVER-SEEN"), CancellationToken.None);

        Assert.Equal("0", result.Ack.ErrorCode);
        Assert.Equal(0, wire.AttachCalls);
        Assert.Empty(hook.Outcomes);
    }

    [Fact]
    public async Task FR_081a_session_evicted_past_ttl_returns_unknown_transaction()
    {
        var now = DateTimeOffset.UtcNow;
        var clock = new FakeClock(now);
        var store = new InMemorySigningSessionStore(clock);
        await store.RegisterAsync(SampleSession() with { Ttl = TimeSpan.FromMinutes(1) }, CancellationToken.None);

        clock.Advance(TimeSpan.FromMinutes(2));

        var wire = BuildWire(clock);
        var hook = new RecordingHook();
        var sut = BuildSut(wire, store, hook);

        var result = await sut.HandleAsync(Envelope(), CancellationToken.None);

        Assert.Equal("webhook.unknown_transaction", result.Ack.ErrorCode);
        Assert.Equal(0, wire.AttachCalls);
    }

    private static HandleWebhook BuildSut(StubWireClient wire, InMemorySigningSessionStore store, IWebhookDeliveryHook hook)
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var cache = new InMemoryTokenCache();
        var keySelector = new BeginSignFixtures.StaticKeySelector();
        var correlation = new StubCorrelationIdAccessor("cid");
        var ensureToken = new EnsureAccessToken(wire, cache, keySelector, clock, () => ("alice", "pass"));
        var listCerts = new ListActiveCertificates(wire, correlation);
        var attachPdf = new AttachSignature(wire);
        var attachXml = new AttachSignatureToXml(wire, correlation);
        var attachWordExcel = new AttachSignatureToWordExcel(wire, correlation);
        var finalize = new FinalizeFromWebhook(
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
        return new HandleWebhook(
            validator: new WebhookEnvelopeValidator(),
            sessionStore: store,
            finalizeFromWebhook: finalize,
            deliveryHook: hook,
            correlationAccessor: correlation,
            configuredClientIdAccessor: () => "client-1",
            successAckCodeAccessor: () => "0",
            logger: NullLogger<HandleWebhook>.Instance);
    }
}
