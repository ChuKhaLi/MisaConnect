namespace MisaConnect.ESign.Infrastructure.ESign;

internal static class ESignHttpRoutes
{
    public const string AuthLoginApi = "api/auth/api/v1/auth/login-api";
    public const string AuthRefreshToken = "webdev/api/auth/api/v1/auth/refreshtoken";
    public const string CertificatesByUserId = "external/esrm/service/general/api/v1/Certificates/by-userId";
    public const string DocumentsHash = "external/esrm/service/document/api/v1/documents/hash";
    public const string SigningHash = "external/esrm/service/signing/api/v1/Signing/hash";
    public const string SigningStatus = "external/esrm/service/signing/api/v1/Signing/status";
    public const string DocumentsAttachment = "external/esrm/service/document/api/v1/documents/attachment";
}
