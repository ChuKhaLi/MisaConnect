using MisaConnect.EInvoice.Application.Abstractions;
using MisaConnect.EInvoice.Application.UseCases;
using MisaConnect.EInvoice.Application.Validation;
using MisaConnect.EInvoice.Domain.Errors;
using MisaConnect.EInvoice.Domain.Invoices;
using MisaConnect.EInvoice.Domain.Pdf;
using MisaConnect.EInvoice.Domain.Templates;
using MisaConnect.EInvoice.Infrastructure.Caching;
using MisaConnect.EInvoice.TestSupport.Fixtures;
using Xunit;

namespace MisaConnect.EInvoice.UnitTests.Amendments;

public class AmendmentRequestValidationTests
{
    private static readonly Template DefaultTemplate = new("ipt-1", "1C25MNQ", "Default", null, 1, true, true, false);

    private static readonly OriginalInvoiceReference ValidOriginalRef = new(
        OrgRefID: "orig-001",
        OrgInvNo: "00000123",
        OrgInvTemplateNo: "1",
        OrgInvSeries: "C26TAA",
        OrgInvDate: new DateOnly(2026, 5, 10));

    // ===== OriginalInvoiceReference value-object tests (T010-T012) =====

    [Fact]
    public void OriginalInvoiceReference_Validate_returns_null_when_all_fields_present()
    {
        Assert.Null(ValidOriginalRef.Validate());
    }

