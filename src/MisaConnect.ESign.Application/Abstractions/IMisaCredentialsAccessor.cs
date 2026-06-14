namespace MisaConnect.ESign.Application.Abstractions;

/// <summary>
/// Resolves the MISA credentials for the CURRENT call. The SDK consults this
/// per-call inside the awaited HTTP pipeline (login body, <c>x-clientId</c>/
/// <c>x-clientKey</c> headers, and the token-cache key), so a consumer's
/// <see cref="System.Threading.AsyncLocal{T}"/> set around an awaited SDK call
/// flows in and isolates each signer.
/// </summary>
/// <remarks>
/// <para>Implementations MUST be safe to invoke per-call and MUST NOT cache the
/// result on a singleton.</para>
/// <para><b>Lifetime contract:</b> implementations MUST be singleton-registrable
/// and resolve credentials from ambient (<see cref="System.Threading.AsyncLocal{T}"/>)
/// or options state read inside <see cref="Get"/>. They MUST NOT be scoped — the
/// SDK resolves the accessor from singleton collaborators, so a scoped accessor
/// is a captive dependency that fails scope-validation at build.</para>
/// <para><b>Override ordering:</b> to replace the default, register the accessor
/// BEFORE <c>AddMisaConnectESign</c> (the SDK registers its default via
/// <c>TryAddSingleton</c>, which then yields). Registering after appends a second
/// descriptor and leaves the options-default accessor constructible.</para>
/// <para>The SDK never logs the returned values.</para>
/// </remarks>
public interface IMisaCredentialsAccessor
{
    /// <summary>Resolves the credentials for the current call.</summary>
    MisaCredentials Get();
}
