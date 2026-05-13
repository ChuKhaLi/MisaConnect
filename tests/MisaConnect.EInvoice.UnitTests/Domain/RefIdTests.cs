using MisaConnect.EInvoice.Domain.Invoices;
using Xunit;

namespace MisaConnect.EInvoice.UnitTests.Domain;

public class RefIdTests
{
    [Fact]
    public void From_empty_throws()
    {
        Assert.Throws<ArgumentException>(() => RefId.From(""));
    }

    [Fact]
    public void From_too_long_throws()
    {
        Assert.Throws<ArgumentException>(() => RefId.From(new string('a', 65)));
    }

    [Fact]
    public void From_accepts_custom_key()
    {
        var refId = RefId.From("custom-key");
        Assert.Equal("custom-key", refId.Value);
    }

    [Fact]
    public void NewGuid_returns_36_char_lowercase_hyphenated()
    {
        var refId = RefId.NewGuid();
        Assert.Equal(36, refId.Value.Length);
        Assert.Equal(refId.Value, refId.Value.ToLowerInvariant());
        Assert.Equal(4, refId.Value.Count(c => c == '-'));
    }
}
