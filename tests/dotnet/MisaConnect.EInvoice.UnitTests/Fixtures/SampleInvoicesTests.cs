using MisaConnect.EInvoice.Application.Validation;
using MisaConnect.EInvoice.TestSupport.Fixtures;
using Xunit;

namespace MisaConnect.EInvoice.UnitTests.Fixtures;

public class SampleInvoicesTests
{
    private readonly InvoiceValidator _validator = new();

    [Fact]
    public void TypicalVatInvoice_passes_validator()
    {
        var failures = _validator.Validate(SampleInvoices.TypicalVatInvoice());
        Assert.Empty(failures);
    }

    [Fact]
    public void IndividualBuyer_passes_validator()
    {
        var failures = _validator.Validate(SampleInvoices.IndividualBuyer());
        Assert.Empty(failures);
    }

    [Fact]
    public void MultiCurrencyVatInvoice_passes_validator()
    {
        var failures = _validator.Validate(SampleInvoices.MultiCurrencyVatInvoice());
        Assert.Empty(failures);
    }

    [Fact]
    public void WithSandboxTag_sets_CustomField1()
    {
        var invoice = SampleInvoices.TypicalVatInvoice().WithSandboxTag("run1", "case1");
        Assert.Equal("EINVOICE-TEST:run1:case1", invoice.CustomFields![1]);
    }
}
