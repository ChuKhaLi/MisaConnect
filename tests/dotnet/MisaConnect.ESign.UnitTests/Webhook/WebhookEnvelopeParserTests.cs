using MisaConnect.ESign.Client.Dtos.Webhook;
using MisaConnect.ESign.Client.Webhook;
using MisaConnect.ESign.Domain.Webhook;
using Xunit;

namespace MisaConnect.ESign.UnitTests.Webhook;

public class WebhookEnvelopeParserTests
{
    private static WebhookEnvelopeDto Valid(
        string? messageId = "m-1",
        string? clientId = "c-1",
        string? status = "SUCCESS",
        string? transactionId = "tx-1",
        List<WebhookSignatureDto>? signatures = null,
        bool nullSignatures = false) => new(
            MessageId: messageId!,
            ClientId: clientId!,
            ExtraData: null,
            Status: status!,
            ErrorCode: null,
            TransactionId: transactionId!,
            Signatures: nullSignatures ? null! : signatures ?? new List<WebhookSignatureDto> { new("doc-1", "SIG") });

    public static IEnumerable<object?[]> BadEnvelopes()
    {
        yield return new object?[] { Valid(messageId: null), "messageId" };
        yield return new object?[] { Valid(messageId: " "), "messageId" };
        yield return new object?[] { Valid(clientId: null), "clientId" };
        yield return new object?[] { Valid(clientId: ""), "clientId" };
        yield return new object?[] { Valid(transactionId: null), "transactionId" };
        yield return new object?[] { Valid(transactionId: "  "), "transactionId" };
        yield return new object?[] { Valid(status: null), "status" };
        yield return new object?[] { Valid(status: "DONE"), "status" };
        yield return new object?[] { Valid(signatures: new List<WebhookSignatureDto> { null! }), "signatures" };
    }

    [Theory]
    [MemberData(nameof(BadEnvelopes))]
    public void Malformed_envelope_returns_false_with_field_name(WebhookEnvelopeDto dto, string field)
    {
        var ok = WebhookEnvelopeParser.TryParse(dto, out var envelope, out var malformed);

        Assert.False(ok);
        Assert.Null(envelope);
        Assert.Equal(field, malformed);
    }

    [Theory]
    [InlineData("success", WebhookStatus.Success)]
    [InlineData("Cancelled", WebhookStatus.Cancelled)]
    [InlineData("failed", WebhookStatus.Failed)]
    public void Status_is_case_insensitive(string status, WebhookStatus expected)
    {
        var ok = WebhookEnvelopeParser.TryParse(Valid(status: status), out var envelope, out _);

        Assert.True(ok);
        Assert.Equal(expected, envelope!.Status);
    }

    [Fact]
    public void Null_signatures_is_empty_list()
    {
        var ok = WebhookEnvelopeParser.TryParse(Valid(nullSignatures: true), out var envelope, out _);

        Assert.True(ok);
        Assert.Empty(envelope!.Signatures);
    }

    [Fact]
    public void Valid_envelope_maps_fields()
    {
        var ok = WebhookEnvelopeParser.TryParse(Valid(), out var envelope, out _);

        Assert.True(ok);
        Assert.Equal("m-1", envelope!.MessageId);
        Assert.Equal("c-1", envelope.ClientId);
        Assert.Equal("tx-1", envelope.TransactionId);
        var sig = Assert.Single(envelope.Signatures);
        Assert.Equal("doc-1", sig.DocumentId);
    }
}
