using MisaConnect.EInvoice.Domain.Errors;
using Xunit;

namespace MisaConnect.EInvoice.UnitTests.ErrorMapping;

/// <summary>
/// Slice 2 test T20 — sanity check that the slice-2 enum additions exist at
/// known positions so future <c>[Theory]</c> data tables that hard-code
/// ordinal positions do not drift undetected.
/// </summary>
public class MeInvoiceErrorMapperDeleteTests
{
    [Fact]
    public void MeInvoiceErrorCategory_has_NotDeletable_value()
    {
        Assert.True(Enum.IsDefined(typeof(MeInvoiceErrorCategory), nameof(MeInvoiceErrorCategory.NotDeletable)));
    }

    [Fact]
    public void MeInvoiceErrorCategory_has_TransportFailed_value()
    {
        Assert.True(Enum.IsDefined(typeof(MeInvoiceErrorCategory), nameof(MeInvoiceErrorCategory.TransportFailed)));
    }

    [Fact]
    public void Slice_1_enum_ordinals_are_preserved()
    {
        // If slice 2 accidentally shuffles slice 1 ordinals, [Theory] data tables in
        // slice 1 callers that key on ordinal positions will silently drift.
        Assert.Equal(0, (int)MeInvoiceErrorCategory.Configuration);
        Assert.Equal(1, (int)MeInvoiceErrorCategory.Authentication);
        Assert.Equal(13, (int)MeInvoiceErrorCategory.MisaUnknown);
    }

    [Fact]
    public void Slice_2_enum_values_appended_at_end()
    {
        Assert.Equal(14, (int)MeInvoiceErrorCategory.NotDeletable);
        Assert.Equal(15, (int)MeInvoiceErrorCategory.TransportFailed);
    }
}
