using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Application.Sessions;
using MisaConnect.ESign.Application.Webhook;
using MisaConnect.ESign.Client;
using MisaConnect.ESign.Client.DependencyInjection;
using MisaConnect.ESign.Client.Dtos.Webhook;
using MisaConnect.ESign.Domain.Webhook;
using MisaConnect.ESign.Infrastructure.Configuration;
using MisaConnect.ESign.Infrastructure.Sessions;
using MisaConnect.ESign.IntegrationTests.EsignFake;
using Xunit;

namespace MisaConnect.ESign.IntegrationTests.EndToEnd;

/// <summary>
/// FR-011/012/014/015/016: the webhook body is only a trigger. The SDK asks MISA's
/// <c>Signing/status</c> for the confirmed state, attaches MISA's own signature, stores a
/// terminal state once, never prompts for an OTP during finalize, and finalizes as the
/// principal that began the transaction (Dynamic mode via <see cref="IWebhookCredentialsResolver"/>).
/// </summary>
public class WebhookConfirmationFakeServerTests
{
    private const string Failed122 = "{\"status\":{\"code\":200,\"error\":true,\"errorCode\":\"122\"}}";

    private sealed class RecordingHook : IWebhookDeliveryHook
    {
        private readonly object _lock = new();
        private readonly List<WebhookOutcome> _outcomes = new();
        public int ThrowsRemaining { get; set; }

        public IReadOnlyList<WebhookOutcome> Outcomes
        {
            get { lock (_lock) { return _outcomes.ToArray(); } }
        }

        public Task DeliverAsync(WebhookOutcome outcome, CancellationToken ct)
        {
            lock (_lock)
            {
                _outcomes.Add(outcome);
                if (ThrowsRemaining > 0)
                {
                    ThrowsRemaining--;
                    throw new InvalidOperationException("hook boom");
                }
            }
            return Task.CompletedTask;
        }
    }

    private static readonly AsyncLocal<MisaCredentials?> Ambient = new();

    private sealed class AmbientCredentialsAccessor : IMisaCredentialsAccessor
    {
        public MisaCredentials Get() =>
            Ambient.Value ?? throw new InvalidOperationException("No MISA credentials set for the current call.");
    }

    private sealed class FixedResolver : IWebhookCredentialsResolver
    {
        private readonly Func<string, string, MisaCredentials?> _resolve;
        public FixedResolver(Func<string, string, MisaCredentials?> resolve) => _resolve = resolve;
        public ValueTask<MisaCredentials?> ResolveAsync(string clientId, string userName, CancellationToken ct) =>
            ValueTask.FromResult(_resolve(clientId, userName));
    }

    /// <summary>A pre-2.5 integrator store: delegates the four original members and
    /// does not override <see cref="ISigningSessionStore.CacheTerminalAsync"/>.</summary>
    private sealed class LegacyStore : ISigningSessionStore
    {
        private readonly ISigningSessionStore _inner;
        public LegacyStore(ISigningSessionStore inner) => _inner = inner;
        public ValueTask RegisterAsync(SigningSession session, CancellationToken ct) => _inner.RegisterAsync(session, ct);
        public ValueTask<SigningSession?> TryGetByTransactionIdAsync(string clientId, string transactionId, CancellationToken ct) =>
            _inner.TryGetByTransactionIdAsync(clientId, transactionId, ct);
        public ValueTask RecordObservedMessageIdAsync(string clientId, string transactionId, string messageId, CancellationToken ct) =>
            _inner.RecordObservedMessageIdAsync(clientId, transactionId, messageId, ct);
        public ValueTask CacheSuccessAsync(string clientId, string transactionId, string triggeringMessageId, WebhookAck ack, byte[] signedBytes, CancellationToken ct) =>
            _inner.CacheSuccessAsync(clientId, transactionId, triggeringMessageId, ack, signedBytes, ct);
    }

    private static IDisposable Use(MisaCredentials creds)
    {
        var prev = Ambient.Value;
        Ambient.Value = creds;
        return new Pop(prev);
    }

    private sealed class Pop : IDisposable
    {
        private readonly MisaCredentials? _prev;
        public Pop(MisaCredentials? prev) => _prev = prev;
        public void Dispose() => Ambient.Value = _prev;
    }

    private static readonly MisaCredentials UserA = new("client-A", "ckey-A", "userA", "pw-A");
    private static readonly MisaCredentials UserB = new("client-B", "ckey-B", "userB", "pw-B");

