using Xunit;

namespace MisaConnect.EInvoice.IntegrationTests.Sandbox;

public class FixturePoolLoaderPathTests
{
    [Fact]
    public void DefaultPoolDirectory_IsFoundUnderTestsDotnet()
    {
        var dir = FixturePoolLoader.DefaultPoolDirectory();

        Assert.True(Directory.Exists(dir));
        Assert.EndsWith(
            Path.Combine("tests", "dotnet", "MisaConnect.EInvoice.IntegrationTests", "Sandbox"),
            dir);
    }
}
