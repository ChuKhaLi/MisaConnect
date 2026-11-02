namespace MisaConnect.ESign.Domain.Signing;

/// <summary>
/// Slim signing-context record for XML (XAdES) signing. Exposes ONLY the
/// XAdES-meaningful fields — visual-positioning fields are intentionally
/// absent so the type system prevents consumers from passing them to a
/// format that has no visual positioning.
/// </summary>
public sealed record XmlSignatureContext(
    string SignatureName,
    HashAlgorithm HashAlgorithm,
    SignatureDescription SignatureDescription);
