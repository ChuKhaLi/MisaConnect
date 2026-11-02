namespace MisaConnect.ESign.Client.Dtos;

public sealed record SignatureDescriptionDto(
    string SignedBy,
    string Location,
    string Reason,
    string Contact,
    bool? ShowSignedDate = null,
    string? DisplayText = null);
