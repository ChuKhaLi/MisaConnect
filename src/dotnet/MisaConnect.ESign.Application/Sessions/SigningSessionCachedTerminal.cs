using MisaConnect.ESign.Domain.Webhook;

namespace MisaConnect.ESign.Application.Sessions;

/// <summary>
/// A terminal (FAILED/CANCELLED) state confirmed by MISA's <c>Signing/status</c> and
/// recorded once on the <see cref="SigningSession"/>. Later deliveries for the same
/// transaction are answered from this record without calling MISA or the delivery hook.
/// </summary>
/// <param name="TriggeringMessageId">The webhook <c>messageId</c> whose finalize recorded the terminal state.</param>
/// <param name="Ack">The ACK returned for that delivery; repeated for later deliveries.</param>
/// <param name="Status">The terminal status MISA reported.</param>
/// <param name="MisaErrorCode">MISA's <c>errorCode</c> for the terminal state, when present.</param>
public sealed record SigningSessionCachedTerminal(
    string TriggeringMessageId,
    WebhookAck Ack,
    WebhookStatus Status,
    string? MisaErrorCode);
