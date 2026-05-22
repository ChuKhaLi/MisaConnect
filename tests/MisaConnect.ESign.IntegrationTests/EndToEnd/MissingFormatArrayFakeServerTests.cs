using Microsoft.Extensions.DependencyInjection;
using MisaConnect.ESign.Client;
using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.IntegrationTests.EsignFake;
using Xunit;

namespace MisaConnect.ESign.IntegrationTests.EndToEnd;

public class MissingFormatArrayFakeServerTests
{
    [Fact]
    public async Task Xml_facade_raises_typed_MissingSignedDocument_when_xmlDocs_empty()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        server.Configure.AttachmentMissingFormatArrayFor = RequestedFormat.Xml;
        await using var sp = TestServiceProvider.Build(server.BaseUrl);
        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();
        var ex = await Assert.ThrowsAsync<ESignGeneralException>(() =>
            client.SignXmlAsync(TestPerFormatFixtures.SampleXmlRequest(), CancellationToken.None));
        Assert.Equal("MissingSignedDocument", ex.RawCode);
        Assert.Equal(DocumentFormat.Xml, ex.Format);
    }

    [Fact]
    public async Task Word_facade_raises_typed_MissingSignedDocument_when_wordDocs_empty()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        server.Configure.AttachmentMissingFormatArrayFor = RequestedFormat.Word;
        await using var sp = TestServiceProvider.Build(server.BaseUrl);
        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();
        var ex = await Assert.ThrowsAsync<ESignGeneralException>(() =>
            client.SignWordAsync(TestPerFormatFixtures.SampleWordRequest(), CancellationToken.None));
        Assert.Equal("MissingSignedDocument", ex.RawCode);
        Assert.Equal(DocumentFormat.Word, ex.Format);
    }

    [Fact]
    public async Task Excel_facade_raises_typed_MissingSignedDocument_when_excelDocs_empty()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        server.Configure.AttachmentMissingFormatArrayFor = RequestedFormat.Excel;
        await using var sp = TestServiceProvider.Build(server.BaseUrl);
        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();
        var ex = await Assert.ThrowsAsync<ESignGeneralException>(() =>
            client.SignExcelAsync(TestPerFormatFixtures.SampleExcelRequest(), CancellationToken.None));
        Assert.Equal("MissingSignedDocument", ex.RawCode);
        Assert.Equal(DocumentFormat.Excel, ex.Format);
    }
}
