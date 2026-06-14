using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MisaConnect.ESign.Client;
using MisaConnect.ESign.IntegrationTests.EsignFake;
using Xunit;

namespace MisaConnect.ESign.IntegrationTests.EndToEnd;

// Slice 007: with the fake server's always-on guard rejecting any present-but-empty
// document-type array (HTTP 400, as MISA does), a sign only completes if the SDK
// omits the arrays it isn't using. These tests lock that in end to end. SC-001/SC-002.
public class SignOmitsEmptyDocArraysFakeServerTests
{
    private static readonly string[] AllArrays = { "pdfDocs", "xmlDocs", "wordDocs", "excelDocs" };

    private static void AssertOnlyArrayPresent(string body, string expected)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty(expected, out _), $"expected '{expected}' present in {body}");
        foreach (var other in AllArrays)
        {
            if (other == expected) continue;
            Assert.False(root.TryGetProperty(other, out _), $"expected '{other}' ABSENT in {body}");
        }
    }

    [Fact]
    public async Task Pdf_sign_succeeds_and_request_bodies_omit_unused_arrays()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        await using var sp = TestServiceProvider.Build(server.BaseUrl);

        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();

        var result = await client.SignPdfAsync(TestPdfFixture.SampleRequest(), CancellationToken.None);

        Assert.Equal(server.SignedPdfBytes, result.SignedPdf);
        AssertOnlyArrayPresent(Assert.Single(server.HashRequests).Body, "pdfDocs");
        AssertOnlyArrayPresent(Assert.Single(server.AttachmentRequests).Body, "pdfDocs");
    }

    [Fact]
    public async Task Word_sign_succeeds_and_request_bodies_omit_unused_arrays()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        await using var sp = TestServiceProvider.Build(server.BaseUrl);

        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();

        var result = await client.SignWordAsync(TestPerFormatFixtures.SampleWordRequest(), CancellationToken.None);

        Assert.Equal(server.SignedWordBytes, result.SignedWord);
        AssertOnlyArrayPresent(Assert.Single(server.HashRequests).Body, "wordDocs");
        AssertOnlyArrayPresent(Assert.Single(server.AttachmentRequests).Body, "wordDocs");
    }

    [Fact]
    public async Task Xml_sign_succeeds_and_request_bodies_omit_unused_arrays()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        await using var sp = TestServiceProvider.Build(server.BaseUrl);

        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();

        var result = await client.SignXmlAsync(TestPerFormatFixtures.SampleXmlRequest(), CancellationToken.None);

        Assert.NotNull(result.SignedXml);
        AssertOnlyArrayPresent(Assert.Single(server.HashRequests).Body, "xmlDocs");
        AssertOnlyArrayPresent(Assert.Single(server.AttachmentRequests).Body, "xmlDocs");
    }
}
