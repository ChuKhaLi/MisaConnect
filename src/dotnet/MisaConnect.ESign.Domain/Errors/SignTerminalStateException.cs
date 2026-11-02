using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.Domain.Signing;

namespace MisaConnect.ESign.Domain.Errors;

public sealed class SignTerminalStateException : ESignException
{
    public SignTerminalStateException(
        SignStatus terminalStatus,
        string transactionId,
        string? rawCode,
        string detail,
        string correlationId,
        Exception? inner = null,
        DocumentFormat format = DocumentFormat.Unknown)
        : base(CategoryFromStatus(terminalStatus), rawCode, detail, correlationId, inner, format)
    {
        TerminalStatus = terminalStatus;
        TransactionId = transactionId;
    }

    public SignStatus TerminalStatus { get; }
    public string TransactionId { get; }

    private static ESignErrorCategory CategoryFromStatus(SignStatus status) => status switch
    {
        SignStatus.FAILED => ESignErrorCategory.SignTerminalFailed,
        SignStatus.CANCELLED => ESignErrorCategory.SignTerminalCancelled,
        _ => ESignErrorCategory.SignTerminalUnknown,
    };
}
