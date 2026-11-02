using System.Text;
using MisaConnect.ESign.Application.UseCases;
using MisaConnect.ESign.Client.Dtos;
using MisaConnect.ESign.Domain.Signing;

namespace MisaConnect.ESign.Client.Mapping;

internal static class SignXmlRequestMapper
{
    public static SignXmlWorkRequest ToWorkRequest(SignXmlRequestDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var hasString = dto.Xml is not null;
        var hasBytes = dto.XmlUtf8Bytes is not null;
        if (hasString == hasBytes)
        {
            throw new ArgumentException(
                "SignXmlRequestDto must have exactly one of Xml or XmlUtf8Bytes set (not both, not neither).",
                nameof(dto));
        }

        var xmlContent = hasString
            ? dto.Xml!
            : Encoding.UTF8.GetString(dto.XmlUtf8Bytes!);

        var hashAlgo = ParseHashAlgorithm(dto.SignatureContext?.HashAlgorithm);

        ArgumentNullException.ThrowIfNull(dto.SignatureContext);
        ArgumentNullException.ThrowIfNull(dto.SignatureContext.SignatureDescription);

        var ctx = new XmlSignatureContext(
            SignatureName: dto.SignatureContext.SignatureName,
            HashAlgorithm: hashAlgo,
            SignatureDescription: new SignatureDescription(
                SignedBy: dto.SignatureContext.SignatureDescription.SignedBy,
                Location: dto.SignatureContext.SignatureDescription.Location,
                Reason: dto.SignatureContext.SignatureDescription.Reason,
                Contact: dto.SignatureContext.SignatureDescription.Contact,
                ShowSignedDate: dto.SignatureContext.SignatureDescription.ShowSignedDate,
                DisplayText: dto.SignatureContext.SignatureDescription.DisplayText));

        return new SignXmlWorkRequest(
            Xml: xmlContent,
            SignatureContext: ctx,
            DocumentId: string.IsNullOrEmpty(dto.DocumentId) ? Guid.NewGuid().ToString("D") : dto.DocumentId!,
            DocumentName: dto.DocumentName,
            DataToBeDisplayed: dto.DataToBeDisplayed);
    }

    private static HashAlgorithm ParseHashAlgorithm(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return HashAlgorithm.SHA256;
        if (Enum.TryParse<HashAlgorithm>(raw, ignoreCase: true, out var parsed)) return parsed;
        return HashAlgorithm.SHA256;
    }
}
