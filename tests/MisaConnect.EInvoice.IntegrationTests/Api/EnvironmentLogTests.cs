using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MisaConnect.EInvoice.IntegrationTests.Api;

public class EnvironmentLogTests
{
    [Fact]
    public async Task Host_logs_active_MeInvoice_environment_at_startup()
    {
        var sink = new InMemoryLogSink();

        await using var factory = new EnvironmentLogFactory(sink);
        _ = factory.CreateClient();

        // Touch the host to ensure it has fully started.
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health");
        Assert.True(response.IsSuccessStatusCode);

        Assert.Contains(
            sink.Entries,
            e => e.Category.StartsWith("MisaConnect.Samples.Api") &&
                 e.Message.Contains("MeInvoice environment: Sandbox"));
    }

    private sealed class EnvironmentLogFactory : WebApplicationFactory<Program>
    {
        private readonly InMemoryLogSink _sink;

        public EnvironmentLogFactory(InMemoryLogSink sink)
        {
            _sink = sink;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Misa:EInvoice:Environment"] = "Sandbox",
                    ["Misa:EInvoice:BaseUrl"] = "https://testapi.meinvoice.vn/api/integration",
                    ["Misa:EInvoice:TaxCode"] = "0000000000",
                    ["Misa:EInvoice:UserName"] = "test@example.com",
                    ["Misa:EInvoice:Password"] = "placeholder",
                    ["Misa:EInvoice:AppId"] = "test-app",
                });
            });

            builder.ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddProvider(new InMemoryLoggerProvider(_sink));
                logging.SetMinimumLevel(LogLevel.Debug);
            });
        }
    }

    private sealed class InMemoryLogSink
    {
        public ConcurrentQueue<LogEntry> Entries { get; } = new();
    }

    private sealed record LogEntry(string Category, LogLevel Level, string Message);

    private sealed class InMemoryLoggerProvider : ILoggerProvider
    {
        private readonly InMemoryLogSink _sink;

        public InMemoryLoggerProvider(InMemoryLogSink sink) => _sink = sink;

        public ILogger CreateLogger(string categoryName) => new InMemoryLogger(categoryName, _sink);

        public void Dispose() { }
    }

    private sealed class InMemoryLogger : ILogger
    {
        private readonly string _category;
        private readonly InMemoryLogSink _sink;

        public InMemoryLogger(string category, InMemoryLogSink sink)
        {
            _category = category;
            _sink = sink;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, System.Exception? exception, System.Func<TState, System.Exception?, string> formatter)
        {
            _sink.Entries.Enqueue(new LogEntry(_category, logLevel, formatter(state, exception)));
        }
    }
}
