namespace MisaConnect.ESign.Client.Dtos;

public sealed record SignExcelRequestDto(
    byte[] Excel,
    string DocumentName,
    string SignerName,
    string Location,
    string Reason,
    string Contact,
    string LogoImageBase64,
    string DataToBeDisplayed,
    int? Page = null,
    int? PositionX = null,
    int? PositionY = null,
    int? Width = null,
    int? Height = null,
    int? RenderingMode = null,
    string? DocumentId = null,
    string? DisplayText = null,
    bool ShowSignedDate = true,
    IReadOnlyList<SignaturePosInfoDto>? AdditionalSignaturePositions = null);
