using Microsoft.Extensions.Logging.Abstractions;
using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Application.Sessions;
using MisaConnect.ESign.Application.UseCases;
using MisaConnect.ESign.Application.Validation;
using MisaConnect.ESign.Domain.Authentication;
using MisaConnect.ESign.Domain.Certificates;
using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.Domain.Signing;
using MisaConnect.ESign.Infrastructure.Caching;
using MisaConnect.ESign.UnitTests.TestSupport;

namespace MisaConnect.ESign.UnitTests.Webhook;

internal static class BeginSignFixtures
{
    public sealed class StaticKeySelector : ITokenCacheKeySelector
    {
        public string Compose() => "k";
    }

    public sealed class FirstCertSelector : ICertificateSelector
    {
        public Task<Certificate> SelectAsync(IReadOnlyList<Certificate> certs, CancellationToken ct) =>
            Task.FromResult(certs[0]);
    }

    public sealed class StubSigningSessionStore : ISigningSessionStore
    {
        public int RegisterCalls;
        public int RecordObservedCalls;
        public int CacheSuccessCalls;
        public int TryGetCalls;
        public List<SigningSession> Registered { get; } = new();

        public ValueTask RegisterAsync(SigningSession session, CancellationToken ct)
        {
            RegisterCalls++;
            Registered.Add(session);
            return ValueTask.CompletedTask;
        }

        public ValueTask<SigningSession?> TryGetByTransactionIdAsync(string clientId, string transactionId, CancellationToken ct)
        {
            TryGetCalls++;
            var session = Registered.FirstOrDefault(s => s.ClientId == clientId && s.TransactionId == transactionId);
            return ValueTask.FromResult<SigningSession?>(session);
        }

        public ValueTask RecordObservedMessageIdAsync(string clientId, string transactionId, string messageId, CancellationToken ct)
        {
            RecordObservedCalls++;
            return ValueTask.CompletedTask;
        }

        public ValueTask CacheSuccessAsync(string clientId, string transactionId, string triggeringMessageId, MisaConnect.ESign.Domain.Webhook.WebhookAck ack, byte[] signedBytes, CancellationToken ct)
        {
            CacheSuccessCalls++;
            return ValueTask.CompletedTask;
        }
    }

