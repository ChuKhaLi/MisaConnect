using System.Text;
using Microsoft.Extensions.DependencyInjection;
using MisaConnect.ESign.Client;
using MisaConnect.ESign.IntegrationTests.EsignFake;
using Xunit;

namespace MisaConnect.ESign.IntegrationTests.EndToEnd;

public class MultiArrayResponseDispatchFakeServerTests
{
    [Fact]
    public async Task Xml_facade_returns_only_xmlDocs_payload_when_response_has_all_four_arrays()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        server.Configure.MultiArrayResponse = true;
        server.SignedXmlText = "<x-only/>";
        await using var sp = TestServiceProvider.Build(server.BaseUrl);
        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();
        var result = await client.SignXmlAsync(TestPerFormatFixtures.SampleXmlRequest(), CancellationToken.None);
        Assert.Equal("<x-only/>", Encoding.UTF8.GetString(result.SignedXml));
    }

    [Fact]
    public async Task Word_facade_returns_only_wordDocs_payload_when_response_has_all_four_arrays()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        server.Configure.MultiArrayResponse = true;
        await using var sp = TestServiceProvider.Build(server.BaseUrl);
        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();
        var result = await client.SignWordAsync(TestPerFormatFixtures.SampleWordRequest(), CancellationToken.None);
        Assert.Equal(server.SignedWordBytes, result.SignedWord);
    }

    [Fact]
    public async Task Excel_facade_returns_only_excelDocs_payload_when_response_has_all_four_arrays()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        server.Configure.MultiArrayResponse = true;
        await using var sp = TestServiceProvider.Build(server.BaseUrl);
        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();
        var result = await client.SignExcelAsync(TestPerFormatFixtures.SampleExcelRequest(), CancellationToken.None);
        Assert.Equal(server.SignedExcelBytes, result.SignedExcel);
    }

    [Fact]
    public async Task Pdf_facade_returns_only_pdfDocs_payload_when_response_has_all_four_arrays()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        server.Configure.MultiArrayResponse = true;
        await using var sp = TestServiceProvider.Build(server.BaseUrl);
        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();
        var result = await client.SignPdfAsync(TestPdfFixture.SampleRequest(), CancellationToken.None);
        Assert.Equal(server.SignedPdfBytes, result.SignedPdf);
    }
}
