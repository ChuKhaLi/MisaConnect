using MisaConnect.EInvoice.Application.Errors;
using MisaConnect.EInvoice.Domain.Errors;
using Xunit;

namespace MisaConnect.EInvoice.UnitTests.Delete;

/// <summary>
/// Slice 2 T030 — exercises every <c>Classify_*</c> case from
/// <c>contracts/error-message-disambiguation.md</c> §Acceptance tests.
/// The classifier itself MUST NOT log — that test is covered separately
/// in <see cref="DeleteDraftInvoiceTests"/> T4/T5.
/// </summary>
public class InvalidTransactionDisambiguatorTests
{
    [Fact]
    public void Classify_returns_ResourceNotFound_on_null_message()
    {
        var sut = new InvalidTransactionDisambiguator();
        Assert.Equal(MeInvoiceErrorCategory.ResourceNotFound, sut.Classify(null));
    }

    [Fact]
    public void Classify_returns_ResourceNotFound_on_empty_message()
    {
        var sut = new InvalidTransactionDisambiguator();
        Assert.Equal(MeInvoiceErrorCategory.ResourceNotFound, sut.Classify(""));
    }

    [Fact]
    public void Classify_returns_ResourceNotFound_on_whitespace_message()
    {
        var sut = new InvalidTransactionDisambiguator();
        Assert.Equal(MeInvoiceErrorCategory.ResourceNotFound, sut.Classify("   \t\n"));
    }

    [Fact]
    public void Classify_matches_khong_ton_tai_to_ResourceNotFound()
    {
        var sut = new InvalidTransactionDisambiguator();
        Assert.Equal(MeInvoiceErrorCategory.ResourceNotFound, sut.Classify("RefID không tồn tại."));
    }

    [Fact]
    public void Classify_matches_KHONG_TON_TAI_case_insensitive()
    {
        var sut = new InvalidTransactionDisambiguator();
        Assert.Equal(MeInvoiceErrorCategory.ResourceNotFound, sut.Classify("REFID KHÔNG TỒN TẠI"));
    }

    [Fact]
    public void Classify_matches_da_phat_hanh_to_NotDeletable()
    {
        var sut = new InvalidTransactionDisambiguator();
        Assert.Equal(MeInvoiceErrorCategory.NotDeletable, sut.Classify("Hóa đơn đã phát hành nên không thể xóa."));
    }

    [Fact]
    public void Classify_matches_da_ky_to_NotDeletable()
    {
        var sut = new InvalidTransactionDisambiguator();
        Assert.Equal(MeInvoiceErrorCategory.NotDeletable, sut.Classify("Hóa đơn đã ký, không thể xóa."));
    }

    [Fact]
    public void Classify_returns_ResourceNotFound_on_unmatched_text()
    {
        var sut = new InvalidTransactionDisambiguator();
        Assert.Equal(MeInvoiceErrorCategory.ResourceNotFound, sut.Classify("Some unrelated text"));
    }

    [Fact]
    public void Allow_list_entries_present_in_static_initializer()
    {
        Assert.Equal(3, InvalidTransactionAllowList.Entries.Count);
        Assert.Contains(InvalidTransactionAllowList.Entries, e => e.PhraseSubstring == "không tồn tại" && e.Category == MeInvoiceErrorCategory.ResourceNotFound);
        Assert.Contains(InvalidTransactionAllowList.Entries, e => e.PhraseSubstring == "đã phát hành" && e.Category == MeInvoiceErrorCategory.NotDeletable);
        Assert.Contains(InvalidTransactionAllowList.Entries, e => e.PhraseSubstring == "đã ký" && e.Category == MeInvoiceErrorCategory.NotDeletable);
    }
}
