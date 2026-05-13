using MisaConnect.EInvoice.Application.Mapping;
using MisaConnect.EInvoice.Domain.Invoices;
using Xunit;

namespace MisaConnect.EInvoice.UnitTests.Mapping;

/// <summary>
/// Parameterised unit tests for the pure two-axis precedence rule
/// (<see cref="InvoiceStatusMapper.Map(int?, int?)"/>). Each row in the
/// load-bearing mapping table at
/// <c>specs/005-misa-invoice-lookup/contracts/status-mapping.md</c> is
/// one <see cref="InlineDataAttribute"/> case here. <see cref="Cancelled_is_never_produced"/>
/// asserts the negative invariant that <see cref="InvoiceStatus.Cancelled"/>
/// is unreachable from any documented combination.
/// </summary>
public class InvoiceStatusMapperTests
{
    [Theory]
    [InlineData(1, 0, "Draft")]
    [InlineData(1, 4, "Signed")]
    [InlineData(1, 6, "Issued")]
    [InlineData(1, 7, "Issued")]
    [InlineData(1, 99, "Issued")]
    [InlineData(1, null, "Issued")]
    [InlineData(3, 0, "Replaced")]
    [InlineData(3, 4, "Replaced")]
    [InlineData(3, 6, "Replaced")]
    [InlineData(3, 7, "Replaced")]
    [InlineData(3, 99, "Replaced")]
    [InlineData(3, null, "Replaced")]
    [InlineData(4, 0, "Adjusted")]
    [InlineData(4, 4, "Adjusted")]
    [InlineData(4, 6, "Adjusted")]
    [InlineData(4, 7, "Adjusted")]
    [InlineData(4, 99, "Adjusted")]
    [InlineData(4, null, "Adjusted")]
    [InlineData(99, 0, "Draft")]
    [InlineData(99, 4, "Signed")]
    [InlineData(99, 6, "Issued")]
    [InlineData(99, 7, "Issued")]
    [InlineData(99, 99, "Issued")]
    [InlineData(null, 0, "Draft")]
    [InlineData(null, 4, "Signed")]
    [InlineData(null, 6, "Issued")]
    [InlineData(null, 7, "Issued")]
    [InlineData(null, null, "Issued")]
    public void Maps_two_axis_precedence_rule(int? eInvoiceStatus, int? publishStatus, string expected)
    {
        var expectedStatus = Enum.Parse<InvoiceStatus>(expected);
        var actual = InvoiceStatusMapper.Map(eInvoiceStatus, publishStatus);
        Assert.Equal(expectedStatus, actual);
    }

    [Fact]
    public void Cancelled_is_never_produced()
    {
        var combinations = new (int? E, int? P)[]
        {
            (1, 0), (1, 4), (1, 6), (1, 7), (1, 99), (1, null),
            (3, 0), (3, 4), (3, 6), (3, 7), (3, 99), (3, null),
            (4, 0), (4, 4), (4, 6), (4, 7), (4, 99), (4, null),
            (99, 0), (99, 4), (99, 6), (99, 7), (99, 99),
            (null, 0), (null, 4), (null, 6), (null, 7), (null, null),
        };

        foreach (var (e, p) in combinations)
        {
            var status = InvoiceStatusMapper.Map(e, p);
            Assert.NotEqual(InvoiceStatus.Cancelled, status);
        }
    }
}
