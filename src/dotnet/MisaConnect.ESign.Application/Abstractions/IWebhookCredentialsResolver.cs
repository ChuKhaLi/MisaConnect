namespace MisaConnect.ESign.Application.Abstractions;

/// <summary>
/// Resolves the MISA credentials of the principal that began a webhook-mode
/// transaction, so the SDK can confirm and finalize it when MISA's webhook
/// arrives (there is no per-call ambient on the inbound webhook request).
/// </summary>
/// <remarks>
/// <para>Used only in <c>CredentialsMode.Dynamic</c>, and only on the webhook
/// finalize path. In <c>CredentialsMode.Static</c> the SDK uses the configured
/// options credentials.</para>
/// <para><b>Override ordering:</b> register the resolver BEFORE
/// <c>AddMisaConnectESign</c> (the SDK registers its default via
/// <c>TryAddSingleton</c>, which then yields). The default returns <c>null</c> in
/// Dynamic mode, so webhook finalize answers <c>webhook.finalize_failed</c>.</para>
/// <para>The resolved credentials must belong to the same <c>clientId</c> and
/// <c>userName</c> that began the transaction; otherwise finalize fails with
/// <c>CredentialsMismatch</c>. The SDK never logs the returned values.</para>
/// </remarks>
public interface IWebhookCredentialsResolver
{
    /// <summary>
    /// Returns the credentials for <paramref name="clientId"/>/<paramref name="userName"/>,
    /// or <c>null</c> when none are available.
    /// </summary>
    /// <param name="clientId">The MISA <c>clientId</c> recorded on the signing session.</param>
    /// <param name="userName">The MISA user name recorded on the signing session.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask<MisaCredentials?> ResolveAsync(string clientId, string userName, CancellationToken ct);
}
