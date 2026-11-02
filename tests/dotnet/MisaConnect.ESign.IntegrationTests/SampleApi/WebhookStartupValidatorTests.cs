using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MisaConnect.ESign.Infrastructure.Configuration;
using MisaConnect.Samples.Api.Middleware;
using Xunit;

namespace MisaConnect.ESign.IntegrationTests.SampleApi;

/// <summary>
/// FR-101 / SC-037: Direct unit test of the sample-API startup validator.
/// (Integration test via WebApplicationFactory was infeasible — minimal-API
/// + WebApplicationBuilder doesn't attach test-side ILoggerProvider before
/// Program.cs emits its startup WARN.)
/// </summary>
public class WebhookStartupValidatorTests
{
    private const string WarnSentinel = "Webhook endpoint is publicly reachable with no transport-layer auth";

    [Fact]
    public void Emits_warn_when_mode_is_Both_and_no_auth_layer_configured()
    {
        var sink = new List<string>();
        var sp = BuildSp(sink, mode: WebhookMode.Both, secret: null, allowedIps: null);

        ESignWebhookStartupValidator.EmitWarnIfPubliclyReachable(sp);

        Assert.Single(sink.Where(line => line.Contains(WarnSentinel)));
    }

    [Fact]
    public void Emits_warn_when_mode_is_Webhook_only_and_no_auth_layer_configured()
    {
        var sink = new List<string>();
        var sp = BuildSp(sink, mode: WebhookMode.Webhook, secret: null, allowedIps: null);

        ESignWebhookStartupValidator.EmitWarnIfPubliclyReachable(sp);

        Assert.Single(sink.Where(line => line.Contains(WarnSentinel)));
    }

    [Fact]
    public void Does_not_emit_warn_when_secret_is_configured()
    {
        var sink = new List<string>();
        var sp = BuildSp(sink, mode: WebhookMode.Both, secret: "0123456789abcdef0123456789abcdef", allowedIps: null);

        ESignWebhookStartupValidator.EmitWarnIfPubliclyReachable(sp);

        Assert.DoesNotContain(sink, line => line.Contains(WarnSentinel));
    }

    [Fact]
    public void Does_not_emit_warn_when_allowed_ips_configured()
    {
        var sink = new List<string>();
        var sp = BuildSp(sink, mode: WebhookMode.Both, secret: null, allowedIps: new[] { "10.0.0.0/8" });

        ESignWebhookStartupValidator.EmitWarnIfPubliclyReachable(sp);

        Assert.DoesNotContain(sink, line => line.Contains(WarnSentinel));
    }

    [Fact]
    public void Does_not_emit_warn_when_mode_is_Polling()
    {
        var sink = new List<string>();
        var sp = BuildSp(sink, mode: WebhookMode.Polling, secret: null, allowedIps: null);

        ESignWebhookStartupValidator.EmitWarnIfPubliclyReachable(sp);

        Assert.DoesNotContain(sink, line => line.Contains(WarnSentinel));
    }

    private static IServiceProvider BuildSp(List<string> sink, WebhookMode mode, string? secret, string[]? allowedIps)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(new SinkLoggerProvider(sink)));
        services.AddSingleton<IOptions<MisaESignOptions>>(Options.Create(new MisaESignOptions
        {
            Webhook = new MisaESignWebhookOptions
            {
                Mode = mode,
                Secret = secret,
                AllowedIps = allowedIps,
            },
        }));
        return services.BuildServiceProvider();
    }

    private sealed class SinkLoggerProvider : ILoggerProvider
    {
        private readonly List<string> _sink;
        public SinkLoggerProvider(List<string> sink) { _sink = sink; }
        public ILogger CreateLogger(string categoryName) => new SinkLogger(_sink);
        public void Dispose() { }
    }

    private sealed class SinkLogger : ILogger
    {
        private readonly List<string> _sink;
        public SinkLogger(List<string> sink) { _sink = sink; }
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex, Func<TState, Exception?, string> formatter)
        {
            lock (_sink) { _sink.Add($"[{level}] {formatter(state, ex)}"); }
        }
        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
