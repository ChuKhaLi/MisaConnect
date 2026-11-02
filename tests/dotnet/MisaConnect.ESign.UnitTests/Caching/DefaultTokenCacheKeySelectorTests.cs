using Microsoft.Extensions.Options;
using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Infrastructure.Caching;
using MisaConnect.ESign.Infrastructure.Configuration;
using MisaConnect.ESign.Infrastructure.Credentials;
using MisaConnect.ESign.UnitTests.TestSupport;
using Xunit;

namespace MisaConnect.ESign.UnitTests.Caching;

// Slice 008 US2/US3 (FR-007/FR-008): the cache key now sources UserName/ClientId
// from IMisaCredentialsAccessor per-call. Static default is byte-identical;
// dynamic credentials isolate signers; empty identity fails loudly.
public class DefaultTokenCacheKeySelectorTests
{
    private static IOptions<MisaESignOptions> Options(string baseUrl = "https://sandbox.example.com/", string userName = "", string clientId = "")
        => Microsoft.Extensions.Options.Options.Create(new MisaESignOptions
        {
            BaseUrl = baseUrl,
            UserName = userName,
            ClientId = clientId,
        });

    [Fact]
    public void Static_default_composes_username_clientid_host_from_options()
    {
        var options = Options(userName: "user", clientId: "cid");
        var selector = new DefaultTokenCacheKeySelector(options, new OptionsMisaCredentialsAccessor(options));

        Assert.Equal("user|cid|sandbox.example.com", selector.Compose());
    }

    [Fact]
    public void Dynamic_two_signers_yield_distinct_keys()
    {
        var options = Options();
        var current = new MisaCredentials("cid-A", "ck", "alice", "pw");
        var selector = new DefaultTokenCacheKeySelector(options, new StubMisaCredentialsAccessor(() => current));

        var keyA = selector.Compose();
        current = new MisaCredentials("cid-B", "ck", "bob", "pw");
        var keyB = selector.Compose();

        Assert.Equal("alice|cid-A|sandbox.example.com", keyA);
        Assert.Equal("bob|cid-B|sandbox.example.com", keyB);
        Assert.NotEqual(keyA, keyB);
    }

    [Fact]
    public void Empty_username_throws_invalid_operation()
    {
        var options = Options();
        var selector = new DefaultTokenCacheKeySelector(options, new StubMisaCredentialsAccessor(userName: "", clientId: "cid"));

        Assert.Throws<InvalidOperationException>(() => selector.Compose());
    }

    [Fact]
    public void Empty_clientid_throws_invalid_operation()
    {
        var options = Options();
        var selector = new DefaultTokenCacheKeySelector(options, new StubMisaCredentialsAccessor(userName: "user", clientId: ""));

        Assert.Throws<InvalidOperationException>(() => selector.Compose());
    }
}
