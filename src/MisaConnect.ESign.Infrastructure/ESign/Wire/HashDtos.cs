using System.Text.Json.Serialization;

namespace MisaConnect.ESign.Infrastructure.ESign.Wire;

internal sealed class HashRequestDto
{
    [JsonPropertyName("certificate")]
    public string Certificate { get; set; } = string.Empty;

    [JsonPropertyName("certificateChain")]
    public List<string> CertificateChain { get; set; } = new();

    [JsonPropertyName("pdfDocs")]
    public List<PdfDocRequestDto> PdfDocs { get; set; } = new();

    [JsonPropertyName("xmlDocs")]
    public List<XmlHashDocRequestDto> XmlDocs { get; set; } = new();

    [JsonPropertyName("wordDocs")]
    public List<WordHashDocRequestDto> WordDocs { get; set; } = new();

    [JsonPropertyName("excelDocs")]
    public List<ExcelHashDocRequestDto> ExcelDocs { get; set; } = new();
}

internal sealed class PdfDocRequestDto
{
    [JsonPropertyName("DocumentId")]
    public string DocumentId { get; set; } = string.Empty;

    [JsonPropertyName("FileToSign")]
    public string FileToSign { get; set; } = string.Empty;

    [JsonPropertyName("SignatureInfo")]
    public SignatureInfoDto SignatureInfo { get; set; } = new();
}

// MISA §4.1.2 — XML hash request entry. `FileToSign` carries raw XML text.
internal sealed class XmlHashDocRequestDto
{
    [JsonPropertyName("DocumentId")]
    public string DocumentId { get; set; } = string.Empty;

    [JsonPropertyName("FileToSign")]
    public string FileToSign { get; set; } = string.Empty;

    [JsonPropertyName("SignatureInfo")]
    public SignatureInfoDto SignatureInfo { get; set; } = new();
}

// MISA §4.1.1 — Word hash request entry. `FileToSign` carries base64.
internal sealed class WordHashDocRequestDto
{
    [JsonPropertyName("DocumentId")]
    public string DocumentId { get; set; } = string.Empty;

    [JsonPropertyName("FileToSign")]
    public string FileToSign { get; set; } = string.Empty;

    [JsonPropertyName("SignatureInfo")]
    public SignatureInfoDto SignatureInfo { get; set; } = new();
}

// MISA §4.1.1 — Excel hash request entry. `FileToSign` carries base64.
internal sealed class ExcelHashDocRequestDto
{
    [JsonPropertyName("DocumentId")]
    public string DocumentId { get; set; } = string.Empty;

    [JsonPropertyName("FileToSign")]
    public string FileToSign { get; set; } = string.Empty;

    [JsonPropertyName("SignatureInfo")]
    public SignatureInfoDto SignatureInfo { get; set; } = new();
}

internal sealed class SignatureInfoDto
{
    [JsonPropertyName("TextColor")]
    public int? TextColor { get; set; }

    [JsonPropertyName("PositionX")]
    public int? PositionX { get; set; }

    [JsonPropertyName("PositionY")]
    public int? PositionY { get; set; }

    [JsonPropertyName("Width")]
    public int? Width { get; set; }

    [JsonPropertyName("Height")]
    public int? Height { get; set; }

    [JsonPropertyName("FontSize")]
    public int? FontSize { get; set; }

    [JsonPropertyName("FontData")]
    public string? FontData { get; set; }

    [JsonPropertyName("SignatureImage")]
    public string? SignatureImage { get; set; }

    [JsonPropertyName("Page")]
    public int? Page { get; set; }

    [JsonPropertyName("SignatureName")]
    public string SignatureName { get; set; } = string.Empty;

    [JsonPropertyName("HashAlgorithm")]
    public string HashAlgorithm { get; set; } = "SHA256";

    [JsonPropertyName("LogoImage")]
    public string LogoImage { get; set; } = string.Empty;

    [JsonPropertyName("SignatureDescription")]
    public SignatureDescriptionDto SignatureDescription { get; set; } = new();

    [JsonPropertyName("RenderingMode")]
    public int RenderingMode { get; set; }

    [JsonPropertyName("SignaturePosInfos")]
    public List<SignaturePosInfoDto>? SignaturePosInfos { get; set; }
}

