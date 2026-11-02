namespace MisaConnect.ESign.Domain.Signing;

public sealed record SignatureDescription(
    string SignedBy,
    string Location,
    string Reason,
    string Contact,
    bool? ShowSignedDate = null,
    string? DisplayText = null);
