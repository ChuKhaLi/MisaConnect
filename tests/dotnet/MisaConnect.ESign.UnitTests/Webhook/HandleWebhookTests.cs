using Microsoft.Extensions.Logging.Abstractions;
using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Application.Sessions;
using MisaConnect.ESign.Application.UseCases;
using MisaConnect.ESign.Application.Validation;
using MisaConnect.ESign.Application.Webhook;
using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.Domain.Webhook;
using MisaConnect.ESign.Infrastructure.Sessions;
using MisaConnect.ESign.UnitTests.TestSupport;
using Xunit;

namespace MisaConnect.ESign.UnitTests.Webhook;

public class HandleWebhookTests
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

    private static SigningSession SampleSession(string clientId = "client-1", string txId = "tx-1")
    {
        var hash = new PdfHashOutput(
            DocumentId: "doc-1", DocumentBytes: "AAA", DocumentHash: "BBB",
            Sh: "CC", SignatureName: "n", Digest: "DD");
        return new SigningSession(
            ClientId: clientId,
            TransactionId: txId,
            Format: DocumentFormat.Pdf,
            HashPayload: new PerFormatHashPayload.Pdf(hash),
            RecordedDocumentIds: new[] { "doc-1" },
            CreatedAtUtc: DateTimeOffset.UtcNow,
            Ttl: TimeSpan.FromMinutes(5),
            ObservedMessageIds: new HashSet<string>(),
            CachedSuccess: null);
    }

    private static WebhookEnvelope BuildEnvelope(
        WebhookStatus status,
        string clientId = "client-1",
        string txId = "tx-1",
        string messageId = "m-1",
        string? errorCode = null,
        params (string docId, string sig)[] sigs) => new(
            MessageId: messageId,
            ClientId: clientId,
            ExtraData: null,
            Status: status,
            ErrorCode: errorCode,
            TransactionId: txId,
            Signatures: sigs.Select(s => new WebhookSignature(s.docId, s.sig)).ToList());

    [Fact]
    public async Task HandleAsync_unknown_transaction_returns_failure_ack_and_records_observed_messageId()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var store = new InMemorySigningSessionStore(clock);
        var hook = new RecordingHook();
        var sut = BuildSut(store, hook);

        var envelope = BuildEnvelope(WebhookStatus.Success, sigs: new[] { ("doc-1", "SIG") });
        var result = await sut.HandleAsync(envelope, CancellationToken.None);

        Assert.Equal("webhook.unknown_transaction", result.Ack.ErrorCode);
        Assert.IsType<WebhookOutcome.FailureWithError>(result.Outcome);
        Assert.Single(hook.Outcomes);
    }

    [Fact]
    public async Task HandleAsync_client_id_mismatch_returns_failure_ack()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var store = new InMemorySigningSessionStore(clock);
        await store.RegisterAsync(SampleSession(), CancellationToken.None);
        var hook = new RecordingHook();
        var sut = BuildSut(store, hook, configuredClientId: "client-1");

        var envelope = BuildEnvelope(WebhookStatus.Success, clientId: "different-client", sigs: new[] { ("doc-1", "SIG") });
        var result = await sut.HandleAsync(envelope, CancellationToken.None);

        Assert.Equal("webhook.client_id_mismatch", result.Ack.ErrorCode);
    }

    [Fact]
    public async Task HandleAsync_terminal_failed_returns_success_ack_with_TerminalWithoutFinalize_outcome()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var store = new InMemorySigningSessionStore(clock);
        await store.RegisterAsync(SampleSession(), CancellationToken.None);
        var hook = new RecordingHook();
        var sut = BuildSut(store, hook);

        var envelope = BuildEnvelope(WebhookStatus.Failed, errorCode: "MisaErr-99");
        var result = await sut.HandleAsync(envelope, CancellationToken.None);

        Assert.Equal("0", result.Ack.ErrorCode);
        var terminal = Assert.IsType<WebhookOutcome.TerminalWithoutFinalize>(result.Outcome);
        Assert.Equal(WebhookStatus.Failed, terminal.Status);
        Assert.Equal("MisaErr-99", terminal.MisaErrorCode);
        Assert.Single(hook.Outcomes);
    }

    [Fact]
    public async Task HandleAsync_terminal_cancelled_returns_success_ack_with_terminal_outcome()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var store = new InMemorySigningSessionStore(clock);
        await store.RegisterAsync(SampleSession(), CancellationToken.None);
        var hook = new RecordingHook();
        var sut = BuildSut(store, hook);

        var envelope = BuildEnvelope(WebhookStatus.Cancelled);
        var result = await sut.HandleAsync(envelope, CancellationToken.None);

        Assert.Equal("0", result.Ack.ErrorCode);
        var terminal = Assert.IsType<WebhookOutcome.TerminalWithoutFinalize>(result.Outcome);
        Assert.Equal(WebhookStatus.Cancelled, terminal.Status);
    }

    [Fact]
    public async Task HandleAsync_success_with_empty_signatures_returns_incomplete_failure_ack()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var store = new InMemorySigningSessionStore(clock);
        await store.RegisterAsync(SampleSession(), CancellationToken.None);
        var hook = new RecordingHook();
        var sut = BuildSut(store, hook);

        var envelope = BuildEnvelope(WebhookStatus.Success);
        var result = await sut.HandleAsync(envelope, CancellationToken.None);

        Assert.Equal("webhook.incomplete_success", result.Ack.ErrorCode);
    }

    [Fact]
    public async Task HandleAsync_success_with_unknown_documentId_returns_document_mismatch_failure()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var store = new InMemorySigningSessionStore(clock);
        await store.RegisterAsync(SampleSession(), CancellationToken.None);
        var hook = new RecordingHook();
        var sut = BuildSut(store, hook);

        var envelope = BuildEnvelope(WebhookStatus.Success, sigs: new[] { ("doc-99", "SIG") });
        var result = await sut.HandleAsync(envelope, CancellationToken.None);

        Assert.Equal("webhook.document_id_mismatch", result.Ack.ErrorCode);
    }

    [Fact]
    public async Task HandleAsync_cached_success_short_circuits_without_invoking_hook_again()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var store = new InMemorySigningSessionStore(clock);
        var session = SampleSession();
        await store.RegisterAsync(session, CancellationToken.None);
        var cachedAck = WebhookAck.Success("0");
        var cachedBytes = new byte[] { 1, 2, 3 };
        await store.CacheSuccessAsync(session.ClientId, session.TransactionId, "m-original", cachedAck, cachedBytes, CancellationToken.None);
        var hook = new RecordingHook();
        var sut = BuildSut(store, hook);

        var envelope = BuildEnvelope(WebhookStatus.Success, messageId: "m-dup", sigs: new[] { ("doc-1", "SIG") });
        var result = await sut.HandleAsync(envelope, CancellationToken.None);

        Assert.Equal("0", result.Ack.ErrorCode);
        var success = Assert.IsType<WebhookOutcome.SuccessWithSignedBytes>(result.Outcome);
        Assert.Equal(cachedBytes, success.SignedBytes);
        Assert.Empty(hook.Outcomes);
    }

    [Fact]
    public async Task Validation_failure_is_delivered_when_option_true()
    {
        var store = new InMemorySigningSessionStore(new FakeClock(DateTimeOffset.UtcNow));
        var hook = new RecordingHook();
        var sut = BuildSut(store, hook, deliverValidationFailures: true);

        var result = await sut.HandleAsync(BuildEnvelope(WebhookStatus.Success, sigs: new[] { ("doc-1", "SIG") }), CancellationToken.None);

        Assert.Equal("webhook.unknown_transaction", result.Ack.ErrorCode);
        Assert.IsType<WebhookOutcome.FailureWithError>(Assert.Single(hook.Outcomes));
    }

    [Fact]
    public async Task Validation_failure_is_not_delivered_when_option_false()
    {
        var store = new InMemorySigningSessionStore(new FakeClock(DateTimeOffset.UtcNow));
        var hook = new RecordingHook();
        var sut = BuildSut(store, hook, deliverValidationFailures: false);

        var result = await sut.HandleAsync(BuildEnvelope(WebhookStatus.Success, sigs: new[] { ("doc-1", "SIG") }), CancellationToken.None);

        Assert.Equal("webhook.unknown_transaction", result.Ack.ErrorCode);
        Assert.IsType<WebhookOutcome.FailureWithError>(result.Outcome);
        Assert.Empty(hook.Outcomes);
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public async Task Malformed_is_delivered_only_when_option_true(bool deliver, int expectedHookCalls)
    {
        var store = new InMemorySigningSessionStore(new FakeClock(DateTimeOffset.UtcNow));
        var hook = new RecordingHook();
        var sut = BuildSut(store, hook, deliverValidationFailures: deliver);

        var result = await sut.HandleMalformedAsync("tx-9", "status", CancellationToken.None);

        Assert.Equal("webhook.malformed", result.Ack.ErrorCode);
        var failure = Assert.IsType<WebhookOutcome.FailureWithError>(result.Outcome);
        Assert.Equal("tx-9", failure.TransactionId);
        Assert.Equal(DocumentFormat.Unknown, failure.Format);
        Assert.Equal(WebhookValidationCategory.MalformedEnvelope, failure.Category);
        Assert.Equal(expectedHookCalls, hook.Outcomes.Count);
    }

    [Fact]
    public async Task Malformed_with_null_transactionId_uses_empty_string()
    {
        var store = new InMemorySigningSessionStore(new FakeClock(DateTimeOffset.UtcNow));
        var sut = BuildSut(store, new RecordingHook());

        var result = await sut.HandleMalformedAsync(null, "transactionId", CancellationToken.None);

        Assert.Equal("", Assert.IsType<WebhookOutcome.FailureWithError>(result.Outcome).TransactionId);
    }

    [Fact]
    public async Task Unexpected_exception_becomes_finalize_failed()
    {
        var inner = new InMemorySigningSessionStore(new FakeClock(DateTimeOffset.UtcNow));
        var hook = new RecordingHook();
        var sut = BuildSut(new ThrowingStore(inner, new InvalidOperationException("boom")), hook, lockOwner: inner);

        var result = await sut.HandleAsync(BuildEnvelope(WebhookStatus.Success, sigs: new[] { ("doc-1", "SIG") }), CancellationToken.None);

        Assert.Equal("webhook.finalize_failed", result.Ack.ErrorCode);
        Assert.Equal("Finalize attempt failed; MISA may retry.", result.Ack.DevMsg);
        Assert.Equal("Signing finalization failed; please wait for retry.", result.Ack.UserMsg);
        var failure = Assert.IsType<WebhookOutcome.FailureWithError>(result.Outcome);
        Assert.Equal("tx-1", failure.TransactionId);
        Assert.Equal(DocumentFormat.Unknown, failure.Format);
        Assert.Empty(hook.Outcomes);
    }

    [Fact]
    public async Task Caller_cancellation_propagates()
    {
        var inner = new InMemorySigningSessionStore(new FakeClock(DateTimeOffset.UtcNow));
        var sut = BuildSut(new ThrowingStore(inner, null), new RecordingHook(), lockOwner: inner);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.HandleAsync(BuildEnvelope(WebhookStatus.Success, sigs: new[] { ("doc-1", "SIG") }), cts.Token));
    }

    private sealed class ThrowingStore : ISigningSessionStore
    {
        private readonly ISigningSessionStore _inner;
        private readonly Exception? _toThrow;

        public ThrowingStore(ISigningSessionStore inner, Exception? toThrow)
        {
            _inner = inner;
            _toThrow = toThrow;
        }

        public ValueTask RegisterAsync(SigningSession session, CancellationToken ct) => _inner.RegisterAsync(session, ct);

        public ValueTask<SigningSession?> TryGetByTransactionIdAsync(string clientId, string transactionId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (_toThrow is not null) throw _toThrow;
            return _inner.TryGetByTransactionIdAsync(clientId, transactionId, ct);
        }

        public ValueTask RecordObservedMessageIdAsync(string clientId, string transactionId, string messageId, CancellationToken ct)
            => _inner.RecordObservedMessageIdAsync(clientId, transactionId, messageId, ct);

        public ValueTask CacheSuccessAsync(string clientId, string transactionId, string triggeringMessageId, WebhookAck ack, byte[] signedBytes, CancellationToken ct)
            => _inner.CacheSuccessAsync(clientId, transactionId, triggeringMessageId, ack, signedBytes, ct);
    }

    private static HandleWebhook BuildSut(
        ISigningSessionStore store,
        IWebhookDeliveryHook hook,
        string configuredClientId = "client-1",
        string successCode = "0",
        bool deliverValidationFailures = true,
        InMemorySigningSessionStore? lockOwner = null)
    {
        var validator = new WebhookEnvelopeValidator();
        // FinalizeFromWebhook is required by HandleWebhook ctor but only invoked when there's no cached success and validation passes;
        // for the failure/short-circuit tests below it's never reached.
        var finalize = new FinalizeFromWebhook(
            ensureToken: null!,
            listCerts: null!,
            certSelector: null!,
            attachPdf: null!,
            attachXml: null!,
            attachWordExcel: null!,
            sessionStore: store,
            lockOwner: (IFinalizeLockOwner)(lockOwner ?? store),
            deliveryHook: hook,
            logger: NullLogger<FinalizeFromWebhook>.Instance,
            successAckCodeAccessor: () => successCode);
        var correlation = new StubCorrelationIdAccessor("cid-test");
        return new HandleWebhook(
            validator: validator,
            sessionStore: store,
            finalizeFromWebhook: finalize,
            deliveryHook: hook,
            correlationAccessor: correlation,
            configuredClientIdAccessor: () => configuredClientId,
            successAckCodeAccessor: () => successCode,
            logger: NullLogger<HandleWebhook>.Instance,
            deliverValidationFailuresAccessor: () => deliverValidationFailures);
    }
}
