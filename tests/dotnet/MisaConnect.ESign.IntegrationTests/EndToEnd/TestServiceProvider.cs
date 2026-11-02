using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MisaConnect.ESign.Client.DependencyInjection;
using MisaConnect.ESign.Infrastructure.Configuration;

namespace MisaConnect.ESign.IntegrationTests.EndToEnd;

internal static class TestServiceProvider
{
    public static ServiceProvider Build(string baseUrl, Action<MisaESignOptions>? extra = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.None));
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
            extra?.Invoke(o);
        });
        return services.BuildServiceProvider();
    }
}
