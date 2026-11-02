using MisaConnect.EInvoice.Application.UseCases;
using MisaConnect.EInvoice.Domain.Invoices;
using Xunit;

namespace MisaConnect.EInvoice.UnitTests.Delete;

/// <summary>
/// Slice 2 T005 — assert <see cref="DeleteDraftRequest"/> demands an explicit
/// <c>invoiceWithCode</c> (FR-031: no implicit default) and that the
/// <c>RefId</c> is carried as the value-object (compile-time non-null).
/// </summary>
public class DeleteDraftRequestTests
{
    [Fact]
    public void Constructs_with_RefId_and_InvoiceWithCode()
    {
        var refId = RefId.From("abc-123");
        var request = new DeleteDraftRequest(refId, InvoiceWithCode: true);

        Assert.Equal(refId, request.RefId);
        Assert.True(request.InvoiceWithCode);
    }

    [Fact]
    public void InvoiceWithCode_is_a_required_positional_parameter()
    {
        // The record has no parameterless ctor and InvoiceWithCode is a value-type
        // positional parameter — verify by reflection that there is no
        // zero-arg ctor (defends against an accidental record-init refactor).
        var ctors = typeof(DeleteDraftRequest).GetConstructors();
        Assert.All(ctors, c => Assert.Equal(2, c.GetParameters().Length));
        Assert.Contains(ctors, c =>
            c.GetParameters()[0].ParameterType == typeof(RefId) &&
            c.GetParameters()[1].ParameterType == typeof(bool));
    }

    [Fact]
    public void Records_with_identical_RefId_and_flag_are_equal()
    {
        var a = new DeleteDraftRequest(RefId.From("x"), true);
        var b = new DeleteDraftRequest(RefId.From("x"), true);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Records_differing_by_flag_are_unequal()
    {
        var a = new DeleteDraftRequest(RefId.From("x"), true);
        var b = new DeleteDraftRequest(RefId.From("x"), false);
        Assert.NotEqual(a, b);
    }
}
