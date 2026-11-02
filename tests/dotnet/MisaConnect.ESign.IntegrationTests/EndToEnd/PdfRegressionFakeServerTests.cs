using Microsoft.Extensions.DependencyInjection;
using MisaConnect.ESign.Client;
using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.IntegrationTests.EsignFake;
using Xunit;

namespace MisaConnect.ESign.IntegrationTests.EndToEnd;

/// <summary>
/// FR-065 / SC-021 regression gate: after slice 3 ships, the slice-1 PDF
/// happy-path must continue to produce byte-identical wire shapes and a
/// signed PDF result tagged with <see cref="DocumentFormat.Pdf"/>.
/// </summary>
public class PdfRegressionFakeServerTests
{
    [Fact]
    public async Task Pdf_happy_path_still_returns_signed_bytes_and_uses_pdfDocs_only()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        await using var sp = TestServiceProvider.Build(server.BaseUrl);
        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();

        var result = await client.SignPdfAsync(TestPdfFixture.SampleRequest(), CancellationToken.None);

        Assert.Equal(server.SignedPdfBytes, result.SignedPdf);
        Assert.Equal(DocumentFormat.Pdf, result.Format);
        Assert.Equal("tx-fake-1", result.TransactionId);

        // FR-065: the wire shape on /documents/hash and /documents/attachment
        // populates only the pdfDocs array.
        var hash = Assert.Single(server.HashRequests);
        Assert.Equal(RequestedFormat.Pdf, hash.DetectedFormat);
        var attach = Assert.Single(server.AttachmentRequests);
        Assert.Equal(RequestedFormat.Pdf, attach.DetectedFormat);

        // The pdfDocs request entry carries the slice-1 fields (DocumentId,
        // FileToSign base64, SignatureInfo) verbatim.
        Assert.Contains("\"pdfDocs\"", hash.Body);
        Assert.Contains("\"DocumentId\"", hash.Body);
        Assert.Contains("\"FileToSign\"", hash.Body);
        Assert.Contains("\"SignatureInfo\"", hash.Body);
    }
}
