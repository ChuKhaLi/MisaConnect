using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Application.Sessions;
using MisaConnect.ESign.Application.Validation;
using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.Domain.Webhook;
using Xunit;

namespace MisaConnect.ESign.UnitTests.Webhook;

public class WebhookEnvelopeValidatorTests
{
    private static SigningSession SampleSession(string clientId = "c", string txId = "tx", params string[] docIds)
    {
        var hash = new PdfHashOutput(
            DocumentId: docIds.Length > 0 ? docIds[0] : "doc-1",
            DocumentBytes: "AAA", DocumentHash: "BBB", Sh: "CC", SignatureName: "n", Digest: "DD");
        return new SigningSession(
            ClientId: clientId,
            TransactionId: txId,
            Format: DocumentFormat.Pdf,
            HashPayload: new PerFormatHashPayload.Pdf(hash),
            RecordedDocumentIds: docIds.Length > 0 ? docIds : new[] { "doc-1" },
            CreatedAtUtc: DateTimeOffset.UtcNow,
            Ttl: TimeSpan.FromMinutes(5),
            ObservedMessageIds: new HashSet<string>(),
            CachedSuccess: null);
    }

    private static WebhookEnvelope SampleEnvelope(string clientId = "c", WebhookStatus status = WebhookStatus.Success, params (string docId, string sig)[] sigs)
    {
        return new WebhookEnvelope(
            MessageId: "m1",
            ClientId: clientId,
            ExtraData: null,
            Status: status,
            ErrorCode: null,
            TransactionId: "tx",
            Signatures: sigs.Select(s => new WebhookSignature(s.docId, s.sig)).ToList());
    }

    [Fact]
    public void AssertClientIdMatch_throws_when_mismatched()
    {
        var v = new WebhookEnvelopeValidator();
        var env = SampleEnvelope(clientId: "wrong");
        Assert.Throws<ClientIdMismatchException>(() =>
            v.AssertClientIdMatch(env, configuredClientId: "right", correlationId: "cid"));
    }

    [Fact]
    public void AssertClientIdMatch_accepts_match()
    {
        var v = new WebhookEnvelopeValidator();
        var env = SampleEnvelope(clientId: "c");
        v.AssertClientIdMatch(env, configuredClientId: "c", correlationId: "cid");
    }

    [Fact]
    public void AssertSuccessShapeIsValid_throws_on_empty_signatures()
    {
        var v = new WebhookEnvelopeValidator();
        var session = SampleSession();
        var env = SampleEnvelope();
        Assert.Throws<IncompleteSuccessEnvelopeException>(() =>
            v.AssertSuccessShapeIsValid(env, session, correlationId: "cid"));
    }

    [Fact]
    public void AssertSuccessShapeIsValid_throws_on_documentId_not_in_session()
    {
        var v = new WebhookEnvelopeValidator();
        var session = SampleSession(docIds: new[] { "doc-1" });
        var env = SampleEnvelope(sigs: new[] { ("doc-99", "sig-bytes") });
        Assert.Throws<DocumentIdMismatchException>(() =>
            v.AssertSuccessShapeIsValid(env, session, correlationId: "cid"));
    }

    [Fact]
    public void AssertSuccessShapeIsValid_accepts_documentId_in_session()
    {
        var v = new WebhookEnvelopeValidator();
        var session = SampleSession(docIds: new[] { "doc-1" });
        var env = SampleEnvelope(sigs: new[] { ("doc-1", "sig-bytes") });
        v.AssertSuccessShapeIsValid(env, session, correlationId: "cid");
    }
}
