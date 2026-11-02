namespace MisaConnect.ESign.Client.Dtos;

public sealed record CertificateDto(
    string KeyAlias,
    string UserId,
    string AppName,
    string KeyStatus,
    DateTimeOffset? EffectiveDate,
    DateTimeOffset? ExpirationDate,
    bool IsAutoSign);
