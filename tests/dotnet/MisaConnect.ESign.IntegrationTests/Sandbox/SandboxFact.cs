using System.Runtime.CompilerServices;
using Xunit;

namespace MisaConnect.ESign.IntegrationTests.Sandbox;

public enum SandboxRequirement
{
    Default = 0,
    TwoFactorAuth = 1,
    Webhook = 2,
}

/// <summary>
/// xUnit fact that runs against the MISA eSign sandbox. Requires
/// <c>MISACONNECT_ESIGN_SANDBOX_*</c> env vars to be set. When
/// <see cref="Requires"/> is <see cref="SandboxRequirement.TwoFactorAuth"/>
/// the fact also requires <c>MISACONNECT_ESIGN_SANDBOX_OTP_PROVIDER</c> and
/// <c>MISACONNECT_ESIGN_SANDBOX_USER_2FA_ENABLED</c> — when any of these is
/// absent the fact skips cleanly.
/// <para>
/// A test can also skip at run time with xUnit v3's built-in <c>Assert.Skip</c>
/// (see <see cref="SandboxSigning"/>); no custom discoverer is needed.
/// </para>
/// </summary>
public sealed class SandboxFactAttribute : FactAttribute
{
    private SandboxRequirement _requires;

    public SandboxFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber) => Evaluate();

    public SandboxRequirement Requires
    {
        get => _requires;
        set
        {
            _requires = value;
            Evaluate();
        }
    }

    private void Evaluate()
    {
        var missing = new List<string>(8);
        foreach (var v in new[]
        {
            "MISACONNECT_ESIGN_SANDBOX_BASE_URL",
            "MISACONNECT_ESIGN_SANDBOX_CLIENT_ID",
            "MISACONNECT_ESIGN_SANDBOX_CLIENT_KEY",
            "MISACONNECT_ESIGN_SANDBOX_USERNAME",
            "MISACONNECT_ESIGN_SANDBOX_PASSWORD",
        })
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(v))) missing.Add(v);
        }
        if (_requires == SandboxRequirement.TwoFactorAuth)
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MISACONNECT_ESIGN_SANDBOX_OTP_PROVIDER")))
                missing.Add("MISACONNECT_ESIGN_SANDBOX_OTP_PROVIDER");
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MISACONNECT_ESIGN_SANDBOX_USER_2FA_ENABLED")))
                missing.Add("MISACONNECT_ESIGN_SANDBOX_USER_2FA_ENABLED");
        }
        if (_requires == SandboxRequirement.Webhook)
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MISACONNECT_ESIGN_SANDBOX_WEBHOOK_URL")))
                missing.Add("MISACONNECT_ESIGN_SANDBOX_WEBHOOK_URL");
        }
        Skip = missing.Count > 0
            ? "ESign sandbox not configured. Missing: " + string.Join(", ", missing)
            : null;
    }
}
