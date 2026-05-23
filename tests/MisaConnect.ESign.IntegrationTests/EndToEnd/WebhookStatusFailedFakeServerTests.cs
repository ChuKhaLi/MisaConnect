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
/// FR-083: when MISA's webhook envelope reports FAILED or CANCELLED, the SDK
/// does NOT call /documents/attachment, returns the success ACK to MISA
/// (so MISA stops retrying), and invokes the delivery hook with a
/// TerminalWithoutFinalize outcome carrying the MISA-side errorCode.
/// </summary>
public class WebhookStatusFailedFakeServerTests
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
    public async Task Webhook_failed_status_returns_success_ack_and_skips_finalize()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        var hook = new CapturingHook();
        await using var sp = BuildSp(server.BaseUrl, hook);

        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();

        var begin = await client.BeginSignPdfAsync(TestPdfFixture.SampleRequest() with { DocumentId = "doc-1" }, CancellationToken.None);

        var envelope = new WebhookEnvelopeDto(
            MessageId: Guid.NewGuid().ToString("N"),
            ClientId: "client-id",
            ExtraData: null,
            Status: "FAILED",
            ErrorCode: "MisaErr-99",
            TransactionId: begin.TransactionId,
            Signatures: new List<WebhookSignatureDto>());

        var result = await client.HandleWebhookAsync(envelope, CancellationToken.None);

        Assert.Equal("0", result.Ack.ErrorCode);
        Assert.Equal(0, server.Calls.Attachment);
        var terminal = Assert.IsType<WebhookOutcomeDto.TerminalWithoutFinalize>(result.Outcome);
        Assert.Equal(WebhookStatusDto.Failed, terminal.Status);
        Assert.Equal("MisaErr-99", terminal.MisaErrorCode);
        Assert.Single(hook.Outcomes);
    }

    [Fact]
    public async Task Webhook_cancelled_status_returns_success_ack_and_skips_finalize()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        var hook = new CapturingHook();
        await using var sp = BuildSp(server.BaseUrl, hook);

        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();

        var begin = await client.BeginSignPdfAsync(TestPdfFixture.SampleRequest() with { DocumentId = "doc-1" }, CancellationToken.None);

        var envelope = new WebhookEnvelopeDto(
            MessageId: Guid.NewGuid().ToString("N"),
            ClientId: "client-id",
            ExtraData: null,
            Status: "CANCELLED",
            ErrorCode: null,
            TransactionId: begin.TransactionId,
            Signatures: new List<WebhookSignatureDto>());

        var result = await client.HandleWebhookAsync(envelope, CancellationToken.None);

        Assert.Equal("0", result.Ack.ErrorCode);
        Assert.Equal(0, server.Calls.Attachment);
        var terminal = Assert.IsType<WebhookOutcomeDto.TerminalWithoutFinalize>(result.Outcome);
        Assert.Equal(WebhookStatusDto.Cancelled, terminal.Status);
        Assert.Single(hook.Outcomes);
    }

    private static ServiceProvider BuildSp(string baseUrl, IWebhookDeliveryHook hook)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.None));
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
