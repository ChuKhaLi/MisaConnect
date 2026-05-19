using MisaConnect.ESign.Client.Dtos;

namespace MisaConnect.ESign.Client;

public interface IMisaESignClient
{
    /// <summary>
    /// Sign a PDF end-to-end via MISA eSign RemoteSigning. Orchestrates login
    /// (or cached-token reuse), certificate selection, server-side hashing,
    /// signing, status polling, and signature attachment. Returns the signed
    /// PDF bytes.
    /// </summary>
    Task<SignPdfResultDto> SignPdfAsync(SignPdfRequestDto request, CancellationToken ct = default);
}
