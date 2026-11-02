using MisaConnect.ESign.Application.Errors;
using MisaConnect.ESign.Domain.Errors;
using Xunit;

namespace MisaConnect.ESign.UnitTests.Webhook;

public class ESignErrorMapperWebhookTests
{
    [Fact]
    public void MapWebhookValidationToAck_returns_webhook_malformed_for_MalformedEnvelopeException()
    {
        var ex = new MalformedEnvelopeException("cid", "bad payload");
        var ack = ESignErrorMapper.MapWebhookValidationToAck(ex);
        Assert.Equal("webhook.malformed", ack.ErrorCode);
    }

    [Fact]
    public void MapWebhookValidationToAck_returns_webhook_client_id_mismatch_for_ClientIdMismatchException()
    {
        var ex = new ClientIdMismatchException("cid", "mismatch");
        var ack = ESignErrorMapper.MapWebhookValidationToAck(ex);
        Assert.Equal("webhook.client_id_mismatch", ack.ErrorCode);
    }

    [Fact]
    public void MapWebhookValidationToAck_returns_webhook_unknown_transaction_for_UnknownTransactionException()
    {
        var ex = new UnknownTransactionException("cid", "tx-1", "no session");
        var ack = ESignErrorMapper.MapWebhookValidationToAck(ex);
        Assert.Equal("webhook.unknown_transaction", ack.ErrorCode);
    }

    [Fact]
    public void MapWebhookValidationToAck_returns_webhook_incomplete_success_for_IncompleteSuccessEnvelopeException()
    {
        var ex = new IncompleteSuccessEnvelopeException("cid", "tx-1", MisaConnect.ESign.Domain.Documents.DocumentFormat.Pdf, "no signatures");
        var ack = ESignErrorMapper.MapWebhookValidationToAck(ex);
        Assert.Equal("webhook.incomplete_success", ack.ErrorCode);
    }

    [Fact]
    public void MapWebhookValidationToAck_returns_webhook_document_id_mismatch_for_DocumentIdMismatchException()
    {
        var ex = new DocumentIdMismatchException("cid", "tx-1", MisaConnect.ESign.Domain.Documents.DocumentFormat.Pdf, "doc mismatch");
        var ack = ESignErrorMapper.MapWebhookValidationToAck(ex);
        Assert.Equal("webhook.document_id_mismatch", ack.ErrorCode);
    }
}
