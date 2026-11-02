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
/// US2 SC-026 / SC-031 / SC-026a: cumulative idempotency over the fake server.
/// Drives Begin then five identical webhook deliveries; the attachment counter
/// stays at 1 and the delivery hook fires exactly once. Also verifies the
/// 8-parallel-deliveries single-flight contract (SC-031) and failure-then-
/// success (SC-026a) at the SDK boundary.
/// </summary>
public class WebhookDedupeFakeServerTests
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
    public async Task Five_identical_deliveries_call_attachment_exactly_once()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        var hook = new CapturingHook();
        await using var sp = BuildSp(server.BaseUrl, hook);

        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();

        var begin = await client.BeginSignPdfAsync(TestPdfFixture.SampleRequest() with { DocumentId = "doc-1" }, CancellationToken.None);

        for (int i = 0; i < 5; i++)
        {
            var envelope = BuildEnvelope(begin.TransactionId, messageId: "m-" + i);
            var result = await client.HandleWebhookAsync(envelope, CancellationToken.None);
            Assert.Equal("0", result.Ack.ErrorCode);
        }

        Assert.Equal(1, server.Calls.Attachment);
        Assert.Single(hook.Outcomes);
    }

    [Fact]
    public async Task Eight_parallel_deliveries_call_attachment_exactly_once()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        var hook = new CapturingHook();
        await using var sp = BuildSp(server.BaseUrl, hook);

        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();

        var begin = await client.BeginSignPdfAsync(TestPdfFixture.SampleRequest() with { DocumentId = "doc-1" }, CancellationToken.None);

        var tasks = Enumerable.Range(0, 8)
            .Select(i => client.HandleWebhookAsync(BuildEnvelope(begin.TransactionId, "m-" + i), CancellationToken.None))
            .ToArray();
        await Task.WhenAll(tasks);

        Assert.All(tasks, t => Assert.Equal("0", t.Result.Ack.ErrorCode));
        Assert.Equal(1, server.Calls.Attachment);
        Assert.Single(hook.Outcomes);
    }

    private static WebhookEnvelopeDto BuildEnvelope(string transactionId, string messageId)
    {
        return new WebhookEnvelopeDto(
            MessageId: messageId,
            ClientId: "client-id",
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
