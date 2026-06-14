using System.Net;
using Microsoft.Extensions.Options;
using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Infrastructure.Configuration;
using MisaConnect.ESign.Infrastructure.Credentials;
using MisaConnect.ESign.Infrastructure.ESign;
using MisaConnect.ESign.Infrastructure.Http;
using MisaConnect.ESign.UnitTests.TestSupport;
using Xunit;

namespace MisaConnect.ESign.UnitTests.Http;

// Slice 008 US1/US2 (FR-003): ClientHeadersHandler stamps x-clientId/x-clientKey
// from IMisaCredentialsAccessor per-call. Static default emits the options values;
// dynamic emits the accessor's values.
public class ClientHeadersHandlerTests
{
    private static async Task<HttpRequestMessage> CaptureAsync(IMisaCredentialsAccessor accessor)
    {
        var capturing = new CapturingHandler();
        using var handler = new ClientHeadersHandler(accessor, new StubCorrelationIdAccessor("cid"))
        {
            InnerHandler = capturing,
        };
        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/anything");
        using var response = await invoker.SendAsync(request, CancellationToken.None);
        return capturing.LastRequest!;
    }

    [Fact]
    public async Task Static_default_emits_options_client_credentials()
    {
        var options = Options.Create(new MisaESignOptions { ClientId = "opt-cid", ClientKey = "opt-ckey" });
        var request = await CaptureAsync(new OptionsMisaCredentialsAccessor(options));

        Assert.Equal("opt-cid", Assert.Single(request.Headers.GetValues(MisaESignWireClient.ClientIdHeader)));
        Assert.Equal("opt-ckey", Assert.Single(request.Headers.GetValues(MisaESignWireClient.ClientKeyHeader)));
    }

    [Fact]
    public async Task Dynamic_emits_accessor_client_credentials()
    {
        var request = await CaptureAsync(new StubMisaCredentialsAccessor(clientId: "dyn-cid", clientKey: "dyn-ckey"));

        Assert.Equal("dyn-cid", Assert.Single(request.Headers.GetValues(MisaESignWireClient.ClientIdHeader)));
        Assert.Equal("dyn-ckey", Assert.Single(request.Headers.GetValues(MisaESignWireClient.ClientKeyHeader)));
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
