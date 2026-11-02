namespace MisaConnect.ESign.Application.Abstractions;

/// <summary>
/// Lightweight Application-layer input record for
/// <see cref="MisaConnect.ESign.Application.UseCases.SubmitSignHash"/>.
/// Carries the two MISA <c>/Signing/hash</c> request fields that vary per
/// document (everything else on the wire is supplied by the wire client
/// from the certificate and the work request). Format-agnostic by
/// construction — the same record carries PDF, XML, Word, and Excel
/// digests, so the slice-1 wire shape on <c>/Signing/hash</c> stays
/// byte-identical (FR-065).
/// </summary>
public sealed record SignHashInput(string DocumentId, string Digest);
