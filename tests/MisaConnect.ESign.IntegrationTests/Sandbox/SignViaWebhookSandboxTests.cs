using Microsoft.Extensions.DependencyInjection;
using MisaConnect.ESign.Application.Webhook;
using MisaConnect.ESign.Client;
using MisaConnect.ESign.Client.DependencyInjection;
using MisaConnect.ESign.Domain.Webhook;
using MisaConnect.ESign.Infrastructure.Configuration;
using MisaConnect.ESign.IntegrationTests.EndToEnd;
using Xunit;

namespace MisaConnect.ESign.IntegrationTests.Sandbox;

/// <summary>
/// T107 / FR-097 / SC-034 — end-to-end webhook-mode sign against the real
/// MISA sandbox. Skips cleanly when sandbox creds or
/// <c>MISACONNECT_ESIGN_SANDBOX_WEBHOOK_URL</c> are absent. Optional
/// <c>MISACONNECT_ESIGN_SANDBOX_WEBHOOK_TIMEOUT</c> overrides the default
/// 5-minute wait.
/// </summary>
public class SignViaWebhookSandboxTests
{
    private sealed class WaitableHook : IWebhookDeliveryHook
    {
        private readonly TaskCompletionSource<WebhookOutcome.SuccessWithSignedBytes> _tcs = new();
        public Task<WebhookOutcome.SuccessWithSignedBytes> WaitForSuccessAsync(TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            cts.Token.Register(() => _tcs.TrySetCanceled());
            return _tcs.Task;
        }
        public Task DeliverAsync(WebhookOutcome outcome, CancellationToken ct)
        {
            if (outcome is WebhookOutcome.SuccessWithSignedBytes s) _tcs.TrySetResult(s);
            return Task.CompletedTask;
        }
    }

    [SandboxFact(SandboxRequirement.Webhook)]
    public async Task Sandbox_webhook_round_trip_returns_signed_pdf_via_delivery_hook()
    {
        var timeoutEnv = Environment.GetEnvironmentVariable("MISACONNECT_ESIGN_SANDBOX_WEBHOOK_TIMEOUT");
        var timeout = TimeSpan.TryParse(timeoutEnv, out var parsed) ? parsed : TimeSpan.FromMinutes(5);

        var hook = new WaitableHook();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMisaConnectESign(o =>
        {
            o.Environment = ESignEnvironment.Sandbox;
            o.BaseUrl = Environment.GetEnvironmentVariable("MISACONNECT_ESIGN_SANDBOX_BASE_URL")!;
            o.ClientId = Environment.GetEnvironmentVariable("MISACONNECT_ESIGN_SANDBOX_CLIENT_ID")!;
            o.ClientKey = Environment.GetEnvironmentVariable("MISACONNECT_ESIGN_SANDBOX_CLIENT_KEY")!;
            o.UserName = Environment.GetEnvironmentVariable("MISACONNECT_ESIGN_SANDBOX_USERNAME")!;
            o.Password = Environment.GetEnvironmentVariable("MISACONNECT_ESIGN_SANDBOX_PASSWORD")!;
            o.Webhook.Mode = WebhookMode.Webhook;
        });
        services.AddSingleton<IWebhookDeliveryHook>(hook);

        await using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();

        var begin = await client.BeginSignPdfAsync(TestPdfFixture.SampleRequest(), CancellationToken.None);
        Assert.NotEmpty(begin.TransactionId);

        // Caller has registered the webhook URL out-of-band; we wait for MISA
        // to POST it back to the SDK (via the consumer-hosted endpoint at
        // MISACONNECT_ESIGN_SANDBOX_WEBHOOK_URL) which invokes HandleWebhookAsync
        // and surfaces the signed bytes through the delivery hook.
        var outcome = await hook.WaitForSuccessAsync(timeout);
        Assert.NotNull(outcome.SignedBytes);
        Assert.True(outcome.SignedBytes.Length > 0);
    }
}