internal sealed class SignatureDescriptionDto
{
    [JsonPropertyName("SignedBy")]
    public string SignedBy { get; set; } = string.Empty;

    [JsonPropertyName("ShowSignedDate")]
    public bool? ShowSignedDate { get; set; }

    [JsonPropertyName("Location")]
    public string Location { get; set; } = string.Empty;

    [JsonPropertyName("Reason")]
    public string Reason { get; set; } = string.Empty;

    [JsonPropertyName("Contact")]
    public string Contact { get; set; } = string.Empty;

    [JsonPropertyName("DisplayText")]
    public string? DisplayText { get; set; }
}

// Per wire-envelopes §E4: SignaturePosInfos uses camelCase inner field names.
internal sealed class SignaturePosInfoDto
{
    [JsonPropertyName("positionX")]
    public int PositionX { get; set; }

    [JsonPropertyName("positionY")]
    public int PositionY { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; }
}

internal sealed class HashResponseDto
{
    [JsonPropertyName("pdfDocs")]
    public List<PdfHashOutputDto> PdfDocs { get; set; } = new();

    [JsonPropertyName("xmlDocs")]
    public List<XmlHashOutputDto>? XmlDocs { get; set; }

    [JsonPropertyName("wordDocs")]
    public List<WordHashOutputDto>? WordDocs { get; set; }

    [JsonPropertyName("excelDocs")]
    public List<ExcelHashOutputDto>? ExcelDocs { get; set; }
}

internal sealed class PdfHashOutputDto
{
    [JsonPropertyName("documentId")]
    public string DocumentId { get; set; } = string.Empty;

    [JsonPropertyName("documentBytes")]
    public string DocumentBytes { get; set; } = string.Empty;

    [JsonPropertyName("documentHash")]
    public string DocumentHash { get; set; } = string.Empty;

    [JsonPropertyName("sh")]
    public string Sh { get; set; } = string.Empty;

    [JsonPropertyName("signatureName")]
    public string SignatureName { get; set; } = string.Empty;

    [JsonPropertyName("digest")]
    public string Digest { get; set; } = string.Empty;

    // PDF: not required. Word/Excel/XML: required. Slice 1 forwards through if present.
    [JsonPropertyName("mainDom")]
    public string? MainDom { get; set; }

    [JsonPropertyName("signatureId")]
    public string? SignatureId { get; set; }
}

// MISA §4.15 — XML response entry. Uses `document` (raw text), not `documentBytes`.
internal sealed class XmlHashOutputDto
{
    [JsonPropertyName("documentId")]
    public string DocumentId { get; set; } = string.Empty;

    [JsonPropertyName("document")]
    public string Document { get; set; } = string.Empty;

    [JsonPropertyName("signatureId")]
    public string SignatureId { get; set; } = string.Empty;

    [JsonPropertyName("digest")]
    public string Digest { get; set; } = string.Empty;

    [JsonPropertyName("sh")]
    public string Sh { get; set; } = string.Empty;
}

// MISA §4.15 — Word response entry. Uses `documentBytes` and carries `mainDom`.
internal sealed class WordHashOutputDto
{
    [JsonPropertyName("documentId")]
    public string DocumentId { get; set; } = string.Empty;

    [JsonPropertyName("documentBytes")]
    public string DocumentBytes { get; set; } = string.Empty;

    [JsonPropertyName("signatureId")]
    public string SignatureId { get; set; } = string.Empty;

    [JsonPropertyName("digest")]
    public string Digest { get; set; } = string.Empty;

    [JsonPropertyName("mainDom")]
    public string MainDom { get; set; } = string.Empty;
}

// MISA §4.15 — Excel response entry. Same field set as Word.
internal sealed class ExcelHashOutputDto
{
    [JsonPropertyName("documentId")]
    public string DocumentId { get; set; } = string.Empty;

    [JsonPropertyName("documentBytes")]
    public string DocumentBytes { get; set; } = string.Empty;

    [JsonPropertyName("signatureId")]
    public string SignatureId { get; set; } = string.Empty;

    [JsonPropertyName("digest")]
    public string Digest { get; set; } = string.Empty;

    [JsonPropertyName("mainDom")]
    public string MainDom { get; set; } = string.Empty;
}