    [Theory]
    [InlineData(null, "OrgRefID", "FR-058")]
    [InlineData("", "OrgRefID", "FR-058")]
    [InlineData("   ", "OrgRefID", "FR-058")]
    public void OriginalInvoiceReference_Validate_flags_missing_OrgRefID(string? value, string fieldSuffix, string ruleId)
    {
        var r = ValidOriginalRef with { OrgRefID = value! };
        var failures = r.Validate();
        Assert.NotNull(failures);
        Assert.Contains(failures!, f => f.FieldPath == $"$originalRef.{fieldSuffix}" && f.RuleId == ruleId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void OriginalInvoiceReference_Validate_flags_missing_OrgInvNo(string? value)
    {
        var r = ValidOriginalRef with { OrgInvNo = value! };
        var failures = r.Validate();
        Assert.NotNull(failures);
        Assert.Contains(failures!, f => f.FieldPath == "$originalRef.OrgInvNo" && f.RuleId == "FR-059");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void OriginalInvoiceReference_Validate_flags_missing_OrgInvTemplateNo(string? value)
    {
        var r = ValidOriginalRef with { OrgInvTemplateNo = value! };
        var failures = r.Validate();
        Assert.NotNull(failures);
        Assert.Contains(failures!, f => f.FieldPath == "$originalRef.OrgInvTemplateNo" && f.RuleId == "FR-059");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void OriginalInvoiceReference_Validate_flags_missing_OrgInvSeries(string? value)
    {
        var r = ValidOriginalRef with { OrgInvSeries = value! };
        var failures = r.Validate();
        Assert.NotNull(failures);
        Assert.Contains(failures!, f => f.FieldPath == "$originalRef.OrgInvSeries" && f.RuleId == "FR-059");
    }

    [Fact]
    public void OriginalInvoiceReference_Validate_flags_default_OrgInvDate()
    {
        var r = ValidOriginalRef with { OrgInvDate = default(DateOnly) };
        var failures = r.Validate();
        Assert.NotNull(failures);
        Assert.Contains(failures!, f => f.FieldPath == "$originalRef.OrgInvDate" && f.RuleId == "FR-059");
    }

    // ===== Replacement use-case validation tests (T-RP-02 .. T-RP-08) =====

    [Fact]
    public async Task Replacement_Missing_OrgRefID_returns_Validation_no_network()
    {
        var (sut, stub) = MakeReplacementSut();
        var request = new ReplacementRequest(
            SampleInvoices.TypicalVatInvoice(),
            ValidOriginalRef with { OrgRefID = "" },
            "Sửa thông tin",
            InvoiceWithCode: true);

        var ex = await Assert.ThrowsAsync<MeInvoiceException>(() => sut.ExecuteAsync(request, default));
        Assert.Equal(MeInvoiceErrorCategory.Validation, ex.Category);
        Assert.Equal("ValidationFailed", ex.RawErrorCode);
        Assert.NotNull(ex.Failures);
        Assert.Contains(ex.Failures!, f => f.FieldPath == "$originalRef.OrgRefID" && f.RuleId == "FR-058");
        Assert.Equal(0, stub.CallCount);
    }

    [Fact]
    public async Task Replacement_Missing_OrgInvNo_returns_Validation_no_network()
    {
        var (sut, stub) = MakeReplacementSut();
        var request = new ReplacementRequest(
            SampleInvoices.TypicalVatInvoice(),
            ValidOriginalRef with { OrgInvNo = "" },
            "Sửa thông tin",
            InvoiceWithCode: true);
        var ex = await Assert.ThrowsAsync<MeInvoiceException>(() => sut.ExecuteAsync(request, default));
        Assert.Contains(ex.Failures!, f => f.FieldPath == "$originalRef.OrgInvNo" && f.RuleId == "FR-059");
        Assert.Equal(0, stub.CallCount);
    }

    [Fact]
    public async Task Replacement_Missing_OrgInvTemplateNo_returns_Validation_no_network()
    {
        var (sut, stub) = MakeReplacementSut();
        var request = new ReplacementRequest(
            SampleInvoices.TypicalVatInvoice(),
            ValidOriginalRef with { OrgInvTemplateNo = "" },
            "Sửa thông tin",
            InvoiceWithCode: true);
        var ex = await Assert.ThrowsAsync<MeInvoiceException>(() => sut.ExecuteAsync(request, default));
        Assert.Contains(ex.Failures!, f => f.FieldPath == "$originalRef.OrgInvTemplateNo" && f.RuleId == "FR-059");
        Assert.Equal(0, stub.CallCount);
    }

    [Fact]
    public async Task Replacement_Missing_OrgInvSeries_returns_Validation_no_network()
    {
        var (sut, stub) = MakeReplacementSut();
        var request = new ReplacementRequest(
            SampleInvoices.TypicalVatInvoice(),
            ValidOriginalRef with { OrgInvSeries = "" },
            "Sửa thông tin",
            InvoiceWithCode: true);
        var ex = await Assert.ThrowsAsync<MeInvoiceException>(() => sut.ExecuteAsync(request, default));
        Assert.Contains(ex.Failures!, f => f.FieldPath == "$originalRef.OrgInvSeries" && f.RuleId == "FR-059");
        Assert.Equal(0, stub.CallCount);
    }

    [Fact]
    public async Task Replacement_Default_OrgInvDate_returns_Validation_no_network()
    {
        var (sut, stub) = MakeReplacementSut();
        var request = new ReplacementRequest(
            SampleInvoices.TypicalVatInvoice(),
            ValidOriginalRef with { OrgInvDate = default(DateOnly) },
            "Sửa thông tin",
            InvoiceWithCode: true);
        var ex = await Assert.ThrowsAsync<MeInvoiceException>(() => sut.ExecuteAsync(request, default));
        Assert.Contains(ex.Failures!, f => f.FieldPath == "$originalRef.OrgInvDate" && f.RuleId == "FR-059");
        Assert.Equal(0, stub.CallCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public async Task Replacement_Whitespace_ChangeReason_returns_Validation_no_network(string changeReason)
    {
        var (sut, stub) = MakeReplacementSut();
        var request = new ReplacementRequest(
            SampleInvoices.TypicalVatInvoice(),
            ValidOriginalRef,
            changeReason,
            InvoiceWithCode: true);
        var ex = await Assert.ThrowsAsync<MeInvoiceException>(() => sut.ExecuteAsync(request, default));
        Assert.Contains(ex.Failures!, f => f.FieldPath == "$changeReason" && f.RuleId == "FR-060");
        Assert.Equal(0, stub.CallCount);
    }

    [Fact]
    public async Task Replacement_Multiple_missing_fields_aggregate_into_one_failure_list()
    {
        var (sut, stub) = MakeReplacementSut();
        var request = new ReplacementRequest(
            SampleInvoices.TypicalVatInvoice(),
            ValidOriginalRef with { OrgRefID = "", OrgInvNo = "" },
            "Sửa thông tin",
            InvoiceWithCode: true);
        var ex = await Assert.ThrowsAsync<MeInvoiceException>(() => sut.ExecuteAsync(request, default));
        Assert.Equal(2, ex.Failures!.Count);
        Assert.Equal(0, stub.CallCount);
    }

    // ===== Adjustment validation tests (T-AJ-05 .. T-AJ-10) =====

    [Fact]
    public async Task Adjustment_missing_OrgRefID_returns_Validation_no_network()
    {
        var (sut, stub) = MakeAdjustmentSut();
        var request = new AdjustmentRequest(
            SampleInvoices.TypicalVatInvoice(),
            ValidOriginalRef with { OrgRefID = "" },
            "Bổ sung phụ thu",
            InvoiceWithCode: true);
        var ex = await Assert.ThrowsAsync<MeInvoiceException>(() => sut.ExecuteAsync(request, default));
        Assert.Contains(ex.Failures!, f => f.FieldPath == "$originalRef.OrgRefID" && f.RuleId == "FR-058");
        Assert.Equal(0, stub.CallCount);
    }

    [Fact]
    public async Task Adjustment_missing_OrgInvNo_returns_Validation_no_network()
    {
        var (sut, stub) = MakeAdjustmentSut();
        var request = new AdjustmentRequest(
            SampleInvoices.TypicalVatInvoice(),
            ValidOriginalRef with { OrgInvNo = "" },
            "x", InvoiceWithCode: true);
        var ex = await Assert.ThrowsAsync<MeInvoiceException>(() => sut.ExecuteAsync(request, default));
        Assert.Contains(ex.Failures!, f => f.FieldPath == "$originalRef.OrgInvNo" && f.RuleId == "FR-059");
        Assert.Equal(0, stub.CallCount);
    }

    [Fact]
    public async Task Adjustment_missing_OrgInvTemplateNo_returns_Validation_no_network()
    {
        var (sut, stub) = MakeAdjustmentSut();
        var request = new AdjustmentRequest(
            SampleInvoices.TypicalVatInvoice(),
            ValidOriginalRef with { OrgInvTemplateNo = "" },
            "x", InvoiceWithCode: true);
        var ex = await Assert.ThrowsAsync<MeInvoiceException>(() => sut.ExecuteAsync(request, default));
        Assert.Contains(ex.Failures!, f => f.FieldPath == "$originalRef.OrgInvTemplateNo" && f.RuleId == "FR-059");
        Assert.Equal(0, stub.CallCount);
    }

    [Fact]
    public async Task Adjustment_missing_OrgInvSeries_returns_Validation_no_network()
    {
        var (sut, stub) = MakeAdjustmentSut();
        var request = new AdjustmentRequest(
            SampleInvoices.TypicalVatInvoice(),
            ValidOriginalRef with { OrgInvSeries = "" },
            "x", InvoiceWithCode: true);
        var ex = await Assert.ThrowsAsync<MeInvoiceException>(() => sut.ExecuteAsync(request, default));
        Assert.Contains(ex.Failures!, f => f.FieldPath == "$originalRef.OrgInvSeries" && f.RuleId == "FR-059");
        Assert.Equal(0, stub.CallCount);
    }

    [Fact]
    public async Task Adjustment_default_OrgInvDate_returns_Validation_no_network()
    {
        var (sut, stub) = MakeAdjustmentSut();
        var request = new AdjustmentRequest(
            SampleInvoices.TypicalVatInvoice(),
            ValidOriginalRef with { OrgInvDate = default(DateOnly) },
            "x", InvoiceWithCode: true);
        var ex = await Assert.ThrowsAsync<MeInvoiceException>(() => sut.ExecuteAsync(request, default));
        Assert.Contains(ex.Failures!, f => f.FieldPath == "$originalRef.OrgInvDate" && f.RuleId == "FR-059");
        Assert.Equal(0, stub.CallCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Adjustment_whitespace_ChangeReason_returns_Validation_no_network(string changeReason)
    {
        var (sut, stub) = MakeAdjustmentSut();
        var request = new AdjustmentRequest(
            SampleInvoices.TypicalVatInvoice(),
            ValidOriginalRef,
            changeReason, InvoiceWithCode: true);
        var ex = await Assert.ThrowsAsync<MeInvoiceException>(() => sut.ExecuteAsync(request, default));
        Assert.Contains(ex.Failures!, f => f.FieldPath == "$changeReason" && f.RuleId == "FR-060");
        Assert.Equal(0, stub.CallCount);
    }

    // ===== Helpers =====

    private static (IssueReplacementInvoice Sut, AmendmentStubClient Stub) MakeReplacementSut()
    {
        var stub = new AmendmentStubClient();
        var resolver = new StubResolver(new TemplateResolution.Resolved(DefaultTemplate));
        var ensure = new EnsureAccessToken(stub, new InMemoryTokenCache(), new SystemClock(TimeProvider.System), () => "key");
        var sut = new IssueReplacementInvoice(stub, ensure, new InvoiceValidator(), resolver, new GuidRefIdGenerator());
        return (sut, stub);
    }

    private static (IssueAdjustmentInvoice Sut, AmendmentStubClient Stub) MakeAdjustmentSut()
    {
        var stub = new AmendmentStubClient();
        var resolver = new StubResolver(new TemplateResolution.Resolved(DefaultTemplate));
        var ensure = new EnsureAccessToken(stub, new InMemoryTokenCache(), new SystemClock(TimeProvider.System), () => "key");
        var sut = new IssueAdjustmentInvoice(stub, ensure, new InvoiceValidator(), resolver, new GuidRefIdGenerator());
        return (sut, stub);
    }

    internal sealed class AmendmentStubClient : IMeInvoiceClient
    {
        public int CallCount { get; private set; }
        public Invoice? LastInvoice { get; private set; }
        public OriginalInvoiceReference? LastOriginalRef { get; private set; }
        public string? LastChangeReason { get; private set; }
        public string? LastEInvoiceStatus { get; private set; }
        public SaveResult? NextResult { get; set; }

        public Task<AccessToken> AcquireTokenAsync(CancellationToken ct) =>
            Task.FromResult(new AccessToken("t", DateTimeOffset.UtcNow.AddDays(1)));
        public Task<IReadOnlyList<Template>> ListTemplatesAsync(bool invoiceWithCode, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Template>>(new[] { DefaultTemplate });
        public Task<PdfDocument> PreviewAsync(Invoice invoice, bool invoiceWithCode, CancellationToken ct) =>
            throw new NotImplementedException();
        public Task<IReadOnlyList<SaveResult>> SaveDraftAsync(BatchSubmission batch, bool invoiceWithCode, CancellationToken ct) =>
            throw new NotImplementedException();
        public Task<PdfDocument> GetDraftPdfByRefIdAsync(RefId refId, bool invoiceWithCode, CancellationToken ct) =>
            throw new NotImplementedException();
        public Task<DeleteResponse> DeleteDraftAsync(RefId refId, bool invoiceWithCode, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<SaveResult> IssueReplacementAsync(
            Invoice invoice,
            OriginalInvoiceReference originalRef,
            string changeReason,
            bool invoiceWithCode,
            CancellationToken ct)
        {
            CallCount++;
            LastInvoice = invoice;
            LastOriginalRef = originalRef;
            LastChangeReason = changeReason;
            LastEInvoiceStatus = "3";
            return Task.FromResult(NextResult ?? new SaveResult(invoice.RefId, SaveOutcome.Success));
        }

        public Task<SaveResult> IssueAdjustmentAsync(
            Invoice invoice,
            OriginalInvoiceReference originalRef,
            string changeReason,
            bool invoiceWithCode,
            CancellationToken ct)
        {
            CallCount++;
            LastInvoice = invoice;
            LastOriginalRef = originalRef;
            LastChangeReason = changeReason;
            LastEInvoiceStatus = "4";
            return Task.FromResult(NextResult ?? new SaveResult(invoice.RefId, SaveOutcome.Success));
        }
    }

    internal sealed class StubResolver : ITemplateResolver
    {
        private readonly TemplateResolution _result;
        public StubResolver(TemplateResolution result) => _result = result;
        public Task<TemplateResolution> ResolveAsync(TemplateRef? callerSupplied, bool invoiceWithCode, CancellationToken ct) =>
            Task.FromResult(_result);
    }
}
