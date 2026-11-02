using System.Text.Json;
using MisaConnect.ESign.Infrastructure.ESign;
using MisaConnect.ESign.Infrastructure.ESign.Wire;
using Xunit;

namespace MisaConnect.ESign.UnitTests.Signing.Wire;

// Slice 007: a single-format documents/hash request MUST serialize only the
// document-type array in use; the other three keys MUST be absent (not empty []),
// which MISA's ESRM endpoint rejected with HTTP 400. Contracts C1/C2.
public class HashRequestSerializationTests
{
    private static readonly string[] AllArrays = { "pdfDocs", "xmlDocs", "wordDocs", "excelDocs" };

    private static JsonElement Serialize(HashRequestDto body)
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
        // Always-present fields are unaffected (Contract C2).
        Assert.True(root.TryGetProperty("certificate", out _));
        Assert.True(root.TryGetProperty("certificateChain", out _));
    }

    [Fact]
    public void Pdf_hash_body_omits_unused_arrays()
    {
        var body = new HashRequestDto
        {
            Certificate = "CERT",
            CertificateChain = new List<string> { "SIGN" },
            PdfDocs = new List<PdfDocRequestDto> { new() { DocumentId = "d", FileToSign = "f" } },
        };
        AssertOnlyArrayPresent(Serialize(body), "pdfDocs");
    }

    [Fact]
    public void Xml_hash_body_omits_unused_arrays()
    {
        var body = new HashRequestDto
        {
            Certificate = "CERT",
            CertificateChain = new List<string> { "SIGN" },
            XmlDocs = new List<XmlHashDocRequestDto> { new() { DocumentId = "d", FileToSign = "<x/>" } },
        };
        AssertOnlyArrayPresent(Serialize(body), "xmlDocs");
    }

    [Fact]
    public void Word_hash_body_omits_unused_arrays()
    {
        var body = new HashRequestDto
        {
            Certificate = "CERT",
            CertificateChain = new List<string> { "SIGN" },
            WordDocs = new List<WordHashDocRequestDto> { new() { DocumentId = "d", FileToSign = "f" } },
        };
        AssertOnlyArrayPresent(Serialize(body), "wordDocs");
    }

    [Fact]
    public void Excel_hash_body_omits_unused_arrays()
    {
        var body = new HashRequestDto
        {
            Certificate = "CERT",
            CertificateChain = new List<string> { "SIGN" },
            ExcelDocs = new List<ExcelHashDocRequestDto> { new() { DocumentId = "d", FileToSign = "f" } },
        };
        AssertOnlyArrayPresent(Serialize(body), "excelDocs");
    }
}
