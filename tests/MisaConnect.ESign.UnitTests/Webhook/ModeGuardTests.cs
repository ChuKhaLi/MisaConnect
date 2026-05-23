using Microsoft.Extensions.Options;
using MisaConnect.ESign.Application.UseCases;
using MisaConnect.ESign.Application.Validation;
using MisaConnect.ESign.Client;
using MisaConnect.ESign.Client.Dtos;
using MisaConnect.ESign.Infrastructure.Configuration;
using Xunit;

namespace MisaConnect.ESign.UnitTests.Webhook;

public class ModeGuardTests
{
    private static MisaESignClient BuildClient(WebhookMode mode)
    {
        var options = Options.Create(new MisaESignOptions
        {
            Webhook = new MisaESignWebhookOptions { Mode = mode },
        });
        return new MisaESignClient(
            signPdf: null!,
            signXml: null!,
            signWord: null!,
            signExcel: null!,
            exchangeOtp: null!,
            resendOtp: null!,
            otpSubmissionValidator: null!,
            options: options);
    }

    [Fact]
    public async Task SignPdfAsync_throws_when_mode_is_Webhook()
    {
        var client = BuildClient(WebhookMode.Webhook);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.SignPdfAsync(StubRequest(), CancellationToken.None));
    }

    [Fact]
    public async Task SignXmlAsync_throws_when_mode_is_Webhook()
    {
        var client = BuildClient(WebhookMode.Webhook);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.SignXmlAsync(StubXmlRequest(), CancellationToken.None));
    }

    [Fact]
    public async Task SignWordAsync_throws_when_mode_is_Webhook()
    {
        var client = BuildClient(WebhookMode.Webhook);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.SignWordAsync(StubWordRequest(), CancellationToken.None));
    }

    [Fact]
    public async Task SignExcelAsync_throws_when_mode_is_Webhook()
    {
        var client = BuildClient(WebhookMode.Webhook);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.SignExcelAsync(StubExcelRequest(), CancellationToken.None));
    }

    [Fact]
    public async Task BeginSignPdfAsync_throws_when_mode_is_Polling()
    {
        var client = BuildClient(WebhookMode.Polling);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.BeginSignPdfAsync(StubRequest(), CancellationToken.None));
    }

    [Fact]
    public async Task BeginSignXmlAsync_throws_when_mode_is_Polling()
    {
        var client = BuildClient(WebhookMode.Polling);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.BeginSignXmlAsync(StubXmlRequest(), CancellationToken.None));
    }

    [Fact]
    public async Task BeginSignWordAsync_throws_when_mode_is_Polling()
    {
        var client = BuildClient(WebhookMode.Polling);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.BeginSignWordAsync(StubWordRequest(), CancellationToken.None));
    }

    [Fact]
    public async Task BeginSignExcelAsync_throws_when_mode_is_Polling()
    {
        var client = BuildClient(WebhookMode.Polling);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.BeginSignExcelAsync(StubExcelRequest(), CancellationToken.None));
    }

    private static SignPdfRequestDto StubRequest() => new(
        Pdf: new byte[] { 0x25, 0x50 },
        DocumentName: "doc",
        SignerName: "Alice",
        Location: "Hanoi",
        Reason: "Test",
        Contact: "a@b.com",
        LogoImageBase64: "logo",
        DataToBeDisplayed: "show");

    private static SignXmlRequestDto StubXmlRequest() => SignXmlRequest.FromString(
        xml: "<doc/>",
        signatureContext: new XmlSignatureContextDto(
            SignatureName: "sig",
            HashAlgorithm: "SHA256",
            SignatureDescription: new SignatureDescriptionDto(
                SignedBy: "Alice",
                Location: "Hanoi",
                Reason: "Test",
                Contact: "a@b.com")),
        documentName: "doc",
        dataToBeDisplayed: "show",
        documentId: "doc-1");

    private static SignWordRequestDto StubWordRequest() => new(
        Word: new byte[] { 0x50, 0x4B, 0x03, 0x04 },
        DocumentName: "doc",
        SignerName: "Alice",
        Location: "Hanoi",
        Reason: "Test",
        Contact: "a@b.com",
        LogoImageBase64: "logo",
        DataToBeDisplayed: "show");

    private static SignExcelRequestDto StubExcelRequest() => new(
        Excel: new byte[] { 0x50, 0x4B, 0x03, 0x04 },
        DocumentName: "doc",
        SignerName: "Alice",
        Location: "Hanoi",
        Reason: "Test",
        Contact: "a@b.com",
        LogoImageBase64: "logo",
        DataToBeDisplayed: "show");
}
