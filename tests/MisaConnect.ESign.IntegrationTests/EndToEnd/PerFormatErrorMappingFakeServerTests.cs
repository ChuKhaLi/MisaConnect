using Microsoft.Extensions.DependencyInjection;
using MisaConnect.ESign.Client;
using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.IntegrationTests.EsignFake;
using Xunit;

namespace MisaConnect.ESign.IntegrationTests.EndToEnd;

public class PerFormatErrorMappingFakeServerTests
{
    [Fact]
    public async Task Xml_hash_rejection_with_malformed_msg_surfaces_InvalidXmlInput_with_xml_format()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        server.Configure.HashRejectionForFormat = (RequestedFormat.Xml, 400, "", "malformed xml input", "u-bad");
        await using var sp = TestServiceProvider.Build(server.BaseUrl);
        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();
        var ex = await Assert.ThrowsAsync<ESignGeneralException>(() =>
            client.SignXmlAsync(TestPerFormatFixtures.SampleXmlRequest(), CancellationToken.None));
        Assert.Equal("InvalidXmlInput", ex.RawCode);
        Assert.Equal(DocumentFormat.Xml, ex.Format);
        Assert.Equal(ESignErrorCategory.HashRejected, ex.Category);
    }

    [Fact]
    public async Task Excel_attachment_rejection_with_main_dom_msg_surfaces_MissingMainDom_with_excel_format()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        server.Configure.AttachmentRejectionForFormat = (RequestedFormat.Excel, 400, "", "missing mainDom on excel attachment", "u-bad");
        await using var sp = TestServiceProvider.Build(server.BaseUrl);
        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();
        var ex = await Assert.ThrowsAsync<ESignGeneralException>(() =>
            client.SignExcelAsync(TestPerFormatFixtures.SampleExcelRequest(), CancellationToken.None));
        Assert.Equal("MissingMainDom", ex.RawCode);
        Assert.Equal(DocumentFormat.Excel, ex.Format);
    }

    [Fact]
    public async Task Xml_attachment_rejection_with_signature_id_msg_surfaces_MissingSignatureId_with_xml_format()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        server.Configure.AttachmentRejectionForFormat = (RequestedFormat.Xml, 400, "", "signatureId is missing", "u-bad");
        await using var sp = TestServiceProvider.Build(server.BaseUrl);
        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();
        var ex = await Assert.ThrowsAsync<ESignGeneralException>(() =>
            client.SignXmlAsync(TestPerFormatFixtures.SampleXmlRequest(), CancellationToken.None));
        Assert.Equal("MissingSignatureId", ex.RawCode);
        Assert.Equal(DocumentFormat.Xml, ex.Format);
    }

    [Fact]
    public async Task Word_attachment_rejection_carries_word_format_and_correlation_id_on_typed_exception()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        server.Configure.AttachmentRejectionForFormat = (RequestedFormat.Word, 400, "ResultBad", "something broke", "u-bad");
        await using var sp = TestServiceProvider.Build(server.BaseUrl);
        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();
        var ex = await Assert.ThrowsAsync<ESignGeneralException>(() =>
            client.SignWordAsync(TestPerFormatFixtures.SampleWordRequest(), CancellationToken.None));
        Assert.Equal(DocumentFormat.Word, ex.Format);
        Assert.False(string.IsNullOrEmpty(ex.CorrelationId));
    }
}
