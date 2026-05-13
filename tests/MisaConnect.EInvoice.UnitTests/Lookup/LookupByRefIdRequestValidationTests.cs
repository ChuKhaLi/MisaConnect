using MisaConnect.EInvoice.Application.UseCases;
using MisaConnect.EInvoice.Domain.Invoices;
using Xunit;

namespace MisaConnect.EInvoice.UnitTests.Lookup;

/// <summary>
/// FR-047 — operation-level validation for the batch lookup request. The
/// <see cref="LookupByRefIdRequest.Validate"/> method covers only the
/// list-level rules (null / empty). Per-RefID length validation lives on
/// <see cref="RefId.From"/> itself (slice 1 FR-021), so the over-length
/// case is exercised at the type-construction layer.
/// </summary>
public class LookupByRefIdRequestValidationTests
{
    [Fact]
    public void Empty_list_returns_Validation()
    {
        var request = new LookupByRefIdRequest(Array.Empty<RefId>(), InvoiceWithCode: true);

        var reason = request.Validate();

        Assert.NotNull(reason);
        Assert.NotEmpty(reason!);
    }

    [Fact]
    public void Null_list_returns_Validation()
    {
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
        var request = new LookupByRefIdRequest(null!, InvoiceWithCode: true);
#pragma warning restore CS8625

        var reason = request.Validate();

        Assert.NotNull(reason);
    }

    [Fact]
    public void Single_item_list_validates_ok()
    {
        var request = new LookupByRefIdRequest(new[] { RefId.From("abc") }, InvoiceWithCode: true);

        var reason = request.Validate();

        Assert.Null(reason);
    }

    [Fact]
    public void RefId_From_throws_on_over_length_value()
    {
        // FR-021 — RefId values are bounded by RefId.DefaultMaxLength (64);
        // construction fails fast at the type boundary, so the use case
        // never observes an over-length value.
        var oversized = string.Concat(Enumerable.Repeat("a", 65));

        var ex = Assert.Throws<ArgumentException>(() => RefId.From(oversized));
        Assert.Contains("length", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
