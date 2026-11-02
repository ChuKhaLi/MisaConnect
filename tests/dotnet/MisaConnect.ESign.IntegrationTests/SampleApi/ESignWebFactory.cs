using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MisaConnect.ESign.IntegrationTests.SampleApi;

/// <summary>
/// WebApplicationFactory wrapper used by slice-4 sample-API auth tests
/// (FR-099 / FR-100 / FR-101 — secret routing, CIDR allowlist, startup WARN).
/// Lets each test override <c>Misa:ESign:Webhook:*</c> + <c>BaseUrl</c> and
/// capture startup-time log entries.
/// </summary>
internal sealed class ESignWebFactory : WebApplicationFactory<Program>
{
    private readonly Action<IDictionary<string, string?>>? _configOverride;
    private readonly Action<List<string>>? _logCaptureRegistration;
    private readonly IPAddress? _forcedRemoteIp;

    public ESignWebFactory(
        Action<IDictionary<string, string?>>? configOverride = null,
        Action<List<string>>? logCaptureRegistration = null,
        IPAddress? forcedRemoteIp = null)
    {
        _configOverride = configOverride;
        _logCaptureRegistration = logCaptureRegistration;
        _forcedRemoteIp = forcedRemoteIp;
    }

    public ESignWebFactory WithConfig(Action<IDictionary<string, string?>> config) =>
        new(config, _logCaptureRegistration, _forcedRemoteIp);

    public ESignWebFactory CapturingLogs(List<string> sink) =>
        new ESignWebFactory(_configOverride, _ => { }, _forcedRemoteIp).SetSink(sink);

    public ESignWebFactory WithForcedRemoteIp(IPAddress ip) =>
        new(_configOverride, _logCaptureRegistration, ip);

    private List<string>? _logSink;
    private ESignWebFactory SetSink(List<string> sink)
    {
        _logSink = sink;
        return this;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            var dict = new Dictionary<string, string?>
            {
                ["Misa:EInvoice:Environment"] = "Sandbox",
                ["Misa:EInvoice:BaseUrl"] = "https://testapi.meinvoice.vn/api/integration",
                ["Misa:EInvoice:TaxCode"] = "0000000000",
                ["Misa:EInvoice:UserName"] = "test@example.com",
                ["Misa:EInvoice:Password"] = "placeholder",
                ["Misa:EInvoice:AppId"] = "1",
                ["Misa:ESign:Environment"] = "Sandbox",
                ["Misa:ESign:BaseUrl"] = "http://127.0.0.1:65535/",
                ["Misa:ESign:ClientId"] = "client-id",
                ["Misa:ESign:ClientKey"] = "client-key",
                ["Misa:ESign:UserName"] = "alice",
                ["Misa:ESign:Password"] = "password",
                ["Misa:ESign:Webhook:Mode"] = "Both",
            };
            _configOverride?.Invoke(dict);
            config.AddInMemoryCollection(dict);
        });

        builder.ConfigureLogging((_, lb) =>
        {
            lb.SetMinimumLevel(LogLevel.Information);
        });
        if (_logSink is not null)
        {
            var sink = _logSink;
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<ILoggerProvider>(new ListLoggerProvider(sink));
            });
        }

        if (_forcedRemoteIp is not null)
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<Microsoft.AspNetCore.Http.IHttpContextAccessor, Microsoft.AspNetCore.Http.HttpContextAccessor>();
                services.AddSingleton<IStartupFilter>(new ForcedRemoteIpStartupFilter(_forcedRemoteIp));
            });
        }
    }

    private sealed class ListLoggerProvider : ILoggerProvider
    {
        private readonly List<string> _sink;
        public ListLoggerProvider(List<string> sink) { _sink = sink; }
        public ILogger CreateLogger(string categoryName) => new ListLogger(_sink, categoryName);
        public void Dispose() { }
    }

    private sealed class ListLogger : ILogger
    {
        private readonly List<string> _sink;
        private readonly string _category;
        public ListLogger(List<string> sink, string category) { _sink = sink; _category = category; }
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_sink)
            {
                _sink.Add($"[{logLevel}] {_category}: {formatter(state, exception)}");
            }
        }
        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }

    private sealed class ForcedRemoteIpStartupFilter : IStartupFilter
    {
        private readonly IPAddress _ip;
        public ForcedRemoteIpStartupFilter(IPAddress ip) { _ip = ip; }
        public Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> Configure(Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> next) => app =>
        {
            app.Use((Microsoft.AspNetCore.Http.RequestDelegate inner) => async context =>
            {
                context.Connection.RemoteIpAddress = _ip;
                await inner(context);
            });
            next(app);
        };
    }
}
