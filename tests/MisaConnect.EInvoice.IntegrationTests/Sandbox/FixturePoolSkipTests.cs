using Xunit;

namespace MisaConnect.EInvoice.IntegrationTests.Sandbox;

/// <summary>
/// Slice 6 meta-test (T026) — asserts the
/// <c>FixturePoolExhausted: &lt;kind&gt;; top up via …</c> skip reason
/// surfaces correctly when no eligible fixture remains. Runs without
/// sandbox connectivity; the loader is given a temporary empty pool file.
/// </summary>
public class FixturePoolSkipTests
{
    [Fact]
    public void Empty_pool_yields_FixturePoolExhausted_skip_reason()
    {
        using var temp = new TempPoolDirectory(@"{ ""schemaVersion"": ""1.0.0"", ""fixtures"": [] }");
        var loader = new FixturePoolLoader(temp.Path, DateTimeOffset.UtcNow);

        Assert.Equal(0, loader.EligibleCount("replacement"));

        var ex = Assert.Throws<FixturePoolExhaustedException>(() =>
            loader.Checkout("replacement", "FixturePoolSkipTests.Empty_pool"));

        Assert.Equal("replacement", ex.Kind);
        Assert.Equal(
            "FixturePoolExhausted: replacement; top up via tests/MisaConnect.EInvoice.IntegrationTests/Sandbox/README.md",
            ex.Message);
    }

    [Fact]
    public void Schema_version_major_mismatch_throws_invalid_data()
    {
        using var temp = new TempPoolDirectory(@"{ ""schemaVersion"": ""2.0.0"", ""fixtures"": [] }");
        Assert.Throws<InvalidDataException>(() =>
            new FixturePoolLoader(temp.Path, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Checkout_returns_first_eligible_and_appends_ledger()
    {
        var poolJson = """
        {
          "schemaVersion": "1.0.0",
          "fixtures": [
            {
              "fixtureId": "fx-2026-05-001",
              "purpose": "either",
              "refId": "ref-A",
              "invNo": "00000123",
              "invTemplateNo": "1",
              "invSeries": "C26TAA",
              "invDate": "2026-05-10"
            }
          ]
        }
        """;
        using var temp = new TempPoolDirectory(poolJson);
        var loader = new FixturePoolLoader(temp.Path, DateTimeOffset.UtcNow.AddSeconds(-1));

        Assert.Equal(1, loader.EligibleCount("replacement"));
        Assert.Equal(1, loader.EligibleCount("adjustment"));

        var picked = loader.Checkout("replacement", "Test.Method");

        Assert.Equal("fx-2026-05-001", picked.FixtureId);
        Assert.Equal("ref-A", picked.RefId);
        Assert.Equal(0, loader.EligibleCount("replacement"));

        var ledgerPath = Path.Combine(temp.Path, "issued-invoice-fixtures.consumption.log");
        Assert.True(File.Exists(ledgerPath));
        var lines = File.ReadAllLines(ledgerPath);
        Assert.Single(lines);
        Assert.Contains("fx-2026-05-001", lines[0]);
        Assert.Contains("replacement", lines[0]);
        Assert.Contains("Test.Method", lines[0]);
    }

    private sealed class TempPoolDirectory : IDisposable
    {
        public string Path { get; }

        public TempPoolDirectory(string poolJson)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "einvoice-fixture-pool-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
            File.WriteAllText(System.IO.Path.Combine(Path, "issued-invoice-fixtures.json"), poolJson);
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { /* best-effort */ }
        }
    }
}
