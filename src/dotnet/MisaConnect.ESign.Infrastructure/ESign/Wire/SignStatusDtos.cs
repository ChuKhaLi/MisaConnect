using System.Text.Json.Serialization;

namespace MisaConnect.ESign.Infrastructure.ESign.Wire;

internal sealed class SignStatusResponseDto
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("errorCode")]
    [JsonConverter(typeof(LooseStringConverter))]
    public string? ErrorCode { get; set; }

    [JsonPropertyName("errorDescription")]
    public string? ErrorDescription { get; set; }

    [JsonPropertyName("transactionId")]
    public string? TransactionId { get; set; }

    [JsonPropertyName("signatures")]
    public List<SignStatusSignatureDto>? Signatures { get; set; }
}

internal sealed class SignStatusSignatureDto
{
    [JsonPropertyName("documentId")]
    public string? DocumentId { get; set; }

    [JsonPropertyName("signature")]
    public string? Signature { get; set; }
}
