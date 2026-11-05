using Microsoft.Extensions.Logging.Abstractions;
using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Application.Sessions;
using MisaConnect.ESign.Application.UseCases;
using MisaConnect.ESign.Application.Webhook;
using MisaConnect.ESign.Domain.Authentication;
using MisaConnect.ESign.Domain.Certificates;
using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.Domain.Signing;
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
        FakeClock clock,
        IWebhookCredentialsResolver? dynamicResolver = null,
        BeginSignFixtures.RecordingUse? use = null) =>
        BeginSignFixtures.BuildFinalize(wire, store, store, hook, clock, dynamicResolver, use);

    private static WebhookEnvelope Envelope(SigningSession session, WebhookStatus status = WebhookStatus.Success, string signature = "ENVELOPE-SIG") => new(
        MessageId: "m-1",
        ClientId: session.ClientId,
        ExtraData: null,
        Status: status,
        ErrorCode: null,
        TransactionId: session.TransactionId,
        Signatures: status == WebhookStatus.Success ? new[] { new WebhookSignature("doc-1", signature) } : Array.Empty<WebhookSignature>());

    private static (InMemorySigningSessionStore Store, StubWireClient Wire, RecordingHook Hook, FakeClock Clock) Arrange()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var store = new InMemorySigningSessionStore(clock);
        var wire = new StubWireClient();
        wire.OnAttach = (_, _, _, _, _) => Task.FromResult(new byte[] { 0xA0 });
        return (store, wire, new RecordingHook(), clock);
    }

    [Fact]
    public async Task Attaches_MISAs_signature_not_the_envelopes()
    {
        var (store, wire, hook, clock) = Arrange();
        string? attached = null;
        wire.OnAttach = (_, _, _, sig, _) =>
        {
            attached = sig;
            return Task.FromResult(new byte[] { 0xA0 });
        };
        var session = BuildSessionForFormat(DocumentFormat.Pdf);
        await store.RegisterAsync(session, CancellationToken.None);

        var result = await BuildSut(wire, store, hook, clock).RunAsync(session, Envelope(session, signature: "forged"), "cid", CancellationToken.None);

        Assert.Equal("0", result.Ack.ErrorCode);
        Assert.Equal("MISA-SIG", attached);
        Assert.Equal(1, wire.GetStatusCalls);
    }

    [Theory]
    [InlineData(SignStatus.PENDING)]
    [InlineData(SignStatus.UNKNOWN)]
    public async Task Non_final_MISA_status_is_finalize_failed(SignStatus status)
    {
        var (store, wire, hook, clock) = Arrange();
        wire.OnGetStatus = (_, _, _) => Task.FromResult(new SignStatusSnapshot(status, null, null, "tx-1", null));
        var session = BuildSessionForFormat(DocumentFormat.Pdf);
        await store.RegisterAsync(session, CancellationToken.None);

        var result = await BuildSut(wire, store, hook, clock).RunAsync(session, Envelope(session), "cid", CancellationToken.None);

        Assert.Equal("webhook.finalize_failed", result.Ack.ErrorCode);
        Assert.Equal("Finalize attempt failed; MISA may retry.", result.Ack.DevMsg);
        Assert.Equal("Signing finalization failed; please wait for retry.", result.Ack.UserMsg);
        var failure = Assert.IsType<WebhookOutcome.FailureWithError>(result.Outcome);
        Assert.Equal("SignNotFinal", failure.MisaErrorCode);
        Assert.Equal(WebhookValidationCategory.None, failure.Category);
        Assert.Empty(hook.Outcomes);
        Assert.Equal(0, wire.AttachCalls);
    }

    [Fact]
    public async Task Success_without_MISA_signature_is_MissingSignature()
    {
        var (store, wire, hook, clock) = Arrange();
        wire.OnGetStatus = (_, _, _) => Task.FromResult(BeginSignFixtures.StatusSuccess(""));
        var session = BuildSessionForFormat(DocumentFormat.Pdf);
        await store.RegisterAsync(session, CancellationToken.None);

        var result = await BuildSut(wire, store, hook, clock).RunAsync(session, Envelope(session), "cid", CancellationToken.None);

        Assert.Equal("MissingSignature", Assert.IsType<WebhookOutcome.FailureWithError>(result.Outcome).MisaErrorCode);
        Assert.Equal(0, wire.AttachCalls);
    }

    [Theory]
    [InlineData(SignStatus.FAILED, WebhookStatus.Failed)]
    [InlineData(SignStatus.CANCELLED, WebhookStatus.Cancelled)]
    public async Task MISA_terminal_is_recorded_once_and_delivered_with_MISAs_status(SignStatus misa, WebhookStatus expected)
    {
        var (store, wire, hook, clock) = Arrange();
        wire.OnGetStatus = (_, _, _) => Task.FromResult(new SignStatusSnapshot(misa, "MISA-E", null, "tx-1", null));
        var session = BuildSessionForFormat(DocumentFormat.Pdf);
        await store.RegisterAsync(session, CancellationToken.None);
        var sut = BuildSut(wire, store, hook, clock);

        // The envelope says SUCCESS; MISA's confirmed terminal state wins.
        var first = await sut.RunAsync(session, Envelope(session), "cid", CancellationToken.None);
        var second = await sut.RunAsync(session, Envelope(session, WebhookStatus.Failed), "cid", CancellationToken.None);

        Assert.Equal("0", first.Ack.ErrorCode);
        Assert.Equal(first.Ack, second.Ack);
        var terminal = Assert.IsType<WebhookOutcome.TerminalWithoutFinalize>(second.Outcome);
        Assert.Equal(expected, terminal.Status);
        Assert.Equal("MISA-E", terminal.MisaErrorCode);
        Assert.Single(hook.Outcomes);
        Assert.Equal(1, wire.GetStatusCalls);
        Assert.Equal(0, wire.AttachCalls);
    }

    [Fact]
    public async Task Two_factor_challenge_is_finalize_failed_without_caching()
    {
        var (store, wire, hook, clock) = Arrange();
        wire.OnLogin = (_, _, _) => throw new AuthenticationFailedException("122", "2FA required", "cid", requires2FA: true, username: "alice");
        var session = BuildSessionForFormat(DocumentFormat.Pdf);
        await store.RegisterAsync(session, CancellationToken.None);

        var result = await BuildSut(wire, store, hook, clock).RunAsync(session, Envelope(session), "cid", CancellationToken.None);

        Assert.Equal("webhook.finalize_failed", result.Ack.ErrorCode);
        Assert.Empty(hook.Outcomes);
        var refreshed = await store.TryGetByTransactionIdAsync(session.ClientId, session.TransactionId, CancellationToken.None);
        Assert.Null(refreshed!.CachedSuccess);
        Assert.Null(refreshed.CachedTerminal);
    }

    [Fact]
    public async Task Dynamic_session_without_user_name_is_MissingPrincipal()
    {
        var (store, wire, hook, clock) = Arrange();
        var session = BuildSessionForFormat(DocumentFormat.Pdf);
        await store.RegisterAsync(session, CancellationToken.None);
        var resolver = new BeginSignFixtures.StubResolver((_, _) => throw new InvalidOperationException("must not be called"));

        var result = await BuildSut(wire, store, hook, clock, resolver).RunAsync(session, Envelope(session), "cid", CancellationToken.None);

        Assert.Equal("MissingPrincipal", Assert.IsType<WebhookOutcome.FailureWithError>(result.Outcome).MisaErrorCode);
        Assert.Equal(0, wire.GetStatusCalls);
    }

    [Theory]
    [InlineData(null, "NoWebhookCredentials")]
    [InlineData("other-user", "CredentialsMismatch")]
    public async Task Dynamic_unresolved_or_foreign_principal_fails(string? resolvedUser, string rawCode)
    {
        var (store, wire, hook, clock) = Arrange();
        var session = BuildSessionForFormat(DocumentFormat.Pdf) with { UserName = "alice" };
        await store.RegisterAsync(session, CancellationToken.None);
        var resolver = new BeginSignFixtures.StubResolver((cid, _) =>
            resolvedUser is null ? null : new MisaCredentials(cid, "k", resolvedUser, "p"));

        var result = await BuildSut(wire, store, hook, clock, resolver).RunAsync(session, Envelope(session), "cid", CancellationToken.None);

        Assert.Equal("webhook.finalize_failed", result.Ack.ErrorCode);
        Assert.Equal(rawCode, Assert.IsType<WebhookOutcome.FailureWithError>(result.Outcome).MisaErrorCode);
        Assert.Equal(0, wire.GetStatusCalls);
        Assert.Empty(hook.Outcomes);
    }

    [Fact]
    public async Task Dynamic_runs_MISA_calls_under_the_resolved_principal()
    {
        var (store, wire, hook, clock) = Arrange();
        var session = BuildSessionForFormat(DocumentFormat.Pdf) with { UserName = "alice" };
        await store.RegisterAsync(session, CancellationToken.None);
        var creds = new MisaCredentials(session.ClientId, "k", "alice", "p");
        var use = new BeginSignFixtures.RecordingUse();

        var result = await BuildSut(wire, store, hook, clock, new BeginSignFixtures.StubResolver((_, _) => creds), use)
            .RunAsync(session, Envelope(session), "cid", CancellationToken.None);

        Assert.Equal("0", result.Ack.ErrorCode);
        Assert.Equal(creds, Assert.Single(use.Used));
    }
}
