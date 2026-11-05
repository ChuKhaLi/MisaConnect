using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Infrastructure.Credentials;
using MisaConnect.ESign.UnitTests.TestSupport;
using Xunit;

namespace MisaConnect.ESign.UnitTests.Abstractions;

public class EffectiveCredentialsTests
{
    private static readonly MisaCredentials Inner = new("cid", "ckey", "inner", "pw");

    private static EffectiveCredentials Build() => new(new StubMisaCredentialsAccessor(Inner));

    [Fact]
    public void Get_returns_inner_without_override() =>
        Assert.Equal(Inner, Build().Get());

    [Fact]
    public void Use_overrides_and_restores_on_dispose()
    {
        var sut = Build();
        var o = new MisaCredentials("c2", "k2", "over", "p2");
        using (sut.Use(o))
        {
            Assert.Equal(o, sut.Get());
        }
        Assert.Equal(Inner, sut.Get());
    }

    [Fact]
    public void Nested_use_restores_outer()
    {
        var sut = Build();
        var outer = new MisaCredentials("c1", "k1", "outer", "p1");
        var inner = new MisaCredentials("c2", "k2", "inner2", "p2");
        using (sut.Use(outer))
        {
            using (sut.Use(inner))
            {
                Assert.Equal(inner, sut.Get());
            }
            Assert.Equal(outer, sut.Get());
        }
        Assert.Equal(Inner, sut.Get());
    }

    [Fact]
    public async Task Override_does_not_leak_into_a_parallel_flow()
    {
        var sut = Build();
        var a = new MisaCredentials("cA", "kA", "userA", "pA");
        var b = new MisaCredentials("cB", "kB", "userB", "pB");

        async Task<MisaCredentials> Flow(MisaCredentials c)
        {
            using (sut.Use(c))
            {
                await Task.Delay(50);
                return sut.Get();
            }
        }

        var ta = Task.Run(() => Flow(a));
        var tb = Task.Run(() => Flow(b));
        Assert.Equal(a, await ta);
        Assert.Equal(b, await tb);
        Assert.Equal(Inner, sut.Get());
    }
}
