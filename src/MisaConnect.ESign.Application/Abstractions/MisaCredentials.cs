namespace MisaConnect.ESign.Application.Abstractions;

/// <summary>
/// One signer's full MISA credential set. Carries both the app-dimension
/// credentials (<see cref="ClientId"/>/<see cref="ClientKey"/>) and the
/// user-dimension credentials (<see cref="UserName"/>/<see cref="Password"/>)
/// so a single per-call resolution feeds header injection, the login body, and
/// the token-cache key consistently.
/// </summary>
public sealed record MisaCredentials(
    string ClientId,
    string ClientKey,
    string UserName,
    string Password)
{
    /// <summary>
    /// Redacts the two secret-bearing members (<see cref="ClientKey"/> and
    /// <see cref="Password"/>). The positional-record auto-generated
    /// <c>ToString()</c> would otherwise emit ALL members in plaintext, so any
    /// structured-logging sink, exception-context capture, or <c>$"{creds}"</c>
    /// interpolation would dump the secrets (Constitution Principle VIII).
    /// </summary>
    public override string ToString() =>
        $"MisaCredentials {{ UserName = {UserName}, ClientId = {ClientId}, ClientKey = <redacted>, Password = <redacted> }}";
}
