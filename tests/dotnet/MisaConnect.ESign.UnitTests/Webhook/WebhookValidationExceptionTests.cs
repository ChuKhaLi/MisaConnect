using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.Domain.Webhook;
using Xunit;

namespace MisaConnect.ESign.UnitTests.Webhook;

public class WebhookValidationExceptionTests
{
    [Fact]
    public void MalformedEnvelopeException_carries_unknown_format_and_null_matched_tx()
    {
        var ex = new MalformedEnvelopeException("cid-1", "bad payload");
        Assert.Equal(WebhookValidationCategory.MalformedEnvelope, ex.WebhookCategory);
        Assert.Equal("cid-1", ex.CorrelationId);
        Assert.Equal(DocumentFormat.Unknown, ex.Format);
        Assert.Null(ex.MatchedTransactionId);
    }

    [Fact]
    public void ClientIdMismatchException_carries_unknown_format_and_null_matched_tx()
    {
        var ex = new ClientIdMismatchException("cid-2", "mismatch");
        Assert.Equal(WebhookValidationCategory.ClientIdMismatch, ex.WebhookCategory);
        Assert.Equal(DocumentFormat.Unknown, ex.Format);
        Assert.Null(ex.MatchedTransactionId);
    }

    [Fact]
    public void UnknownTransactionException_carries_unknown_format_and_the_envelope_transactionId()
    {
        var ex = new UnknownTransactionException("cid-3", "tx-99", "no session");
        Assert.Equal(WebhookValidationCategory.UnknownTransaction, ex.WebhookCategory);
        Assert.Equal(DocumentFormat.Unknown, ex.Format);
        Assert.Equal("tx-99", ex.MatchedTransactionId);
    }

    [Fact]
    public void IncompleteSuccessEnvelopeException_carries_resolved_format_from_session()
    {
        var ex = new IncompleteSuccessEnvelopeException("cid-4", "tx-7", DocumentFormat.Xml, "no signatures");
        Assert.Equal(WebhookValidationCategory.IncompleteSuccessEnvelope, ex.WebhookCategory);
        Assert.Equal(DocumentFormat.Xml, ex.Format);
        Assert.Equal("tx-7", ex.MatchedTransactionId);
    }

    [Fact]
    public void DocumentIdMismatchException_carries_resolved_format_from_session()
    {
        var ex = new DocumentIdMismatchException("cid-5", "tx-7", DocumentFormat.Word, "doc mismatch");
        Assert.Equal(WebhookValidationCategory.DocumentIdMismatch, ex.WebhookCategory);
        Assert.Equal(DocumentFormat.Word, ex.Format);
        Assert.Equal("tx-7", ex.MatchedTransactionId);
    }
}
