using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MisaConnect.EInvoice.Application.Abstractions;
using MisaConnect.EInvoice.Application.UseCases;
using MisaConnect.EInvoice.Client.DependencyInjection;
using MisaConnect.EInvoice.Domain.Errors;
using MisaConnect.EInvoice.Infrastructure.Configuration;
using MisaConnect.EInvoice.Infrastructure.MeInvoice.Auth;
using Xunit;

namespace MisaConnect.EInvoice.IntegrationTests.Sandbox;

[Collection(SandboxCollection.Name)]
public class TokenAcquisitionTests
{
    [SandboxFact]
    public async Task Live_token_acquisition_returns_nonempty_value_without_leaking_taxcode()
    {
        Assert.True(SandboxCredentials.TryLoad(out var creds, out _));
        using var scope = BuildScope(creds!);
        var ensure = scope.ServiceProvider.GetRequiredService<EnsureAccessToken>();

        var token = await ensure.ExecuteAsync(default);

        Assert.NotEmpty(token.Value);
        Assert.True(token.ExpiresAtUtc > DateTimeOffset.UtcNow);
        Assert.DoesNotContain(creds!.TaxCode, token.Value, StringComparison.Ordinal);
        Assert.DoesNotContain(creds.UserName, token.Value, StringComparison.Ordinal);
    }

    [SandboxFact]
    public async Task Cache_reuse_returns_same_token()
    {
        Assert.True(SandboxCredentials.TryLoad(out var creds, out _));
        using var scope = BuildScope(creds!);
        var ensure = scope.ServiceProvider.GetRequiredService<EnsureAccessToken>();

        var first = await ensure.ExecuteAsync(default);
        var second = await ensure.ExecuteAsync(default);

        Assert.Equal(first.Value, second.Value);
    }

    [SandboxFact]
    public async Task Refresh_after_eviction_returns_valid_token()
    {
        Assert.True(SandboxCredentials.TryLoad(out var creds, out _));
        using var scope = BuildScope(creds!);
        var ensure = scope.ServiceProvider.GetRequiredService<EnsureAccessToken>();
        var cache = scope.ServiceProvider.GetRequiredService<ITokenCache>();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<MisaEInvoiceOptions>>().Value;

        _ = await ensure.ExecuteAsync(default);
        await cache.RemoveAsync(WireTaxCode.Compose(options), default);
        var refreshed = await ensure.ExecuteAsync(default);

        Assert.NotEmpty(refreshed.Value);
    }

    [SandboxFact]
    public async Task Wrong_password_surfaces_Authentication_category()
    {
        Assert.True(SandboxCredentials.TryLoad(out var creds, out _));
        var bogus = creds! with { Password = "wrong-password-on-purpose" };
        using var scope = BuildScope(bogus);
        var ensure = scope.ServiceProvider.GetRequiredService<EnsureAccessToken>();

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => ensure.ExecuteAsync(default));
        Assert.True(ex is MeInvoiceException or HttpRequestException, $"Unexpected exception type: {ex.GetType()}");
    }

    private static IServiceScope BuildScope(SandboxCredentialsSnapshot creds)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddFilter(_ => false));
        services.AddEInvoiceClient(opts =>
        {
            opts.Environment = MeInvoiceEnvironment.Sandbox;
            opts.BaseUrl = "https://testapi.meinvoice.vn/api/integration";
            opts.TaxCode = creds.TaxCode;
            opts.UserName = creds.UserName;
            opts.Password = creds.Password;
            opts.AppId = creds.AppId;
        });
        return services.BuildServiceProvider().CreateScope();
    }
}
