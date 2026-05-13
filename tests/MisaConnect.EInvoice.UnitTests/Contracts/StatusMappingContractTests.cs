using System.Text.RegularExpressions;
using MisaConnect.EInvoice.Application.Mapping;
using MisaConnect.EInvoice.Domain.Invoices;
using Xunit;

namespace MisaConnect.EInvoice.UnitTests.Contracts;

/// <summary>
/// Slice 5 contract-parity test — loads
/// <c>specs/005-misa-invoice-lookup/contracts/status-mapping.md</c> as an
/// embedded resource, parses the load-bearing mapping table, and asserts
/// <see cref="InvoiceStatusMapper.Map(int?, int?)"/> agrees with every
/// documented row. If the contract document and the executable mapper
/// drift, this test fails red.
/// </summary>
public class StatusMappingContractTests
{
    private const string ResourceName = "MisaConnect.EInvoice.UnitTests.Contracts.status-mapping.md";

    [Fact]
    public void MapperMatchesContract()
    {
        var assembly = typeof(StatusMappingContractTests).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{ResourceName}' not found. Available: {string.Join(", ", assembly.GetManifestResourceNames())}");
        using var reader = new StreamReader(stream);
        var content = reader.ReadToEnd();

        var rows = ParseMappingTable(content);

        Assert.True(
            rows.Count >= 20,
            $"Expected at least 20 mapping rows parsed from the contract; got {rows.Count}.");

        foreach (var row in rows)
        {
            var actual = InvoiceStatusMapper.Map(row.EInvoiceStatus, row.PublishStatus);
            Assert.True(
                actual == row.Expected,
                $"Row (E={Format(row.EInvoiceStatus)}, P={Format(row.PublishStatus)}) expected {row.Expected} but mapper returned {actual}.");
        }
    }

    private static IReadOnlyList<ContractRow> ParseMappingTable(string content)
    {
        // Locate the load-bearing table by its heading.
        const string heading = "## The mapping table (load-bearing)";
        var headingIndex = content.IndexOf(heading, StringComparison.Ordinal);
        Assert.True(headingIndex >= 0, $"Heading '{heading}' not found in contract markdown.");

        var tableSection = content[headingIndex..];

        var rows = new List<ContractRow>();
        // Match a row with exactly four pipe-separated cells before the trailing pipe.
        var rowPattern = new Regex(
            @"^\|\s*(?<e>[^|]+?)\s*\|\s*(?<p>[^|]+?)\s*\|\s*(?<status>[^|]+?)\s*\|\s*(?<notes>[^|]*?)\s*\|\s*$",
            RegexOptions.Multiline);

        foreach (Match m in rowPattern.Matches(tableSection))
        {
            var eRaw = m.Groups["e"].Value.Trim();
            var pRaw = m.Groups["p"].Value.Trim();
            var statusRaw = m.Groups["status"].Value.Trim();

            // Skip the header row.
            if (eRaw.Contains("RawEInvoiceStatus", StringComparison.Ordinal))
            {
                continue;
            }

            // Skip the separator row (cells composed of dashes / colons).
            if (IsSeparatorCell(eRaw))
            {
                continue;
            }

            var eParsed = ParseCell(eRaw);
            var pParsed = ParseCell(pRaw);
            var statusName = StripBackticks(statusRaw);

            if (!Enum.TryParse<InvoiceStatus>(statusName, out var expected))
            {
                // Not a status row — skip defensively.
                continue;
            }

            rows.Add(new ContractRow(eParsed, pParsed, expected));
        }

        return rows;
    }

    private static bool IsSeparatorCell(string cell)
    {
        if (cell.Length == 0) return false;
        foreach (var c in cell)
        {
            if (c != '-' && c != ':' && c != ' ') return false;
        }
        return true;
    }

    private static int? ParseCell(string raw)
    {
        var stripped = StripBackticks(raw);

        if (string.Equals(stripped, "null", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // `*(unknown)*` (or any italicised "unknown" placeholder) → 99.
        if (stripped.Contains("unknown", StringComparison.OrdinalIgnoreCase))
        {
            return 99;
        }

        if (int.TryParse(stripped, out var parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException($"Unable to parse mapping-table cell value '{raw}'.");
    }

    private static string StripBackticks(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith('`') && trimmed.EndsWith('`') && trimmed.Length >= 2)
        {
            trimmed = trimmed[1..^1];
        }
        return trimmed.Trim();
    }

    private static string Format(int? value) => value.HasValue ? value.Value.ToString() : "null";

    private sealed record ContractRow(int? EInvoiceStatus, int? PublishStatus, InvoiceStatus Expected);
}
