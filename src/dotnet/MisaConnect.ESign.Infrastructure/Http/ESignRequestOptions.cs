namespace MisaConnect.ESign.Infrastructure.Http;

/// <summary>
/// Keys for per-request flags read by the HTTP handler pipeline.
/// </summary>
internal static class ESignRequestOptions
{
    /// <summary>
    /// When true, the request may have caused a side effect at MISA once sent, so
    /// <see cref="TransientFailureRetryHandler"/> retries it only when it provably
    /// never reached the server (429, DNS failure, connection failure).
    /// </summary>
    public static readonly HttpRequestOptionsKey<bool> NotRetryableAfterSend = new("MisaConnect.ESign.NotRetryableAfterSend");
}
