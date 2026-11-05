using MisaConnect.ESign.Domain.Errors;
using Xunit;

namespace MisaConnect.ESign.IntegrationTests.Sandbox;

/// <summary>
/// Runs a live sandbox sign call. <see cref="NoActiveCertificateException"/> means the
/// sandbox account has no certificate with <c>keyStatus = ACTIVE</c>: login and the
/// certificate listing worked, so this is an account-setup problem, not an SDK defect.
/// The test is skipped in that case unless <c>MISACONNECT_ESIGN_SANDBOX_REQUIRED=1</c>,
/// which makes it fail instead (mirrors TS ruling R13 in
/// <c>src/typescript/esign/test/sandbox/sandbox.test.ts</c>).
/// </summary>
public static class SandboxSigning
{
    public const string RequiredVar = "MISACONNECT_ESIGN_SANDBOX_REQUIRED";

    public static bool Required =>
        Environment.GetEnvironmentVariable(RequiredVar)?.Trim() == "1";

    public static Task<T> SignOrSkipAsync<T>(Func<Task<T>> sign) => SignOrSkipAsync(sign, Required);

    public static async Task<T> SignOrSkipAsync<T>(Func<Task<T>> sign, bool required)
    {
        try
        {
            return await sign();
        }
        catch (NoActiveCertificateException) when (!required)
        {
            throw new SkipException(
                "Sandbox account has no ACTIVE certificate (NoActiveCertificate): login and certificate " +
                $"listing worked; set {RequiredVar}=1 to fail instead.");
        }
    }
}
