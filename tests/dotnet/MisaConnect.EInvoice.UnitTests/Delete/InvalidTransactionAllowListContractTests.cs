using System.Reflection;
using System.Text.RegularExpressions;
using MisaConnect.EInvoice.Application.Errors;
using MisaConnect.EInvoice.Domain.Errors;
using Xunit;

namespace MisaConnect.EInvoice.UnitTests.Delete;

/// <summary>
/// Slice 2 T031 (test T12) — load the contract markdown file as an
/// embedded resource, parse its allow-list table, and assert set-equality
/// (phrase + category) against
/// <see cref="InvalidTransactionAllowList.Entries"/>. A red test means the
/// contract document and the executable allow-list have drifted.
/// </summary>
public class InvalidTransactionAllowListContractTests
{
    private const string ResourceName = "MisaConnect.EInvoice.UnitTests.Delete.error-message-disambiguation.md";

    [Fact]
    public void AllowList_matches_contract()
    {
        var rows = ReadContractTable();
        Assert.Equal(rows.Count, InvalidTransactionAllowList.Entries.Count);

        foreach (var row in rows)
        {
            Assert.Contains(
                InvalidTransactionAllowList.Entries,
                e => e.PhraseSubstring == row.Phrase && e.Category == row.Category);
        }
    }

    private static IReadOnlyList<ContractRow> ReadContractTable()
    {
        var assembly = typeof(InvalidTransactionAllowListContractTests).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' not found. Available: {string.Join(", ", assembly.GetManifestResourceNames())}");
        using var reader = new StreamReader(stream);
        var content = reader.ReadToEnd();

        // Match the table rows under "### Entries" — `| N | \`phrase\` | \`Category\` | \`Status\` | ... |`
        var rows = new List<ContractRow>();
        var rowPattern = new Regex(
            @"^\|\s*\d+\s*\|\s*`(?<phrase>[^`]+)`\s*\|\s*`(?<cat>[^`]+)`\s*\|",
            RegexOptions.Multiline);

        foreach (Match m in rowPattern.Matches(content))
        {
            var phrase = m.Groups["phrase"].Value.Trim();
            var catName = m.Groups["cat"].Value.Trim();
            if (Enum.TryParse<MeInvoiceErrorCategory>(catName, out var category))
            {
                rows.Add(new ContractRow(phrase, category));
            }
        }

        Assert.NotEmpty(rows);
        return rows;
    }

    private sealed record ContractRow(string Phrase, MeInvoiceErrorCategory Category);
}
