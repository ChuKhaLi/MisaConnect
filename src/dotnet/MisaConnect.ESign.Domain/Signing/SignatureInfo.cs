namespace MisaConnect.ESign.Domain.Signing;

public sealed record SignatureInfo(
    string SignatureName,
    HashAlgorithm HashAlgorithm,
    string LogoImage,
    SignatureDescription SignatureDescription,
    int RenderingMode,
    int? TextColor = null,
    int? PositionX = null,
    int? PositionY = null,
    int? Width = null,
    int? Height = null,
    int? FontSize = null,
    string? FontData = null,
    string? SignatureImage = null,
    int? Page = null,
    IReadOnlyList<SignaturePosInfo>? SignaturePosInfos = null);
