using System.Text.Json.Serialization;

namespace MisaConnect.ESign.Infrastructure.ESign.Wire;

internal sealed class AttachmentRequestDto
{
    [JsonPropertyName("certificate")]
    public string Certificate { get; set; } = string.Empty;

    [JsonPropertyName("certificateChain")]
    public List<string> CertificateChain { get; set; } = new();

    [JsonPropertyName("pdfDocs")]
    public List<AttachmentPdfDocRequestDto> PdfDocs { get; set; } = new();

    [JsonPropertyName("xmlDocs")]
    public List<XmlAttachmentDocRequestDto> XmlDocs { get; set; } = new();

    [JsonPropertyName("wordDocs")]
    public List<WordExcelAttachmentDocRequestDto> WordDocs { get; set; } = new();

    [JsonPropertyName("excelDocs")]
    public List<WordExcelAttachmentDocRequestDto> ExcelDocs { get; set; } = new();
}

internal sealed class AttachmentPdfDocRequestDto
{
    [JsonPropertyName("signature")]
    public string Signature { get; set; } = string.Empty;

    [JsonPropertyName("documentId")]
    public string DocumentId { get; set; } = string.Empty;

    [JsonPropertyName("documentBytes")]
    public string DocumentBytes { get; set; } = string.Empty;

    [JsonPropertyName("digest")]
    public string Digest { get; set; } = string.Empty;

    // PDF: not required; omitted when null.
    [JsonPropertyName("mainDom")]
    public string? MainDom { get; set; }

    [JsonPropertyName("signatureName")]
    public string SignatureName { get; set; } = string.Empty;

    [JsonPropertyName("sh")]
    public string Sh { get; set; } = string.Empty;

    // PDF: not required; omitted when null.
    [JsonPropertyName("signatureId")]
    public string? SignatureId { get; set; }

    [JsonPropertyName("documentHash")]
    public string DocumentHash { get; set; } = string.Empty;
}

// MISA §4.6 — XML Doc_Attackment row. Carries `signatureId`; no `mainDom`.
internal sealed class XmlAttachmentDocRequestDto
{
    [JsonPropertyName("signature")]
    public string Signature { get; set; } = string.Empty;

    [JsonPropertyName("documentId")]
    public string DocumentId { get; set; } = string.Empty;

    [JsonPropertyName("documentBytes")]
    public string DocumentBytes { get; set; } = string.Empty;

    [JsonPropertyName("digest")]
    public string Digest { get; set; } = string.Empty;

    [JsonPropertyName("signatureName")]
    public string SignatureName { get; set; } = string.Empty;

    [JsonPropertyName("sh")]
    public string Sh { get; set; } = string.Empty;

    [JsonPropertyName("signatureId")]
    public string SignatureId { get; set; } = string.Empty;

    [JsonPropertyName("documentHash")]
    public string DocumentHash { get; set; } = string.Empty;
}

// MISA §4.6 — Word/Excel Doc_Attackment row. Carries both `mainDom` and `signatureId`.
internal sealed class WordExcelAttachmentDocRequestDto
{
    [JsonPropertyName("signature")]
    public string Signature { get; set; } = string.Empty;

    [JsonPropertyName("documentId")]
    public string DocumentId { get; set; } = string.Empty;

    [JsonPropertyName("documentBytes")]
    public string DocumentBytes { get; set; } = string.Empty;

    [JsonPropertyName("digest")]
    public string Digest { get; set; } = string.Empty;

    [JsonPropertyName("mainDom")]
    public string MainDom { get; set; } = string.Empty;

    [JsonPropertyName("signatureName")]
    public string SignatureName { get; set; } = string.Empty;

    [JsonPropertyName("sh")]
    public string Sh { get; set; } = string.Empty;

    [JsonPropertyName("signatureId")]
    public string SignatureId { get; set; } = string.Empty;

    [JsonPropertyName("documentHash")]
    public string DocumentHash { get; set; } = string.Empty;
}

internal sealed class AttachmentResponseDto
{
    [JsonPropertyName("pdfDocs")]
    public List<AttachmentPdfDocResponseDto>? PdfDocs { get; set; }

    [JsonPropertyName("xmlDocs")]
    public List<XmlAttachmentDocResponseDto>? XmlDocs { get; set; }

    [JsonPropertyName("wordDocs")]
    public List<WordExcelAttachmentDocResponseDto>? WordDocs { get; set; }

    [JsonPropertyName("excelDocs")]
    public List<WordExcelAttachmentDocResponseDto>? ExcelDocs { get; set; }
}

internal sealed class AttachmentPdfDocResponseDto
{
    [JsonPropertyName("documentId")]
    public string? DocumentId { get; set; }

    [JsonPropertyName("document")]
    public string? Document { get; set; }
}

internal sealed class XmlAttachmentDocResponseDto
{
    [JsonPropertyName("documentId")]
    public string? DocumentId { get; set; }

    // Signed XML text (UTF-8).
    [JsonPropertyName("document")]
    public string? Document { get; set; }
}

internal sealed class WordExcelAttachmentDocResponseDto
{
    [JsonPropertyName("documentId")]
    public string? DocumentId { get; set; }

    // Signed Word/Excel bytes — base64.
    [JsonPropertyName("document")]
    public string? Document { get; set; }
}
