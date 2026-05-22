using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.Domain.Signing;

namespace MisaConnect.ESign.Application.UseCases;

public sealed class PollSignStatus
{
    private readonly IMisaESignWireClient _wire;
    private readonly IDelayer _delayer;
    private readonly ISystemClock _clock;
    private readonly ICorrelationIdAccessor _correlation;

    public PollSignStatus(
        IMisaESignWireClient wire,
        IDelayer delayer,
        ISystemClock clock,
        ICorrelationIdAccessor correlation)
    {
        _wire = wire;
        _delayer = delayer;
        _clock = clock;
        _correlation = correlation;
    }

    public async Task<SignStatusSnapshot> ExecuteAsync(
        string accessToken,
        string transactionId,
        TimeSpan interval,
        TimeSpan totalTimeout,
        CancellationToken ct,
        DocumentFormat format = DocumentFormat.Pdf)
    {
        var start = _clock.UtcNow;
        var deadline = start + totalTimeout;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var snapshot = await _wire.GetSignStatusAsync(accessToken, transactionId, ct).ConfigureAwait(false);

            switch (snapshot.Status)
            {
                case SignStatus.SUCCESS:
                    return snapshot;

                case SignStatus.FAILED:
                case SignStatus.CANCELLED:
                    throw new SignTerminalStateException(
                        terminalStatus: snapshot.Status,
                        transactionId: transactionId,
                        rawCode: snapshot.ErrorCode,
                        detail: BuildTerminalDetail(snapshot),
                        correlationId: _correlation.Current,
                        format: format);

                case SignStatus.UNKNOWN:
                    throw new SignTerminalStateException(
                        terminalStatus: SignStatus.UNKNOWN,
                        transactionId: transactionId,
                        rawCode: snapshot.ErrorCode ?? "UnknownStatus",
                        detail: BuildTerminalDetail(snapshot),
                        correlationId: _correlation.Current,
                        format: format);

                case SignStatus.PENDING:
                default:
                    break;
            }

            var now = _clock.UtcNow;
            if (now >= deadline)
            {
                throw new SignTimeoutException(
                    transactionId: transactionId,
                    elapsedTime: now - start,
                    detail: $"Polling exceeded total timeout of {totalTimeout} on transactionId={transactionId}.",
                    correlationId: _correlation.Current,
                    format: format);
            }

            var remainingToDeadline = deadline - now;
            var nextInterval = interval < remainingToDeadline ? interval : remainingToDeadline;
            await _delayer.DelayAsync(nextInterval, ct).ConfigureAwait(false);
        }
    }

    private static string BuildTerminalDetail(SignStatusSnapshot snapshot)
    {
        var status = snapshot.Status.ToString();
        if (!string.IsNullOrEmpty(snapshot.ErrorDescription))
        {
            return $"Signing/status terminal state {status}: {snapshot.ErrorDescription}";
        }
        if (!string.IsNullOrEmpty(snapshot.ErrorCode))
        {
            return $"Signing/status terminal state {status} (errorCode={snapshot.ErrorCode}).";
        }
        return $"Signing/status terminal state {status}.";
    }
}
