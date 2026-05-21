using Xunit;

namespace MisaConnect.ESign.IntegrationTests.Sandbox;

public enum SandboxRequirement
{
    Default = 0,
    TwoFactorAuth = 1,
}

/// <summary>
/// xUnit fact that runs against the MISA eSign sandbox. Requires
/// <c>MISACONNECT_ESIGN_SANDBOX_*</c> env vars to be set. When the
/// <c>requires</c> argument is <see cref="SandboxRequirement.TwoFactorAuth"/>
/// the fact also requires <c>MISACONNECT_ESIGN_SANDBOX_OTP_PROVIDER</c> and
/// <c>MISACONNECT_ESIGN_SANDBOX_USER_2FA_ENABLED</c> — when any of these is
/// absent the fact skips cleanly.
/// </summary>
public sealed class SandboxFactAttribute : FactAttribute
{
    public SandboxFactAttribute(SandboxRequirement requires = SandboxRequirement.Default)
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
        if (requires == SandboxRequirement.TwoFactorAuth)
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MISACONNECT_ESIGN_SANDBOX_OTP_PROVIDER")))
                missing.Add("MISACONNECT_ESIGN_SANDBOX_OTP_PROVIDER");
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MISACONNECT_ESIGN_SANDBOX_USER_2FA_ENABLED")))
                missing.Add("MISACONNECT_ESIGN_SANDBOX_USER_2FA_ENABLED");
        }
        if (missing.Count > 0)
        {
            Skip = "ESign sandbox not configured. Missing: " + string.Join(", ", missing);
        }
    }
}
