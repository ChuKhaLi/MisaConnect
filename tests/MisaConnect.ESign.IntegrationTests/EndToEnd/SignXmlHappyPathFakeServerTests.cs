using System.Text;
using Microsoft.Extensions.DependencyInjection;
using MisaConnect.ESign.Client;
using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.IntegrationTests.EsignFake;
using Xunit;

namespace MisaConnect.ESign.IntegrationTests.EndToEnd;

public class SignXmlHappyPathFakeServerTests
{
    [Fact]
    public async Task End_to_end_xml_sign_populates_xmlDocs_only_and_returns_signed_xml()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        server.SignedXmlText = "<root><child>hello</child><signature/></root>";
        await using var sp = TestServiceProvider.Build(server.BaseUrl);

        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();
        var result = await client.SignXmlAsync(TestPerFormatFixtures.SampleXmlRequest(), CancellationToken.None);

        Assert.NotNull(result.SignedXml);
        Assert.True(result.SignedXml.Length > 0);
        Assert.Equal(DocumentFormat.Xml, result.Format);
        Assert.Equal("tx-fake-1", result.TransactionId);
        Assert.Equal(server.SignedXmlText, Encoding.UTF8.GetString(result.SignedXml));

        // FR-052 / SC-017: only the matching per-format array is populated on the wire.
        var hashReq = Assert.Single(server.HashRequests);
        Assert.Equal(RequestedFormat.Xml, hashReq.DetectedFormat);
        var attachReq = Assert.Single(server.AttachmentRequests);
        Assert.Equal(RequestedFormat.Xml, attachReq.DetectedFormat);
    }

    [Fact]
    public async Task End_to_end_xml_sign_via_bytes_overload_decodes_utf8()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        await using var sp = TestServiceProvider.Build(server.BaseUrl);

        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();
        var result = await client.SignXmlAsync(TestPerFormatFixtures.SampleXmlRequestFromBytes(), CancellationToken.None);

        Assert.Equal(DocumentFormat.Xml, result.Format);
        Assert.True(result.SignedXml.Length > 0);
    }
}
