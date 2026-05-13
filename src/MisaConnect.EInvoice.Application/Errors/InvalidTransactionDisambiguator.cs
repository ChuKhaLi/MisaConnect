using MisaConnect.EInvoice.Domain.Errors;

namespace MisaConnect.EInvoice.Application.Errors;

/// <summary>
/// FR-035 disambiguation of MISA's <c>InvalidTransactionID</c> errorCode into
/// either <see cref="MeInvoiceErrorCategory.ResourceNotFound"/> (RefID
/// unknown) or <see cref="MeInvoiceErrorCategory.NotDeletable"/> (invoice
/// exists but cannot be deleted). Substring allow-list match, first hit
/// wins, default to <c>ResourceNotFound</c>. See
/// <c>specs/004-misa-invoice-draft-delete/contracts/error-message-disambiguation.md</c>.
///
/// The classifier itself does NOT log — the caller (the delete use case)
/// emits the FR-035 warning on the default branch so the classifier stays
/// pure and unit-testable.
/// </summary>
public sealed class InvalidTransactionDisambiguator
{
    public MeInvoiceErrorCategory Classify(string? misaErrorMessage)
    {
        if (string.IsNullOrWhiteSpace(misaErrorMessage))
        {
            return MeInvoiceErrorCategory.ResourceNotFound;
        }

        foreach (var entry in InvalidTransactionAllowList.Entries)
        {
            if (misaErrorMessage.Contains(entry.PhraseSubstring, StringComparison.OrdinalIgnoreCase))
            {
                return entry.Category;
            }
        }

        return MeInvoiceErrorCategory.ResourceNotFound;
    }
}

internal sealed record DisambiguationEntry(
    string PhraseSubstring,
    MeInvoiceErrorCategory Category,
    string Source);

internal static class InvalidTransactionAllowList
{
    public static readonly IReadOnlyList<DisambiguationEntry> Entries = new[]
    {
        new DisambiguationEntry("không tồn tại", MeInvoiceErrorCategory.ResourceNotFound, "MISA error catalogue"),
        new DisambiguationEntry("đã phát hành",  MeInvoiceErrorCategory.NotDeletable,    "MISA sandbox observed 2026-05-08"),
        new DisambiguationEntry("đã ký",          MeInvoiceErrorCategory.NotDeletable,    "MISA sandbox observed 2026-05-08"),
    };
}