    public static Certificate ActiveCert() => new(
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

    public static AuthSession ActiveSession(DateTimeOffset now) => new(
        AccessToken: "raw-token",
        RemoteSigningAccessToken: "rs-token",
        RefreshToken: "refresh-token",
        ExpiresAtUtc: now.AddHours(1),
        UserId: "user-id",
        Username: "alice");

    public static SignatureInfo PdfSignatureInfo() => new(
        SignatureName: "sig",
        HashAlgorithm: HashAlgorithm.SHA256,
        LogoImage: "logo",
        SignatureDescription: new SignatureDescription(
            SignedBy: "Alice",
            Location: "Hanoi",
            Reason: "Test",
            Contact: "alice@example.com"),
        RenderingMode: 1);

    public static XmlSignatureContext XmlContext() => new(
        SignatureName: "xml-sig",
        HashAlgorithm: HashAlgorithm.SHA256,
        SignatureDescription: new SignatureDescription(
            SignedBy: "Alice",
            Location: "Hanoi",
            Reason: "Test",
            Contact: "alice@example.com"));

    public static BeginSignPdf BuildBeginSignPdf(StubWireClient wire, StubSigningSessionStore store, FakeClock clock, string clientId = "client-1", TimeSpan? ttl = null)
    {
        var selector = new StaticKeySelector();
        var cache = new InMemoryTokenCache();
        var correlation = new StubCorrelationIdAccessor("cid-test");
        var ensureToken = new EnsureAccessToken(wire, cache, selector, clock, () => ("alice", "pass"));
        var listCerts = new ListActiveCertificates(wire, correlation);
        var hashPdf = new HashPdfDocument(wire, correlation);
        var submitSignHash = new SubmitSignHash(wire);
        var validator = new SignPdfRequestValidator(correlation);
        return new BeginSignPdf(
            ensureToken: ensureToken,
            listCerts: listCerts,
            certSelector: new FirstCertSelector(),
            hashPdf: hashPdf,
            submitSignHash: submitSignHash,
            sessionStore: store,
            clock: clock,
            validator: validator,
            clientIdAccessor: () => clientId,
            ttlAccessor: () => ttl ?? TimeSpan.FromHours(24),
            logger: NullLogger<BeginSignPdf>.Instance,
            correlation: correlation);
    }

    public static BeginSignXml BuildBeginSignXml(StubWireClient wire, StubSigningSessionStore store, FakeClock clock, string clientId = "client-1", TimeSpan? ttl = null)
    {
        var selector = new StaticKeySelector();
        var cache = new InMemoryTokenCache();
        var correlation = new StubCorrelationIdAccessor("cid-test");
        var ensureToken = new EnsureAccessToken(wire, cache, selector, clock, () => ("alice", "pass"));
        var listCerts = new ListActiveCertificates(wire, correlation);
        var hashXml = new HashXmlDocument(wire, correlation);
        var submitSignHash = new SubmitSignHash(wire);
        var validator = new SignXmlRequestValidator(correlation);
        return new BeginSignXml(
            ensureToken: ensureToken,
            listCerts: listCerts,
            certSelector: new FirstCertSelector(),
            hashXml: hashXml,
            submitSignHash: submitSignHash,
            sessionStore: store,
            clock: clock,
            validator: validator,
            clientIdAccessor: () => clientId,
            ttlAccessor: () => ttl ?? TimeSpan.FromHours(24),
            logger: NullLogger<BeginSignXml>.Instance,
            correlation: correlation);
    }

    public static BeginSignWord BuildBeginSignWord(StubWireClient wire, StubSigningSessionStore store, FakeClock clock, string clientId = "client-1", TimeSpan? ttl = null)
    {
        var selector = new StaticKeySelector();
        var cache = new InMemoryTokenCache();
        var correlation = new StubCorrelationIdAccessor("cid-test");
        var ensureToken = new EnsureAccessToken(wire, cache, selector, clock, () => ("alice", "pass"));
        var listCerts = new ListActiveCertificates(wire, correlation);
        var hashWord = new HashWordDocument(wire, correlation);
        var submitSignHash = new SubmitSignHash(wire);
        var validator = new SignWordRequestValidator(correlation);
        return new BeginSignWord(
            ensureToken: ensureToken,
            listCerts: listCerts,
            certSelector: new FirstCertSelector(),
            hashWord: hashWord,
            submitSignHash: submitSignHash,
            sessionStore: store,
            clock: clock,
            validator: validator,
            clientIdAccessor: () => clientId,
            ttlAccessor: () => ttl ?? TimeSpan.FromHours(24),
            logger: NullLogger<BeginSignWord>.Instance,
            correlation: correlation);
    }

    public static BeginSignExcel BuildBeginSignExcel(StubWireClient wire, StubSigningSessionStore store, FakeClock clock, string clientId = "client-1", TimeSpan? ttl = null)
    {
        var selector = new StaticKeySelector();
        var cache = new InMemoryTokenCache();
        var correlation = new StubCorrelationIdAccessor("cid-test");
        var ensureToken = new EnsureAccessToken(wire, cache, selector, clock, () => ("alice", "pass"));
        var listCerts = new ListActiveCertificates(wire, correlation);
        var hashExcel = new HashExcelDocument(wire, correlation);
        var submitSignHash = new SubmitSignHash(wire);
        var validator = new SignExcelRequestValidator(correlation);
        return new BeginSignExcel(
            ensureToken: ensureToken,
            listCerts: listCerts,
            certSelector: new FirstCertSelector(),
            hashExcel: hashExcel,
            submitSignHash: submitSignHash,
            sessionStore: store,
            clock: clock,
            validator: validator,
            clientIdAccessor: () => clientId,
            ttlAccessor: () => ttl ?? TimeSpan.FromHours(24),
            logger: NullLogger<BeginSignExcel>.Instance,
            correlation: correlation);
    }

    public static void WireDefaults(StubWireClient wire, FakeClock clock)
    {
        wire.OnLogin = (_, _, _) => Task.FromResult(ActiveSession(clock.UtcNow));
        wire.OnListCerts = (_, _) => Task.FromResult<IReadOnlyList<Certificate>>(new[] { ActiveCert() });
        wire.OnHash = (_, _, _, docId, _, _) => Task.FromResult(new PdfHashOutput(
            DocumentId: docId,
            DocumentBytes: "DOCBYTES",
            DocumentHash: "DOCHASH",
            Sh: "SH",
            SignatureName: "SigName",
            Digest: "DIGEST"));
        wire.OnHashXml = (_, _, _, docId, _, _) => Task.FromResult(new XmlHashOutput(
            DocumentId: docId,
            Document: "<doc/>",
            SignatureId: "sig-x",
            Digest: "DIGEST",
            Sh: "SH"));
        wire.OnHashWord = (_, _, _, docId, _, _) => Task.FromResult(new WordExcelHashOutput(
            DocumentId: docId,
            DocumentBytes: "WORDBYTES",
            SignatureId: "sig-w",
            Digest: "DIGEST",
            MainDom: "WORDMAIN"));
        wire.OnHashExcel = (_, _, _, docId, _, _) => Task.FromResult(new WordExcelHashOutput(
            DocumentId: docId,
            DocumentBytes: "EXCELBYTES",
            SignatureId: "sig-e",
            Digest: "DIGEST",
            MainDom: "EXCELMAIN"));
        wire.OnSubmitSignHash = (_, _, _, _, _, _, _) => Task.FromResult(new SignTransaction("tx-fake-1", clock.UtcNow));
    }
}
