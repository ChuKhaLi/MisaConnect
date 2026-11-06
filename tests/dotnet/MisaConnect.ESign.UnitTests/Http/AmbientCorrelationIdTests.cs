using MisaConnect.ESign.Infrastructure.Http;
using MisaConnect.ESign.UnitTests.TestSupport;
using Xunit;

namespace MisaConnect.ESign.UnitTests.Http;

public class AmbientCorrelationIdTests
{
    [Fact]
    public void Nested_push_restores_in_lifo_order()
    {
        Assert.Null(AmbientCorrelationId.Current);

        var outer = AmbientCorrelationId.Push("a");
        Assert.Equal("a", AmbientCorrelationId.Current);

        var inner = AmbientCorrelationId.Push("b");
        Assert.Equal("b", AmbientCorrelationId.Current);

        using (AmbientCorrelationId.Push(null))
        {
            Assert.Equal("b", AmbientCorrelationId.Current);
        }
        using (AmbientCorrelationId.Push(string.Empty))
        {
            Assert.Equal("b", AmbientCorrelationId.Current);
        }

        inner.Dispose();
        Assert.Equal("a", AmbientCorrelationId.Current);

        outer.Dispose();
        Assert.Null(AmbientCorrelationId.Current);
    }

    [Fact]
    public void Wrapped_accessor_prefers_the_ambient_value()
    {
        var wrapped = AmbientCorrelationId.Wrap(new StubCorrelationIdAccessor("scope-cid"));
        Assert.Equal("scope-cid", wrapped.Current);

        using (AmbientCorrelationId.Push("flow-cid"))
        {
            Assert.Equal("flow-cid", wrapped.Current);
        }

        Assert.Equal("scope-cid", wrapped.Current);
        Assert.Same(wrapped, AmbientCorrelationId.Wrap(wrapped));
    }
}