    private static ServiceProvider BuildStatic(string baseUrl, IWebhookDeliveryHook hook, bool legacyStore = false)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.None));
        if (legacyStore)
        {
            services.AddSingleton<ISigningSessionStore>(sp => new LegacyStore(sp.GetRequiredService<InMemorySigningSessionStore>()));
        }
        services.AddMisaConnectESign(o =>
        {
            o.Environment = ESignEnvironment.Sandbox;
            o.BaseUrl = baseUrl;
            o.ClientId = "client-id";
            o.ClientKey = "client-key";
            o.UserName = "alice";
            o.Password = "password";
            o.Polling.Interval = TimeSpan.FromMilliseconds(10);
            o.Polling.TotalTimeout = TimeSpan.FromSeconds(5);
            o.TransportRetry.MaxAttempts = 1;
        });
        services.AddSingleton(hook);
        return services.BuildServiceProvider();
    }

    private static ServiceProvider BuildDynamic(string baseUrl, IWebhookDeliveryHook hook, IWebhookCredentialsResolver? resolver)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.None));
        services.AddSingleton<IMisaCredentialsAccessor, AmbientCredentialsAccessor>();
        if (resolver is not null) services.AddSingleton(resolver);
        services.AddMisaConnectESign(o =>
        {
            o.Environment = ESignEnvironment.Sandbox;
            o.BaseUrl = baseUrl;
            o.CredentialsMode = CredentialsMode.Dynamic;
            o.Polling.Interval = TimeSpan.FromMilliseconds(10);
            o.Polling.TotalTimeout = TimeSpan.FromSeconds(5);
            o.TransportRetry.MaxAttempts = 1;
        });
        services.AddSingleton(hook);
        return services.BuildServiceProvider();
    }

    private static WebhookEnvelopeDto Envelope(string transactionId, string status = "SUCCESS", string clientId = "client-id", string signature = "SIGNATURE-BYTES", string? errorCode = null) =>
        new(
            MessageId: Guid.NewGuid().ToString("N"),
            ClientId: clientId,
            ExtraData: null,
            Status: status,
            ErrorCode: errorCode,
            TransactionId: transactionId,
            Signatures: status == "SUCCESS"
                ? new List<WebhookSignatureDto> { new("doc-1", signature) }
                : new List<WebhookSignatureDto>());

    private static async Task<(IMisaESignClient Client, string TransactionId)> BeginAsync(AsyncServiceScope scope)
    {
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();
        var begin = await client.BeginSignPdfAsync(TestPdfFixture.SampleRequest() with { DocumentId = "doc-1" }, CancellationToken.None);
        return (client, begin.TransactionId);
    }

    [Fact]
    public async Task Forged_success_while_pending_is_not_delivered()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        server.Configure.StatusAlwaysPending = true;
        var hook = new RecordingHook();
        await using var sp = BuildStatic(server.BaseUrl, hook);
        await using var scope = sp.CreateAsyncScope();
        var (client, tx) = await BeginAsync(scope);

        var result = await client.HandleWebhookAsync(Envelope(tx), CancellationToken.None);

        Assert.Equal("webhook.finalize_failed", result.Ack.ErrorCode);
        var failure = Assert.IsType<WebhookOutcomeDto.FailureWithError>(result.Outcome);
        Assert.Equal("SignNotFinal", failure.MisaErrorCode);
        Assert.Empty(hook.Outcomes);
        Assert.Equal(0, server.Calls.Attachment);
    }

    [Fact]
    public async Task Attach_uses_MISAs_signature_not_the_envelopes()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        server.Configure.PendingsBeforeSuccess = 0;
        var hook = new RecordingHook();
        await using var sp = BuildStatic(server.BaseUrl, hook);
        await using var scope = sp.CreateAsyncScope();
        var (client, tx) = await BeginAsync(scope);

        var result = await client.HandleWebhookAsync(Envelope(tx, signature: "forged"), CancellationToken.None);

        Assert.Equal("0", result.Ack.ErrorCode);
        var attach = Assert.Single(server.AttachmentRequests);
        Assert.Contains("misa-signature", attach.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("forged", attach.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Failed_webhook_while_MISA_reports_success_is_signed()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        server.Configure.PendingsBeforeSuccess = 0;
        var hook = new RecordingHook();
        await using var sp = BuildStatic(server.BaseUrl, hook);
        await using var scope = sp.CreateAsyncScope();
        var (client, tx) = await BeginAsync(scope);

        var result = await client.HandleWebhookAsync(Envelope(tx, status: "FAILED", errorCode: "E"), CancellationToken.None);

        Assert.Equal("0", result.Ack.ErrorCode);
        Assert.IsType<WebhookOutcomeDto.SuccessWithSignedBytes>(result.Outcome);
        var delivered = Assert.Single(hook.Outcomes);
        var success = Assert.IsType<WebhookOutcome.SuccessWithSignedBytes>(delivered);
        Assert.Equal(server.SignedPdfBytes, success.SignedBytes);
        Assert.Equal(1, server.Calls.Attachment);
    }

    [Fact]
    public async Task Terminal_is_delivered_once()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        server.Configure.StatusAlwaysFailed = true;
        var hook = new RecordingHook();
        await using var sp = BuildStatic(server.BaseUrl, hook);
        await using var scope = sp.CreateAsyncScope();
        var (client, tx) = await BeginAsync(scope);
        var envelope = Envelope(tx, status: "FAILED", errorCode: "E");

        var first = await client.HandleWebhookAsync(envelope, CancellationToken.None);
        var second = await client.HandleWebhookAsync(envelope, CancellationToken.None);

        Assert.Equal("0", first.Ack.ErrorCode);
        Assert.Equal(first.Ack, second.Ack);
        var terminal = Assert.IsType<WebhookOutcomeDto.TerminalWithoutFinalize>(second.Outcome);
        Assert.Equal(WebhookStatusDto.Failed, terminal.Status);
        Assert.Equal("ResultBad", terminal.MisaErrorCode);
        Assert.Single(hook.Outcomes);
        Assert.Equal(1, server.Calls.SignStatus);
        Assert.Equal(0, server.Calls.Attachment);
    }

    [Fact]
    public async Task Success_after_recorded_failure_is_not_delivered()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        server.Configure.StatusAlwaysFailed = true;
        var hook = new RecordingHook();
        await using var sp = BuildStatic(server.BaseUrl, hook);
        await using var scope = sp.CreateAsyncScope();
        var (client, tx) = await BeginAsync(scope);

        await client.HandleWebhookAsync(Envelope(tx, status: "FAILED"), CancellationToken.None);
        server.Configure.StatusAlwaysFailed = false;
        server.Configure.PendingsBeforeSuccess = 0;
        var result = await client.HandleWebhookAsync(Envelope(tx, status: "SUCCESS"), CancellationToken.None);

        Assert.Equal("0", result.Ack.ErrorCode);
        Assert.IsType<WebhookOutcomeDto.TerminalWithoutFinalize>(result.Outcome);
        Assert.Single(hook.Outcomes);
        Assert.Equal(0, server.Calls.Attachment);
    }

    [Fact]
    public async Task Two_factor_during_finalize_is_a_failure_ack()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        server.Configure.PendingsBeforeSuccess = 0;
        var hook = new RecordingHook();
        await using var sp = BuildStatic(server.BaseUrl, hook);
        await using var scope = sp.CreateAsyncScope();
        var (client, tx) = await BeginAsync(scope);

        server.Configure.RevokeInitialBearerAfterCertificates = true;
        server.Configure.Intercept = req => req.Route switch
        {
            "refresh" => new FakeResponse(500, "{}"),
            "login" => new FakeResponse(200, Failed122),
            _ => null,
        };

        var result = await client.HandleWebhookAsync(Envelope(tx), CancellationToken.None);

        Assert.Equal("webhook.finalize_failed", result.Ack.ErrorCode);
        Assert.IsType<WebhookOutcomeDto.FailureWithError>(result.Outcome);
        Assert.Empty(hook.Outcomes);
        Assert.Equal(0, server.Calls.TwoFactorAuth);
        Assert.Equal(0, server.Calls.Attachment);
    }

    [Fact]
    public async Task Dynamic_webhook_uses_resolved_principal()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        server.Configure.PendingsBeforeSuccess = 0;
        var hook = new RecordingHook();
        var resolver = new FixedResolver((cid, user) => cid == "client-A" && user == "userA" ? UserA : null);
        await using var sp = BuildDynamic(server.BaseUrl, hook, resolver);
        await using var scope = sp.CreateAsyncScope();
        string tx;
        IMisaESignClient client;
        using (Use(UserA))
        {
            (client, tx) = await BeginAsync(scope);
        }

        // Force a fresh login during finalize so the principal used is observable on the wire.
        server.Configure.RevokeInitialBearerAfterCertificates = true;
        server.Configure.Intercept = req => req.Route == "refresh" ? new FakeResponse(500, "{}") : null;

        var result = await client.HandleWebhookAsync(Envelope(tx, clientId: "client-A"), CancellationToken.None);

        Assert.Equal("0", result.Ack.ErrorCode);
        Assert.IsType<WebhookOutcomeDto.SuccessWithSignedBytes>(result.Outcome);
        Assert.Single(hook.Outcomes);
        Assert.Equal(2, server.LoginRequests.Count);
        var last = server.LoginRequests[^1];
        Assert.Equal("client-A", last.ClientId);
        Assert.Contains("\"userA\"", last.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dynamic_webhook_without_resolver_fails_cleanly()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        server.Configure.PendingsBeforeSuccess = 0;
        var hook = new RecordingHook();
        await using var sp = BuildDynamic(server.BaseUrl, hook, resolver: null);
        await using var scope = sp.CreateAsyncScope();
        string tx;
        IMisaESignClient client;
        using (Use(UserA))
        {
            (client, tx) = await BeginAsync(scope);
        }

        var result = await client.HandleWebhookAsync(Envelope(tx, clientId: "client-A"), CancellationToken.None);

        Assert.Equal("webhook.finalize_failed", result.Ack.ErrorCode);
        var failure = Assert.IsType<WebhookOutcomeDto.FailureWithError>(result.Outcome);
        Assert.Equal("NoWebhookCredentials", failure.MisaErrorCode);
        Assert.Empty(hook.Outcomes);
        Assert.Equal(0, server.Calls.Attachment);
    }

    [Fact]
    public async Task Dynamic_webhook_with_other_principal_is_CredentialsMismatch()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        server.Configure.PendingsBeforeSuccess = 0;
        var hook = new RecordingHook();
        await using var sp = BuildDynamic(server.BaseUrl, hook, new FixedResolver((_, _) => UserB));
        await using var scope = sp.CreateAsyncScope();
        string tx;
        IMisaESignClient client;
        using (Use(UserA))
        {
            (client, tx) = await BeginAsync(scope);
        }

        var result = await client.HandleWebhookAsync(Envelope(tx, clientId: "client-A"), CancellationToken.None);

        Assert.Equal("webhook.finalize_failed", result.Ack.ErrorCode);
        var failure = Assert.IsType<WebhookOutcomeDto.FailureWithError>(result.Outcome);
        Assert.Equal("CredentialsMismatch", failure.MisaErrorCode);
        Assert.Empty(hook.Outcomes);
        Assert.Equal(0, server.Calls.SignStatus);
    }

    [Fact]
    public async Task Custom_store_without_terminal_cache_still_works()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        server.Configure.PendingsBeforeSuccess = 0;
        var hook = new RecordingHook();
        await using var sp = BuildStatic(server.BaseUrl, hook, legacyStore: true);
        await using var scope = sp.CreateAsyncScope();
        Assert.IsType<LegacyStore>(scope.ServiceProvider.GetRequiredService<ISigningSessionStore>());

        // Success completes (and is cached by the original CacheSuccessAsync member).
        var (client, tx) = await BeginAsync(scope);
        var ok = await client.HandleWebhookAsync(Envelope(tx), CancellationToken.None);
        Assert.Equal("0", ok.Ack.ErrorCode);
        Assert.IsType<WebhookOutcomeDto.SuccessWithSignedBytes>(ok.Outcome);
        await client.HandleWebhookAsync(Envelope(tx), CancellationToken.None);
        Assert.Equal(1, server.Calls.Attachment);

        // FAILED repeats are confirmed and delivered each time (no terminal cache: 2.4 behaviour).
        server.Configure.StatusAlwaysFailed = true;
        var begin2 = await client.BeginSignPdfAsync(TestPdfFixture.SampleRequest() with { DocumentId = "doc-1" }, CancellationToken.None);
        var failedEnvelope = Envelope(begin2.TransactionId, status: "FAILED");
        var r1 = await client.HandleWebhookAsync(failedEnvelope, CancellationToken.None);
        var r2 = await client.HandleWebhookAsync(failedEnvelope, CancellationToken.None);
        Assert.Equal("0", r1.Ack.ErrorCode);
        Assert.Equal("0", r2.Ack.ErrorCode);
        Assert.Equal(3, hook.Outcomes.Count);
        Assert.Equal(2, hook.Outcomes.Count(o => o is WebhookOutcome.TerminalWithoutFinalize));
    }

    [Fact]
    public async Task Hook_exception_on_success_propagates_but_retry_is_cached()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        server.Configure.PendingsBeforeSuccess = 0;
        var hook = new RecordingHook { ThrowsRemaining = 1 };
        await using var sp = BuildStatic(server.BaseUrl, hook);
        await using var scope = sp.CreateAsyncScope();
        var (client, tx) = await BeginAsync(scope);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.HandleWebhookAsync(Envelope(tx), CancellationToken.None));
        Assert.Equal("hook boom", ex.Message);

        var retry = await client.HandleWebhookAsync(Envelope(tx), CancellationToken.None);

        Assert.Equal("0", retry.Ack.ErrorCode);
        Assert.IsType<WebhookOutcomeDto.SuccessWithSignedBytes>(retry.Outcome);
        Assert.Equal(1, server.Calls.Attachment);
    }
}
