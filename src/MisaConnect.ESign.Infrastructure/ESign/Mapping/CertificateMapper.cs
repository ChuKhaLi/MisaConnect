using MisaConnect.ESign.Domain.Certificates;
using DomainCert = MisaConnect.ESign.Domain.Certificates.Certificate;
using WireCertDto = MisaConnect.ESign.Infrastructure.ESign.Wire.CertificateDto;

namespace MisaConnect.ESign.Infrastructure.ESign.Mapping;

internal static class CertificateMapper
{
    public static IReadOnlyList<DomainCert> ToDomain(IReadOnlyList<WireCertDto> dtos)
    {
        var result = new List<DomainCert>(dtos.Count);
        foreach (var dto in dtos)
        {
            result.Add(ToDomain(dto));
        }
        return result;
    }

    private static DomainCert ToDomain(WireCertDto dto)
    {
        var chainList = dto.CertificateChain ?? new List<string>();
        if (chainList.Count != 3)
        {
            throw new InvalidCertChainLengthException(chainList.Count);
        }

        var keyStatus = ParseKeyStatus(dto.KeyStatus);

        return new DomainCert(
            UserId: dto.UserId ?? string.Empty,
            KeyAlias: dto.KeyAlias ?? string.Empty,
            AppName: dto.AppName ?? string.Empty,
            KeyStatus: keyStatus,
            CertStatus: dto.CertStatus,
            CertificateValue: dto.Certificate ?? string.Empty,
            CertificateChain: new CertificateChain(chainList[0], chainList[1], chainList[2]),
            EffectiveDate: dto.EffectiveDate,
            ExpirationDate: dto.ExpirationDate,
            EmailName: dto.EmailName,
            IsAutoSign: dto.IsAutoSign);
    }

    private static KeyStatus ParseKeyStatus(string? raw) =>
        string.Equals(raw, "ACTIVE", StringComparison.OrdinalIgnoreCase)
            ? KeyStatus.ACTIVE
            : KeyStatus.INACTIVE;
}

/// <summary>
/// Internal sentinel — the wire client catches this and surfaces an
/// <c>ESignException(MisaUnknown, "InvalidCertChainLength")</c> with the
/// current correlation ID so the Domain layer stays correlation-aware.
/// </summary>
internal sealed class InvalidCertChainLengthException : Exception
{
    public InvalidCertChainLengthException(int actual)
        : base($"Certificate chain length expected 3 but was {actual}.")
    {
        ActualLength = actual;
    }

    public int ActualLength { get; }
}
