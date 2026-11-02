using System.Text.Json.Serialization;

namespace MisaConnect.ESign.Infrastructure.ESign.Wire;

internal sealed class ResponseErrorDto
{
    // Slice 007: nullable — MISA's 400 body sends "error":null (e.g. the
    // empty-doc-array rejection). A non-nullable bool threw on deserialization, so
    // the whole envelope (errorCode, devMsg, validationFailures) was swallowed and
    // the error surfaced as errorCode=<none>. Nullable lets the body parse.
    [JsonPropertyName("error")]
    public bool? Error { get; set; }

    [JsonPropertyName("errorCode")]
    [JsonConverter(typeof(LooseStringConverter))]
    public string? ErrorCode { get; set; }

    [JsonPropertyName("devMsg")]
    public string? DevMsg { get; set; }

    [JsonPropertyName("userMsg")]
    public string? UserMsg { get; set; }

    // Slice 007: MISA's per-property rejection detail (e.g. each empty doc array).
    // Surfaced in the error detail only when IncludeRawErrorMessage is set; never
    // fed to the error-code synthesis path.
    [JsonPropertyName("validationFailures")]
    public List<ValidationFailureDto>? ValidationFailures { get; set; }
}

internal sealed class ValidationFailureDto
{
    [JsonPropertyName("property")]
    public string? Property { get; set; }

    [JsonPropertyName("failureReason")]
    public string? FailureReason { get; set; }
}
