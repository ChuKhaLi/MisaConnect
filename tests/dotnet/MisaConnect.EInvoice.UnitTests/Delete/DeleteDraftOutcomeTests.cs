using MisaConnect.EInvoice.Application.Results;
using MisaConnect.EInvoice.Domain.Invoices;
using Xunit;

namespace MisaConnect.EInvoice.UnitTests.Delete;

/// <summary>
/// Slice 2 T006 — assert <see cref="DeleteDraftOutcome"/> carries the five
/// shape fields (FR-034) and that <see cref="DeleteDraftStatus"/> enumerates
/// exactly the nine values the spec lists.
/// </summary>
public class DeleteDraftOutcomeTests
{
    [Fact]
    public void DeleteDraftStatus_has_exactly_nine_values()
    {
        var values = Enum.GetValues(typeof(DeleteDraftStatus));
        Assert.Equal(9, values.Length);
    }

    [Theory]
    [InlineData(DeleteDraftStatus.Deleted)]
    [InlineData(DeleteDraftStatus.NotFound)]
    [InlineData(DeleteDraftStatus.NotDeletable)]
    [InlineData(DeleteDraftStatus.AuthFailed)]
    [InlineData(DeleteDraftStatus.Configuration)]
    [InlineData(DeleteDraftStatus.MisaThrottled)]
    [InlineData(DeleteDraftStatus.MisaUnavailable)]
    [InlineData(DeleteDraftStatus.TransportFailed)]
    [InlineData(DeleteDraftStatus.MisaUnknown)]
    public void DeleteDraftStatus_enumerates_each_FR_034_value(DeleteDraftStatus value)
    {
        Assert.True(Enum.IsDefined(typeof(DeleteDraftStatus), value));
    }

    [Fact]
    public void Outcome_carries_Status_RawErrorCode_Message_Field_RefId()
    {
        var refId = RefId.From("r1");
        var outcome = new DeleteDraftOutcome(
            Status: DeleteDraftStatus.NotDeletable,
            RawErrorCode: "InvalidTransactionID",
            Message: "hello",
            Field: "fld",
            RefId: refId);

        Assert.Equal(DeleteDraftStatus.NotDeletable, outcome.Status);
        Assert.Equal("InvalidTransactionID", outcome.RawErrorCode);
        Assert.Equal("hello", outcome.Message);
        Assert.Equal("fld", outcome.Field);
        Assert.Equal(refId, outcome.RefId);
    }

    [Fact]
    public void Outcome_with_Deleted_status_carries_all_other_fields_null()
    {
        // The data-model.md §State semantics rule: Status == Deleted ⇒ all other
        // fields null. We assert that the canonical "Deleted" construction the
        // use case will produce holds that contract.
        var refId = RefId.From("r1");
        var outcome = new DeleteDraftOutcome(
            Status: DeleteDraftStatus.Deleted,
            RawErrorCode: null,
            Message: null,
            Field: null,
            RefId: refId);

        Assert.Equal(DeleteDraftStatus.Deleted, outcome.Status);
        Assert.Null(outcome.RawErrorCode);
        Assert.Null(outcome.Message);
        Assert.Null(outcome.Field);
    }
}
