namespace MisaConnect.ESign.Client.Dtos;

public sealed record XmlSignatureContextDto(
    string SignatureName,
    string HashAlgorithm,
    SignatureDescriptionDto SignatureDescription);
