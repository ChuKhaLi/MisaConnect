using System.Globalization;
using System.Text.Json;

namespace MisaConnect.EInvoice.IntegrationTests.Sandbox;

/// <summary>
/// Slice 6 fixture-pool loader. Reads
/// <c>issued-invoice-fixtures.json</c> (per
/// <c>specs/006-misa-invoice-amendments/contracts/fixture-pool.md</c>),
/// guards against incompatible schema versions, and serves fixtures via
/// <see cref="Checkout(string)"/> while appending to the per-run
/// consumption ledger. Sandbox tests treat
/// <see cref="FixturePoolExhaustedException"/> as a clean skip signal.
/// </summary>
internal sealed class FixturePoolLoader
{
    private const string ExpectedMajorVersion = "1";
    private const string PoolFileName = "issued-invoice-fixtures.json";
    private const string LedgerFileName = "issued-invoice-fixtures.consumption.log";

    private readonly object _gate = new();
    private readonly List<Fixture> _fixtures;
    private readonly HashSet<string> _consumedThisRun = new(StringComparer.Ordinal);
    private readonly string _ledgerPath;
    private readonly DateTimeOffset _runStartUtc;

    public FixturePoolLoader(string poolDirectory, DateTimeOffset runStartUtc)
    {
        var poolPath = Path.Combine(poolDirectory, PoolFileName);
        _ledgerPath = Path.Combine(poolDirectory, LedgerFileName);
        _runStartUtc = runStartUtc;

        _fixtures = Load(poolPath);
        ReplayConsumptionLedger();
    }

    public int EligibleCount(string kind)
    {
        lock (_gate)
        {
            return _fixtures.Count(f => Matches(f, kind) && !_consumedThisRun.Contains(f.FixtureId));
        }
    }

    public Fixture Checkout(string kind, string testMethodFullName)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(testMethodFullName);

        lock (_gate)
        {
            var pick = _fixtures
                .Where(f => Matches(f, kind) && !_consumedThisRun.Contains(f.FixtureId))
                .OrderBy(f => f.FixtureId, StringComparer.Ordinal)
                .FirstOrDefault();

            if (pick is null)
            {
                throw new FixturePoolExhaustedException(kind);
            }

            _consumedThisRun.Add(pick.FixtureId);
            AppendLedger(pick.FixtureId, kind, testMethodFullName);
            return pick;
        }
    }

    public static string DefaultPoolDirectory()
    {
        // Walk up from the test assembly's directory to find the
        // tests/MisaConnect.EInvoice.IntegrationTests/Sandbox folder.
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            var candidate = Path.Combine(dir, "tests", "MisaConnect.EInvoice.IntegrationTests", "Sandbox");
            if (Directory.Exists(candidate)) return candidate;
            var parent = Directory.GetParent(dir);
            if (parent is null) break;
            dir = parent.FullName;
        }
        throw new DirectoryNotFoundException(
            "Could not locate tests/MisaConnect.EInvoice.IntegrationTests/Sandbox/ from the test assembly location.");
    }

    private static bool Matches(Fixture f, string kind) =>
        f.Purpose.Equals("either", StringComparison.OrdinalIgnoreCase) ||
        f.Purpose.Equals(kind, StringComparison.OrdinalIgnoreCase);

    private void AppendLedger(string fixtureId, string kind, string testMethodFullName)
    {
        var line = $"{DateTimeOffset.UtcNow:O} {fixtureId} {kind} {testMethodFullName}{Environment.NewLine}";
        try
        {
            File.AppendAllText(_ledgerPath, line);
        }
        catch (IOException)
        {
            // Ledger failures are non-fatal — the fixture is already marked
            // consumed in memory for this run.
        }
    }

    private void ReplayConsumptionLedger()
    {
        if (!File.Exists(_ledgerPath)) return;
        foreach (var line in File.ReadAllLines(_ledgerPath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Split(' ', 4, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            if (!DateTimeOffset.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var ts)) continue;
            if (ts < _runStartUtc) continue; // pre-run consumption doesn't block re-use this run
            _consumedThisRun.Add(parts[1]);
        }
    }

    private static List<Fixture> Load(string poolPath)
    {
        if (!File.Exists(poolPath))
        {
            throw new FileNotFoundException(
                $"Fixture pool file missing: {poolPath}. Operator must create it per fixture-pool.md.", poolPath);
        }

        var raw = File.ReadAllText(poolPath);
        var doc = JsonSerializer.Deserialize<PoolFile>(raw, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        }) ?? throw new InvalidDataException($"Fixture pool file empty / unparseable: {poolPath}");

        var major = (doc.SchemaVersion ?? "").Split('.').FirstOrDefault();
        if (!string.Equals(major, ExpectedMajorVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Fixture pool schemaVersion '{doc.SchemaVersion}' is not compatible with the loader's expected major version {ExpectedMajorVersion}.");
        }

        return doc.Fixtures?.ToList() ?? new List<Fixture>();
    }

    private sealed record PoolFile(string? SchemaVersion, IReadOnlyList<Fixture>? Fixtures);
}

/// <summary>
/// Slice 6 fixture-pool entry per
/// <c>specs/006-misa-invoice-amendments/contracts/fixture-pool.md</c>.
/// </summary>
internal sealed record Fixture(
    string FixtureId,
    string Purpose,
    string RefId,
    string InvNo,
    string InvTemplateNo,
    string InvSeries,
    string InvDate,
    string? CreatedAt = null,
    string? Notes = null);

/// <summary>
/// Thrown when <see cref="FixturePoolLoader.Checkout(string,string)"/>
/// can't find an eligible fixture for the requested kind. The
/// <see cref="SandboxFact"/> infrastructure converts this into a clean
/// test skip with reason
/// <c>FixturePoolExhausted: &lt;kind&gt;; top up via tests/MisaConnect.EInvoice.IntegrationTests/Sandbox/README.md</c>
/// (FR-069).
/// </summary>
internal sealed class FixturePoolExhaustedException : Exception
{
    public string Kind { get; }

    public FixturePoolExhaustedException(string kind)
        : base($"FixturePoolExhausted: {kind}; top up via tests/MisaConnect.EInvoice.IntegrationTests/Sandbox/README.md")
    {
        Kind = kind;
    }
}
