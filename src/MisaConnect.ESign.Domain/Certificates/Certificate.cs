namespace MisaConnect.ESign.Domain.Certificates;

public sealed record Certificate(
    string UserId,
    string KeyAlias,
    string AppName,
    KeyStatus KeyStatus,
    string? CertStatus,
    string CertificateValue,
    CertificateChain CertificateChain,
    DateTimeOffset? EffectiveDate,
    DateTimeOffset? ExpirationDate,
    string? EmailName,
    bool IsAutoSign);
