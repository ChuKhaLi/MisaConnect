using Xunit;

namespace MisaConnect.EInvoice.IntegrationTests.Sandbox;

public class SandboxFactSkipTests
{
    private const string TaxCodeVar = SandboxCredentials.TaxCodeVar;
    private const string UserNameVar = SandboxCredentials.UserNameVar;
    private const string PasswordVar = SandboxCredentials.PasswordVar;
    private const string AppIdVar = SandboxCredentials.AppIdVar;

    [Fact]
    public void Load_returns_null_when_env_vars_missing()
    {
        var snapshot = SaveAndClear();
        try
        {
            var creds = SandboxCredentials.Load();
            Assert.Null(creds);
        }
        finally
        {
            Restore(snapshot);
        }
    }

    [Fact]
    public void Load_populates_from_env_vars_when_all_present()
    {
        var snapshot = SaveAndClear();
        try
        {
            Environment.SetEnvironmentVariable(TaxCodeVar, "9999999999");
            Environment.SetEnvironmentVariable(UserNameVar, "override@example.com");
            Environment.SetEnvironmentVariable(PasswordVar, "override-pw");
            Environment.SetEnvironmentVariable(AppIdVar, "999");

            var creds = SandboxCredentials.Load();

            Assert.NotNull(creds);
            Assert.Equal("9999999999", creds!.TaxCode);
            Assert.Equal("override@example.com", creds.UserName);
            Assert.Equal("override-pw", creds.Password);
            Assert.Equal("999", creds.AppId);
        }
        finally
        {
            Restore(snapshot);
        }
    }

    [Fact]
    public void TryLoad_returns_false_with_a_skip_reason_when_no_env_vars_set()
    {
        var snapshot = SaveAndClear();
        try
        {
            var present = SandboxCredentials.TryLoad(out var creds, out var reason);

            Assert.False(present);
            Assert.Null(creds);
            Assert.NotNull(reason);
            Assert.Contains("not configured", reason);
        }
        finally
        {
            Restore(snapshot);
        }
    }

    [Fact]
    public void TryLoad_returns_true_when_all_env_vars_set()
    {
        var snapshot = SaveAndClear();
        try
        {
            Environment.SetEnvironmentVariable(TaxCodeVar, "1234567890");
            Environment.SetEnvironmentVariable(UserNameVar, "user@example.com");
            Environment.SetEnvironmentVariable(PasswordVar, "pw");
            Environment.SetEnvironmentVariable(AppIdVar, "111");

            var present = SandboxCredentials.TryLoad(out var creds, out var reason);

            Assert.True(present);
            Assert.NotNull(creds);
            Assert.Null(reason);
        }
        finally
        {
            Restore(snapshot);
        }
    }

    private static (string?, string?, string?, string?) SaveAndClear()
    {
        var snap = (
            Environment.GetEnvironmentVariable(TaxCodeVar),
            Environment.GetEnvironmentVariable(UserNameVar),
            Environment.GetEnvironmentVariable(PasswordVar),
            Environment.GetEnvironmentVariable(AppIdVar)
        );

        Environment.SetEnvironmentVariable(TaxCodeVar, null);
        Environment.SetEnvironmentVariable(UserNameVar, null);
        Environment.SetEnvironmentVariable(PasswordVar, null);
        Environment.SetEnvironmentVariable(AppIdVar, null);

        return snap;
    }

    private static void Restore((string?, string?, string?, string?) snap)
    {
        Environment.SetEnvironmentVariable(TaxCodeVar, snap.Item1);
        Environment.SetEnvironmentVariable(UserNameVar, snap.Item2);
        Environment.SetEnvironmentVariable(PasswordVar, snap.Item3);
        Environment.SetEnvironmentVariable(AppIdVar, snap.Item4);
    }
}
