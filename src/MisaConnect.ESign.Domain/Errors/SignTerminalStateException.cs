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
        Exception? inner = null)
        : base(CategoryFromStatus(terminalStatus), rawCode, detail, correlationId, inner)
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
