using MisaConnect.ESign.Application.Sessions;
using MisaConnect.ESign.Application.UseCases;
using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.Domain.Signing;
using MisaConnect.ESign.UnitTests.TestSupport;
using Xunit;

namespace MisaConnect.ESign.UnitTests.Webhook;

public class BeginSignTests
{
    [Fact]
    public async Task BeginSignPdf_records_session_after_signing_hash_returns()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var wire = new StubWireClient();
        BeginSignFixtures.WireDefaults(wire, clock);
        var store = new BeginSignFixtures.StubSigningSessionStore();
        var sut = BeginSignFixtures.BuildBeginSignPdf(wire, store, clock, clientId: "client-1");

        var result = await sut.ExecuteAsync(BuildPdfWork(), CancellationToken.None);

        Assert.Equal("tx-fake-1", result.TransactionId);
        Assert.Equal(DocumentFormat.Pdf, result.Format);
        Assert.Equal(0, wire.GetStatusCalls);
        Assert.Equal(1, store.RegisterCalls);
        var session = Assert.Single(store.Registered);
        Assert.Equal("client-1", session.ClientId);
        Assert.Equal(DocumentFormat.Pdf, session.Format);
        Assert.IsType<PerFormatHashPayload.Pdf>(session.HashPayload);
        Assert.Contains("doc-1", session.RecordedDocumentIds);
    }

    [Fact]
    public async Task BeginSignXml_records_session_with_xml_payload()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var wire = new StubWireClient();
        BeginSignFixtures.WireDefaults(wire, clock);
        var store = new BeginSignFixtures.StubSigningSessionStore();
        var sut = BeginSignFixtures.BuildBeginSignXml(wire, store, clock);

        var result = await sut.ExecuteAsync(BuildXmlRequest(), CancellationToken.None);

        Assert.Equal(DocumentFormat.Xml, result.Format);
        Assert.Equal(0, wire.GetStatusCalls);
        var session = Assert.Single(store.Registered);
        Assert.IsType<PerFormatHashPayload.Xml>(session.HashPayload);
    }

    [Fact]
    public async Task BeginSignWord_records_session_with_word_payload()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var wire = new StubWireClient();
        BeginSignFixtures.WireDefaults(wire, clock);
        var store = new BeginSignFixtures.StubSigningSessionStore();
        var sut = BeginSignFixtures.BuildBeginSignWord(wire, store, clock);

        var result = await sut.ExecuteAsync(BuildWordRequest(), CancellationToken.None);

        Assert.Equal(DocumentFormat.Word, result.Format);
        Assert.Equal(0, wire.GetStatusCalls);
        var session = Assert.Single(store.Registered);
        Assert.IsType<PerFormatHashPayload.Word>(session.HashPayload);
    }

    [Fact]
    public async Task BeginSignExcel_records_session_with_excel_payload()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var wire = new StubWireClient();
        BeginSignFixtures.WireDefaults(wire, clock);
        var store = new BeginSignFixtures.StubSigningSessionStore();
        var sut = BeginSignFixtures.BuildBeginSignExcel(wire, store, clock);

        var result = await sut.ExecuteAsync(BuildExcelRequest(), CancellationToken.None);

        Assert.Equal(DocumentFormat.Excel, result.Format);
        Assert.Equal(0, wire.GetStatusCalls);
        var session = Assert.Single(store.Registered);
        Assert.IsType<PerFormatHashPayload.Excel>(session.HashPayload);
    }

    [Fact]
    public async Task BeginSignPdf_session_carries_configured_ttl_and_creation_clock()
    {
        var now = DateTimeOffset.UtcNow;
        var clock = new FakeClock(now);
        var wire = new StubWireClient();
        BeginSignFixtures.WireDefaults(wire, clock);
        var store = new BeginSignFixtures.StubSigningSessionStore();
        var ttl = TimeSpan.FromMinutes(15);
        var sut = BeginSignFixtures.BuildBeginSignPdf(wire, store, clock, ttl: ttl);

        await sut.ExecuteAsync(BuildPdfWork(), CancellationToken.None);

        var session = Assert.Single(store.Registered);
        Assert.Equal(ttl, session.Ttl);
        Assert.Equal(now, session.CreatedAtUtc);
    }

    private static SignPdfWorkRequest BuildPdfWork() => new(
        Pdf: new PdfDocument(new byte[] { 0x25, 0x50 }),
        SignatureInfo: BeginSignFixtures.PdfSignatureInfo(),
        DocumentName: "doc",
        DocumentId: "doc-1",
        DataToBeDisplayed: "to-display");

    private static SignXmlWorkRequest BuildXmlRequest() => new(
        Xml: "<doc/>",
        SignatureContext: BeginSignFixtures.XmlContext(),
        DocumentId: "doc-1",
        DocumentName: "doc",
        DataToBeDisplayed: "to-display");

    private static SignWordWorkRequest BuildWordRequest() => new(
        Word: new byte[] { 0x50, 0x4B, 0x03, 0x04 },
        SignatureInfo: BeginSignFixtures.PdfSignatureInfo(),
        DocumentId: "doc-1",
        DocumentName: "doc",
        DataToBeDisplayed: "to-display");

    private static SignExcelWorkRequest BuildExcelRequest() => new(
        Excel: new byte[] { 0x50, 0x4B, 0x03, 0x04 },
        SignatureInfo: BeginSignFixtures.PdfSignatureInfo(),
        DocumentId: "doc-1",
        DocumentName: "doc",
        DataToBeDisplayed: "to-display");
}
