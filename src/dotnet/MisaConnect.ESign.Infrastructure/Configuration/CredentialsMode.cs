namespace MisaConnect.ESign.Infrastructure.Configuration;

/// <summary>
/// Selects where the SDK sources the four MISA credentials
/// (ClientId/ClientKey/UserName/Password). This flag is read ONLY by
/// <see cref="MisaESignOptionsValidator"/>; it relaxes the startup validator and
/// does not, by itself, change credential resolution — dynamic behaviour is
/// achieved by a consumer registering its own
/// <see cref="Application.Abstractions.IMisaCredentialsAccessor"/>.
/// </summary>
public enum CredentialsMode
{
    /// <summary>
    /// Default. The four credentials are read from <see cref="MisaESignOptions"/>
    /// and the validator requires them at startup.
    /// </summary>
    Static = 0,

    /// <summary>
    /// Credentials are supplied per-call via
    /// <see cref="Application.Abstractions.IMisaCredentialsAccessor"/>; the
    /// validator does NOT require the four static credential values. All other
    /// validation (BaseUrl/Environment/Polling/…) still applies.
    /// </summary>
    Dynamic = 1,
}
