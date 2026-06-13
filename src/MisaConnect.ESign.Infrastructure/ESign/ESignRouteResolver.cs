using MisaConnect.ESign.Infrastructure.Configuration;

namespace MisaConnect.ESign.Infrastructure.ESign;

/// <summary>
/// Centralizes the split between the configured <see cref="MisaESignOptions.BaseUrl"/>
/// and the actual MISA endpoint topology. The HttpClient base address is the
/// <see cref="Origin"/> of the configured base URL (any path the consumer
/// included — e.g. <c>/webdev/</c> — is discarded), and each request path is
/// derived from a canonical route via <see cref="ResolveRequestPath"/>.
///
/// The <c>ESignHttpRoutes.*</c> constants remain the canonical endpoint
/// identifiers passed to the error mappers; this type only shapes the request
/// path, so error categorization is unaffected.
/// </summary>
internal static class ESignRouteResolver
{
    /// <summary>
    /// Returns the origin (scheme + host + port) of <paramref name="baseUrl"/>
    /// with a trailing slash, discarding any path, query, or fragment.
    /// </summary>
    public static Uri Origin(string baseUrl) =>
        new(new Uri(baseUrl, UriKind.Absolute).GetLeftPart(UriPartial.Authority) + "/");

    /// <summary>
    /// Resolves the effective auth-under-webdev decision: the explicit
    /// <see cref="MisaESignOptions.AuthUnderWebdev"/> override when set,
    /// otherwise derived from <see cref="MisaESignOptions.Environment"/>
    /// (Sandbox ⇒ <c>true</c>, Production ⇒ <c>false</c>).
    /// </summary>
    public static bool EffectiveAuthUnderWebdev(MisaESignOptions options) =>
        options.AuthUnderWebdev ?? (options.Environment == ESignEnvironment.Sandbox);

    /// <summary>
    /// Maps a canonical route (an <c>ESignHttpRoutes.*</c> value) to the request
    /// path resolved against the origin. Prepends <c>webdev/</c> to the login and
    /// two-factor routes when <paramref name="authUnderWebdev"/> is <c>true</c>;
    /// every other route (refresh, resend, ESRM) is returned unchanged.
    /// </summary>
    public static string ResolveRequestPath(string canonicalRoute, bool authUnderWebdev) =>
        authUnderWebdev
            && (canonicalRoute == ESignHttpRoutes.AuthLoginApi
                || canonicalRoute == ESignHttpRoutes.AuthTwoFactor)
            ? "webdev/" + canonicalRoute
            : canonicalRoute;
}
