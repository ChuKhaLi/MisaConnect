using MisaConnect.ESign.Client;
using Xunit;

namespace MisaConnect.ESign.UnitTests.Authentication;

public class PendingOtpChallengesTests
{
    [Fact]
    public void Add_then_Contains()
    {
        var sut = new PendingOtpChallenges();
        sut.Add("c", "u");
        Assert.True(sut.Contains("c", "u"));
        Assert.False(sut.Contains("c", "other"));
    }

    [Fact]
    public void Remove_clears_only_that_principal()
    {
        var sut = new PendingOtpChallenges();
        sut.Add("c", "a");
        sut.Add("c", "b");
        sut.Remove("c", "a");
        Assert.False(sut.Contains("c", "a"));
        Assert.True(sut.Contains("c", "b"));
    }

    [Fact]
    public void Capacity_evicts_oldest()
    {
        var sut = new PendingOtpChallenges(2);
        sut.Add("c", "A");
        sut.Add("c", "B");
        sut.Add("c", "C");
        Assert.False(sut.Contains("c", "A"));
        Assert.True(sut.Contains("c", "B"));
        Assert.True(sut.Contains("c", "C"));
    }

    [Fact]
    public void Re_adding_refreshes_recency()
    {
        var sut = new PendingOtpChallenges(2);
        sut.Add("c", "A");
        sut.Add("c", "B");
        sut.Add("c", "A");
        sut.Add("c", "C");
        Assert.True(sut.Contains("c", "A"));
        Assert.False(sut.Contains("c", "B"));
    }

    [Fact]
    public void Same_user_different_client_is_distinct()
    {
        var sut = new PendingOtpChallenges();
        sut.Add("c1", "u");
        Assert.True(sut.Contains("c1", "u"));
        Assert.False(sut.Contains("c2", "u"));
    }
}
