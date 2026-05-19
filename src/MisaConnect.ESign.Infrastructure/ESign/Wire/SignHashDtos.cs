using System.Text.Json.Serialization;

namespace MisaConnect.ESign.Infrastructure.ESign.Wire;

internal sealed class SignHashRequestDto
{
    [JsonPropertyName("DataToBeDisplayed")]
    public string DataToBeDisplayed { get; set; } = string.Empty;

    [JsonPropertyName("UserId")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("CertAlias")]
    public string CertAlias { get; set; } = string.Empty;

    [JsonPropertyName("Documents")]
    public List<SignHashDocumentDto> Documents { get; set; } = new();
}

internal sealed class SignHashDocumentDto
{
    [JsonPropertyName("DocumentId")]
    public string DocumentId { get; set; } = string.Empty;

    [JsonPropertyName("FileToSign")]
    public string FileToSign { get; set; } = string.Empty;

    [JsonPropertyName("DocumentName")]
    public string DocumentName { get; set; } = string.Empty;
}

internal sealed class SignHashResponseDto
{
    [JsonPropertyName("transactionId")]
    public string? TransactionId { get; set; }
}
