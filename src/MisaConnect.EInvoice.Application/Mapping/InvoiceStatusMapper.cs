using MisaConnect.EInvoice.Domain.Invoices;

namespace MisaConnect.EInvoice.Application.Mapping;

/// <summary>
/// FR-048 / R-LU-05 — pure two-axis precedence function mapping MISA's
/// <c>(EInvoiceStatus, PublishStatus)</c> tuple to the unified
/// <see cref="InvoiceStatus"/> enum. The load-bearing copy of the mapping
/// table lives in <c>specs/005-misa-invoice-lookup/contracts/status-mapping.md</c>;
/// the contract-parity test in
/// <c>tests/MisaConnect.EInvoice.UnitTests/Contracts/StatusMappingContractTests.cs</c>
/// asserts the two stay in lockstep.
/// </summary>
public static class InvoiceStatusMapper
{
    public static InvoiceStatus Map(int? eInvoiceStatus, int? publishStatus)
    {
        if (eInvoiceStatus == 3) return InvoiceStatus.Replaced;
        if (eInvoiceStatus == 4) return InvoiceStatus.Adjusted;

        return publishStatus switch
        {
            0 => InvoiceStatus.Draft,
            4 => InvoiceStatus.Signed,
            6 => InvoiceStatus.Issued,
            7 => InvoiceStatus.Issued,
            _ => InvoiceStatus.Issued,
        };
    }
}
