using System.Net.Sockets;
using Xunit;

namespace MisaConnect.EInvoice.IntegrationTests.Sandbox;

/// <summary>
/// xUnit fact that runs against the MISA sandbox. Sandbox credentials are
/// supplied via configuration: env vars (<c>Misa__EInvoice__*</c>) or the
/// <c>MISACONNECT_SANDBOX_*</c> env vars consumed by this attribute. When
/// no credentials are present or the sandbox host is unreachable, the
/// fact skips cleanly. Credential-rejection still fails.
/// </summary>
public sealed class SandboxFactAttribute : FactAttribute
{
    public SandboxFactAttribute()
    {
        if (!SandboxCredentials.TryLoad(out _, out var credsMissing))
        {
            Skip = credsMissing;
            return;
        }
        if (!SandboxReachability.IsReachable.Value)
        {
            Skip = SandboxReachability.SkipReason ?? "MISA sandbox unreachable.";
        }
    }
}

/// <summary>
/// Slice 6 — variant of <see cref="SandboxFactAttribute"/> that also
/// pre-checks the amendment fixture pool. Skips with the documented
/// <c>FixturePoolExhausted: &lt;kind&gt;; top up …</c> reason when no
/// eligible fixture remains for the requested amendment kind, per FR-069.
/// </summary>
public sealed class SandboxAmendmentFactAttribute : FactAttribute
{
    public SandboxAmendmentFactAttribute(string kind)
    {
        if (!SandboxCredentials.TryLoad(out _, out var credsMissing))
        {
            Skip = credsMissing;
            return;
        }
        if (!SandboxReachability.IsReachable.Value)
        {
            Skip = SandboxReachability.SkipReason ?? "MISA sandbox unreachable.";
            return;
        }

        try
        {
            var dir = FixturePoolLoader.DefaultPoolDirectory();
            var loader = new FixturePoolLoader(dir, DateTimeOffset.MinValue);
            if (loader.EligibleCount(kind) == 0)
            {
                Skip = $"FixturePoolExhausted: {kind}; top up via tests/MisaConnect.EInvoice.IntegrationTests/Sandbox/README.md";
            }
        }
        catch (FileNotFoundException ex)
        {
            Skip = $"FixturePoolExhausted: {kind}; top up via tests/MisaConnect.EInvoice.IntegrationTests/Sandbox/README.md (pool file missing: {ex.FileName}).";
        }
        catch (Exception ex)
        {
            Skip = $"FixturePoolExhausted: {kind}; top up via tests/MisaConnect.EInvoice.IntegrationTests/Sandbox/README.md (loader error: {ex.Message}).";
        }
    }
}

/// <summary>
/// Snapshot of the MISA sandbox credentials used by integration tests.
/// </summary>
public sealed record SandboxCredentialsSnapshot(string TaxCode, string UserName, string Password, string AppId);

public static class SandboxCredentials
{
    public const string TaxCodeVar = "MISACONNECT_SANDBOX_TAXCODE";
    public const string UserNameVar = "MISACONNECT_SANDBOX_USERNAME";
    public const string PasswordVar = "MISACONNECT_SANDBOX_PASSWORD";
    public const string AppIdVar = "MISACONNECT_SANDBOX_APPID";

    /// <summary>
    /// Reads sandbox credentials from env vars. Returns null fields when a
    /// value is missing; callers / <see cref="TryLoad"/> decide whether to
    /// run or skip.
    /// </summary>
    public static SandboxCredentialsSnapshot? Load()
    {
        var taxCode = Read(TaxCodeVar);
        var userName = Read(UserNameVar);
        var password = Read(PasswordVar);
        var appId = Read(AppIdVar);
        if (taxCode is null || userName is null || password is null || appId is null) return null;
        return new SandboxCredentialsSnapshot(taxCode, userName, password, appId);
    }

    /// <summary>
    /// Returns <c>true</c> with a populated snapshot when all four env vars
    /// are set; otherwise <c>false</c> with a reason describing which vars
    /// are missing.
    /// </summary>
    public static bool TryLoad(out SandboxCredentialsSnapshot? snapshot, out string? missingReason)
    {
        var missing = new List<string>(4);
        foreach (var v in new[] { TaxCodeVar, UserNameVar, PasswordVar, AppIdVar })
        {
            if (Read(v) is null) missing.Add(v);
        }
        if (missing.Count > 0)
        {
            snapshot = null;
            missingReason = "Sandbox credentials not configured. Set: " + string.Join(", ", missing);
            return false;
        }
        snapshot = Load();
        missingReason = null;
        return snapshot is not null;
    }

    private static string? Read(string varName)
    {
        var v = Environment.GetEnvironmentVariable(varName);
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }
}

/// <summary>
/// Cheap one-time TCP-connect probe to the MISA sandbox host. Runs once
/// per process at first <see cref="SandboxFactAttribute"/> construction;
/// the cached result drives every sandbox test's skip decision (FR-018).
/// </summary>
internal static class SandboxReachability
{
    public const string Host = "testapi.meinvoice.vn";
    public const int Port = 443;
    public const int TimeoutMs = 1500;

    public static readonly Lazy<bool> IsReachable = new(Probe);
    public static string? SkipReason { get; private set; }

    private static bool Probe()
    {
        try
        {
            using var tcp = new TcpClient();
            var task = tcp.ConnectAsync(Host, Port);
            if (!task.Wait(TimeoutMs))
            {
                SkipReason = $"MISA sandbox host {Host}:{Port} did not respond within {TimeoutMs}ms.";
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            SkipReason = $"MISA sandbox host {Host}:{Port} unreachable: {ex.GetType().Name} — {ex.Message}";
            return false;
        }
    }
}
