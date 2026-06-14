using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Domain.Certificates;
using MisaConnect.ESign.Domain.Signing;
using MisaConnect.ESign.Infrastructure.Configuration;
using MisaConnect.ESign.Infrastructure.ESign;
using MisaConnect.ESign.UnitTests.TestSupport;
using Xunit;

namespace MisaConnect.ESign.UnitTests.Signing.Wire;

// Slice 007 US2: a null SignatureInfo.Page is sent to MISA as 1 (visible signatures
// require Page >= 1) and the default is logged; an explicit Page is sent verbatim
// with no defaulting log. Contract C3, FR-004.
public class SignatureInfoPageDefaultTests
{
    private static Certificate Cert() => new(
        UserId: "u", KeyAlias: "ka", AppName: "misa",
        KeyStatus: KeyStatus.ACTIVE, CertStatus: "ACTIVE",
        CertificateValue: "BASE64CERT",
        CertificateChain: new CertificateChain("SIGN", "INT", "ROOT"),
        EffectiveDate: DateTimeOffset.UtcNow.AddYears(-1),
        ExpirationDate: DateTimeOffset.UtcNow.AddYears(1),
        EmailName: "alice@example.com", IsAutoSign: false);

    private static SignatureInfo SigInfo(int? page) => new(
        SignatureName: "sig",
        HashAlgorithm: HashAlgorithm.SHA256,
        LogoImage: string.Empty,
        SignatureDescription: new SignatureDescription("Alice", "Hanoi", "Test", "alice@example.com"),
        RenderingMode: 1,
        Page: page);

    private static async Task<(string body, ListLogger logger)> RunHashPdf(int? page)
    {
        var handler = new CapturingHandler(
            "{\"pdfDocs\":[{\"documentId\":\"d\",\"documentBytes\":\"b\",\"documentHash\":\"h\",\"sh\":\"s\",\"signatureName\":\"n\",\"digest\":\"dg\"}]}");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var logger = new ListLogger();
        var client = new MisaESignWireClient(
            http,
            Options.Create(new MisaESignOptions()),
            new StubClock(),
            new StubCorrelationIdAccessor("cid"),
            logger);

        await client.HashPdfAsync("tok", Cert(), new byte[] { 1, 2, 3 }, "doc-1", SigInfo(page), CancellationToken.None);
        return (handler.LastRequestBody!, logger);
    }

    private static int WirePage(string body)
    {
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("pdfDocs")[0].GetProperty("SignatureInfo").GetProperty("Page").GetInt32();
    }

    [Fact]
    public async Task Null_page_is_sent_as_1_and_logged()
    {
        var (body, logger) = await RunHashPdf(page: null);

        Assert.Equal(1, WirePage(body));
        Assert.Contains(logger.Messages, m => m.Contains("Page", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Explicit_page_is_preserved_and_not_logged()
    {
        var (body, logger) = await RunHashPdf(page: 3);

        Assert.Equal(3, WirePage(body));
        Assert.DoesNotContain(logger.Messages, m => m.Contains("Page", StringComparison.OrdinalIgnoreCase) && m.Contains("default", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly string _responseJson;
        public string? LastRequestBody { get; private set; }
        public CapturingHandler(string responseJson) => _responseJson = responseJson;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseJson, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class StubClock : ISystemClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UnixEpoch;
    }

    private sealed class ListLogger : ILogger<MisaESignWireClient>
    {
        public List<string> Messages { get; } = new();
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
