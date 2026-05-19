namespace MisaConnect.ESign.Domain.Errors;

public enum ESignErrorCategory
{
    MisaUnknown = 0,
    Authentication = 1,
    NoActiveCertificate = 2,
    CertificateLookupFailed = 3,
    HashRejected = 4,
    SignRejected = 5,
    SignTerminalFailed = 6,
    SignTerminalCancelled = 7,
    SignTerminalUnknown = 8,
    SignTimeout = 9,
    AttachmentRejected = 10,
    StatusLookupFailed = 11,
    Transport = 12,
    Validation = 13,
}
