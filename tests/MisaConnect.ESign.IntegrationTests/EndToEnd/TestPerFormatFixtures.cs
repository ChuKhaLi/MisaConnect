using MisaConnect.ESign.Client.Dtos;

namespace MisaConnect.ESign.IntegrationTests.EndToEnd;

internal static class TestPerFormatFixtures
{
    public static byte[] SampleXmlBytes() =>
        System.Text.Encoding.UTF8.GetBytes("<root><child>hello</child></root>");

    public static byte[] SampleWordBytes()
    {
        // Minimal OOXML zip header (`PK\x03\x04`) plus padding — not a valid
        // docx; the fake server doesn't parse the payload.
        var hdr = new byte[] { 0x50, 0x4B, 0x03, 0x04 };
        var pad = new byte[256];
        return hdr.Concat(pad).ToArray();
    }

    public static byte[] SampleExcelBytes()
    {
        var hdr = new byte[] { 0x50, 0x4B, 0x03, 0x04 };
        var pad = new byte[256];
        return hdr.Concat(pad).ToArray();
    }

    public static SignXmlRequestDto SampleXmlRequest() =>
        SignXmlRequest.FromString(
            xml: "<root><child>hello</child></root>",
            signatureContext: new XmlSignatureContextDto(
                SignatureName: "XmlSig1",
                HashAlgorithm: "SHA256",
                SignatureDescription: new SignatureDescriptionDto(
                    SignedBy: "Alice",
                    Location: "Hanoi",
                    Reason: "Test",
                    Contact: "alice@example.com",
                    ShowSignedDate: true)),
            documentName: "test.xml",
            dataToBeDisplayed: "<p>Confirm sign</p>");

    public static SignXmlRequestDto SampleXmlRequestFromBytes() =>
        SignXmlRequest.FromUtf8Bytes(
            xmlUtf8Bytes: SampleXmlBytes(),
            signatureContext: new XmlSignatureContextDto(
                SignatureName: "XmlSig1",
                HashAlgorithm: "SHA256",
                SignatureDescription: new SignatureDescriptionDto(
                    SignedBy: "Alice",
                    Location: "Hanoi",
                    Reason: "Test",
                    Contact: "alice@example.com",
                    ShowSignedDate: true)),
            documentName: "test.xml",
            dataToBeDisplayed: "<p>Confirm sign</p>");

    public static SignWordRequestDto SampleWordRequest() => new(
        Word: SampleWordBytes(),
        DocumentName: "test.docx",
        SignerName: "Alice",
        Location: "Hanoi",
        Reason: "Test",
        Contact: "alice@example.com",
        LogoImageBase64: "base64-logo",
        DataToBeDisplayed: "<p>Confirm sign</p>",
        Page: 1,
        PositionX: 100, PositionY: 100, Width: 200, Height: 80,
        RenderingMode: 1);

    public static SignExcelRequestDto SampleExcelRequest() => new(
        Excel: SampleExcelBytes(),
        DocumentName: "test.xlsx",
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
