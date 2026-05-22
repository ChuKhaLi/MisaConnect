using MisaConnect.ESign.Application.UseCases;
using MisaConnect.ESign.Client.Dtos;
using MisaConnect.ESign.Domain.Signing;

namespace MisaConnect.ESign.Client.Mapping;

internal static class SignWordRequestMapper
{
    public static SignWordWorkRequest ToWorkRequest(SignWordRequestDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var sigInfo = new SignatureInfo(
            SignatureName: dto.SignerName,
            HashAlgorithm: HashAlgorithm.SHA256,
            LogoImage: dto.LogoImageBase64,
            SignatureDescription: new SignatureDescription(
                SignedBy: dto.SignerName,
                Location: dto.Location,
                Reason: dto.Reason,
                Contact: dto.Contact,
                ShowSignedDate: dto.ShowSignedDate,
                DisplayText: dto.DisplayText),
            RenderingMode: dto.RenderingMode ?? 0,
            PositionX: dto.PositionX,
            PositionY: dto.PositionY,
            Width: dto.Width,
            Height: dto.Height,
            Page: dto.Page,
            SignaturePosInfos: dto.AdditionalSignaturePositions?
                .Select(p => new SignaturePosInfo(p.PositionX, p.PositionY, p.Width, p.Height, p.Page))
                .ToList());

        return new SignWordWorkRequest(
            Word: dto.Word ?? Array.Empty<byte>(),
            SignatureInfo: sigInfo,
            DocumentId: string.IsNullOrEmpty(dto.DocumentId) ? Guid.NewGuid().ToString("D") : dto.DocumentId!,
            DocumentName: dto.DocumentName,
            DataToBeDisplayed: dto.DataToBeDisplayed);
    }
}
