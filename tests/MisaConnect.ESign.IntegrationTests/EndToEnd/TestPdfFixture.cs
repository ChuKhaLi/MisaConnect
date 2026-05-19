using MisaConnect.ESign.Client.Dtos;

namespace MisaConnect.ESign.IntegrationTests.EndToEnd;

internal static class TestPdfFixture
{
    public static byte[] OnePagePdfBytes()
    {
        var hdr = "%PDF-1.4\n%\xE2\xE3\xCF\xD3\n";
        var bytes = System.Text.Encoding.ASCII.GetBytes(hdr);
        var pad = new byte[256];
        return bytes.Concat(pad).ToArray();
    }

    public static SignPdfRequestDto SampleRequest(byte[]? pdfOverride = null) => new(
        Pdf: pdfOverride ?? OnePagePdfBytes(),
        DocumentName: "test.pdf",
        SignerName: "Alice",
        Location: "Hanoi",
        Reason: "Test",
        Contact: "alice@example.com",
        LogoImageBase64: "base64-logo",
        DataToBeDisplayed: "<p>Confirm sign</p>",
        Page: 1,
        PositionX: 100, PositionY: 100, Width: 200, Height: 80,
        RenderingMode: 1);
}
