using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MisaConnect.ESign.Application.Webhook;
using MisaConnect.ESign.Client;
using MisaConnect.ESign.Client.Dtos.Webhook;
using MisaConnect.ESign.Domain.Webhook;
using MisaConnect.ESign.IntegrationTests.EsignFake;
using Xunit;

namespace MisaConnect.ESign.IntegrationTests.EndToEnd;

/// <summary>
/// End-to-end happy-path for each format: BeginSign{Format}Async then a
/// synthetic MISA-shaped webhook envelope arrives, the SDK finalizes via
/// <c>/documents/attachment</c>, the delivery hook receives the signed bytes,
/// and the success ACK is returned (SC-025 / SC-028 / SC-029).
/// </summary>
public class BeginAndWebhookHappyPathFakeServerTests
{
    private sealed class CapturingHook : IWebhookDeliveryHook
    {
        public List<WebhookOutcome> Outcomes { get; } = new();
        public Task DeliverAsync(WebhookOutcome outcome, CancellationToken ct)
        {
            Outcomes.Add(outcome);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Pdf_begin_then_webhook_returns_signed_bytes_without_polling()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        var hook = new CapturingHook();
        await using var sp = BuildSp(server.BaseUrl, hook);

        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();

        var begin = await client.BeginSignPdfAsync(TestPdfFixture.SampleRequest() with { DocumentId = "doc-1" }, CancellationToken.None);

        Assert.Equal("tx-fake-1", begin.TransactionId);
        Assert.Equal(MisaConnect.ESign.Domain.Documents.DocumentFormat.Pdf, begin.Format);
        Assert.Equal(0, server.Calls.SignStatus);

        var envelope = BuildEnvelopeDto(begin.TransactionId, clientId: "client-id");
        var result = await client.HandleWebhookAsync(envelope, CancellationToken.None);

        Assert.Equal("0", result.Ack.ErrorCode);
        Assert.Equal(1, server.Calls.Attachment);
        Assert.Equal(0, server.Calls.SignStatus);
        var outcome = Assert.IsType<WebhookOutcomeDto.SuccessWithSignedBytes>(result.Outcome);
        Assert.Equal(server.SignedPdfBytes, outcome.SignedBytes);
        Assert.Single(hook.Outcomes);
    }

    [Fact]
    public async Task Xml_begin_then_webhook_returns_signed_xml_bytes()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        var hook = new CapturingHook();
        await using var sp = BuildSp(server.BaseUrl, hook);

        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();

        var begin = await client.BeginSignXmlAsync(TestPerFormatFixtures.SampleXmlRequest() with { DocumentId = "doc-1" }, CancellationToken.None);
        var envelope = BuildEnvelopeDto(begin.TransactionId, clientId: "client-id");
        var result = await client.HandleWebhookAsync(envelope, CancellationToken.None);

        Assert.Equal("0", result.Ack.ErrorCode);
        Assert.Equal(1, server.Calls.Attachment);
        Assert.Single(hook.Outcomes);
    }

    [Fact]
    public async Task Word_begin_then_webhook_returns_signed_word_bytes()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        var hook = new CapturingHook();
        await using var sp = BuildSp(server.BaseUrl, hook);

        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();

        var begin = await client.BeginSignWordAsync(TestPerFormatFixtures.SampleWordRequest() with { DocumentId = "doc-1" }, CancellationToken.None);
        var envelope = BuildEnvelopeDto(begin.TransactionId, clientId: "client-id");
        var result = await client.HandleWebhookAsync(envelope, CancellationToken.None);

        Assert.Equal("0", result.Ack.ErrorCode);
        Assert.Equal(1, server.Calls.Attachment);
        Assert.Single(hook.Outcomes);
    }

    [Fact]
    public async Task Excel_begin_then_webhook_returns_signed_excel_bytes()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        var hook = new CapturingHook();
        await using var sp = BuildSp(server.BaseUrl, hook);

        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();

        var begin = await client.BeginSignExcelAsync(TestPerFormatFixtures.SampleExcelRequest() with { DocumentId = "doc-1" }, CancellationToken.None);
        var envelope = BuildEnvelopeDto(begin.TransactionId, clientId: "client-id");
        var result = await client.HandleWebhookAsync(envelope, CancellationToken.None);

        Assert.Equal("0", result.Ack.ErrorCode);
        Assert.Equal(1, server.Calls.Attachment);
        Assert.Single(hook.Outcomes);
    }

    private static WebhookEnvelopeDto BuildEnvelopeDto(string transactionId, string clientId)
    {
        return new WebhookEnvelopeDto(
            MessageId: Guid.NewGuid().ToString("N"),
            ClientId: clientId,
            ExtraData: null,
            Status: "SUCCESS",
            ErrorCode: null,
            TransactionId: transactionId,
            Signatures: new List<WebhookSignatureDto>
            {
                new("doc-1", "SIGNATURE-BYTES"),
            });
    }

    private static ServiceProvider BuildSp(string baseUrl, IWebhookDeliveryHook hook)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.None));
        MisaConnect.ESign.Client.DependencyInjection.ServiceCollectionExtensions.AddMisaConnectESign(services, o =>
        {
            o.Environment = MisaConnect.ESign.Infrastructure.Configuration.ESignEnvironment.Sandbox;
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
}
