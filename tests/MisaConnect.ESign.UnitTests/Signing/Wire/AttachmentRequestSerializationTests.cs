using System.Text.Json;
using MisaConnect.ESign.Infrastructure.ESign;
using MisaConnect.ESign.Infrastructure.ESign.Wire;
using Xunit;

namespace MisaConnect.ESign.UnitTests.Signing.Wire;

// Slice 007: a single-format documents/attachment request MUST serialize only the
// document-type array in use; the other three keys MUST be absent. Contracts C1/C2.
public class AttachmentRequestSerializationTests
{
    private static readonly string[] AllArrays = { "pdfDocs", "xmlDocs", "wordDocs", "excelDocs" };

    private static JsonElement Serialize(AttachmentRequestDto body)
    {
        var json = JsonSerializer.Serialize(body, ESignJsonOptions.Wire);
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static void AssertOnlyArrayPresent(JsonElement root, string expected)
    {
        Assert.True(root.TryGetProperty(expected, out _), $"expected '{expected}' to be present");
        foreach (var other in AllArrays)
        {
            if (other == expected) continue;
            Assert.False(root.TryGetProperty(other, out _), $"expected '{other}' to be ABSENT (not empty [])");
        }
        Assert.True(root.TryGetProperty("certificate", out _));
        Assert.True(root.TryGetProperty("certificateChain", out _));
    }

    [Fact]
    public void Pdf_attachment_body_omits_unused_arrays()
    {
        var body = new AttachmentRequestDto
        {
            Certificate = "CERT",
            CertificateChain = new List<string> { "SIGN" },
            PdfDocs = new List<AttachmentPdfDocRequestDto> { new() { DocumentId = "d" } },
        };
        AssertOnlyArrayPresent(Serialize(body), "pdfDocs");
    }

    [Fact]
    public void Xml_attachment_body_omits_unused_arrays()
    {
        var body = new AttachmentRequestDto
        {
            Certificate = "CERT",
            CertificateChain = new List<string> { "SIGN" },
            XmlDocs = new List<XmlAttachmentDocRequestDto> { new() { DocumentId = "d" } },
        };
        AssertOnlyArrayPresent(Serialize(body), "xmlDocs");
    }

    [Fact]
    public void Word_attachment_body_omits_unused_arrays()
    {
        var body = new AttachmentRequestDto
        {
            Certificate = "CERT",
            CertificateChain = new List<string> { "SIGN" },
            WordDocs = new List<WordExcelAttachmentDocRequestDto> { new() { DocumentId = "d" } },
        };
        AssertOnlyArrayPresent(Serialize(body), "wordDocs");
    }

    [Fact]
    public void Excel_attachment_body_omits_unused_arrays()
    {
        var body = new AttachmentRequestDto
        {
            Certificate = "CERT",
            CertificateChain = new List<string> { "SIGN" },
            ExcelDocs = new List<WordExcelAttachmentDocRequestDto> { new() { DocumentId = "d" } },
        };
        AssertOnlyArrayPresent(Serialize(body), "excelDocs");
    }
}
