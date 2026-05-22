using MisaConnect.ESign.Domain.Signing;
using MisaConnect.ESign.Infrastructure.ESign.Wire;

namespace MisaConnect.ESign.Infrastructure.ESign.Mapping;

/// <summary>
/// Maps a slim Domain <see cref="XmlSignatureContext"/> to the wire
/// <see cref="SignatureInfoDto"/>, filling MISA-documented "no signature
/// visualization" defaults for the visual fields the slim context omits.
/// Lives in Infrastructure so the JSON-serialization defaults are not a
/// Domain concern.
/// </summary>
internal static class XmlSignatureContextMapper
{
    public static SignatureInfoDto ToWireSignatureInfo(XmlSignatureContext source) => new()
    {
        SignatureName = source.SignatureName,
        HashAlgorithm = source.HashAlgorithm.ToString(),
        LogoImage = string.Empty,
        RenderingMode = 0,
        SignatureDescription = new SignatureDescriptionDto
        {
            SignedBy = source.SignatureDescription.SignedBy,
            ShowSignedDate = source.SignatureDescription.ShowSignedDate,
            Location = source.SignatureDescription.Location,
            Reason = source.SignatureDescription.Reason,
            Contact = source.SignatureDescription.Contact,
            DisplayText = source.SignatureDescription.DisplayText,
        },
        // Visual fields (TextColor, PositionX/Y, Width, Height, FontSize,
        // FontData, SignatureImage, Page, SignaturePosInfos) are left
        // null/default — JsonIgnore on the wire DTO omits them.
    };
}
