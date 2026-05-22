namespace MisaConnect.ESign.Client.Dtos;

/// <summary>
/// Consumer-facing XML signing request. Exactly one of <see cref="Xml"/> or
/// <see cref="XmlUtf8Bytes"/> MUST be non-null; the
/// <c>SignXmlRequest.FromString(...)</c> / <c>FromUtf8Bytes(...)</c>
/// factories on <see cref="SignXmlRequest"/> are the recommended construction
/// helpers.
/// </summary>
public sealed record SignXmlRequestDto(
    string? Xml,
    byte[]? XmlUtf8Bytes,
    XmlSignatureContextDto SignatureContext,
    string DocumentName,
    string DataToBeDisplayed,
    string? DocumentId = null);

public static class SignXmlRequest
{
    public static SignXmlRequestDto FromString(
        string xml,
        XmlSignatureContextDto signatureContext,
        string documentName,
        string dataToBeDisplayed,
        string? documentId = null) =>
        new(xml, null, signatureContext, documentName, dataToBeDisplayed, documentId);

    public static SignXmlRequestDto FromUtf8Bytes(
        byte[] xmlUtf8Bytes,
        XmlSignatureContextDto signatureContext,
        string documentName,
        string dataToBeDisplayed,
        string? documentId = null) =>
        new(null, xmlUtf8Bytes, signatureContext, documentName, dataToBeDisplayed, documentId);
}
