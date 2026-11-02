using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using MisaConnect.EInvoice.Application.Abstractions;
using MisaConnect.EInvoice.Domain.Invoices;
using MisaConnect.EInvoice.Infrastructure.Configuration;
using MisaConnect.EInvoice.Infrastructure.MeInvoice;
using Xunit;

namespace MisaConnect.EInvoice.UnitTests.Infrastructure;

/// <summary>
/// Slice 2 T018 + T019 — assert the wire request shape (DELETE method,
/// query-string parameters, no body) and the parsing of MISA's success
/// envelope into <see cref="DeleteResponse"/>. The handler chain (bearer
/// token attach, throttle/retry) is not exercised here; this test focuses
/// on the concrete <see cref="MeInvoiceClient"/> only.
/// </summary>
public class MeInvoiceClientDeleteTests
{
    [Fact]
    public async Task Composes_wire_url_with_query_params()
    {
        HttpRequestMessage? captured = null;
        var handler = new CapturingHandler(_ =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(@"{""success"":true,""errorCode"":null}", Encoding.UTF8, "application/json"),
            };
        }, r => captured = r);

        var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://testapi.meinvoice.vn/api/integration/"),
        };
        var client = new MeInvoiceClient(http, MakeOptions());

        var result = await client.DeleteDraftAsync(RefId.From("b195f5f7-7133-4898-9370-0e701b4a467f"), true, default);

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Delete, captured!.Method);
        Assert.NotNull(captured.RequestUri);
        var url = captured.RequestUri!.ToString();
        Assert.Contains("webapp/delete?invoiceWithCode=true&refid=b195f5f7-7133-4898-9370-0e701b4a467f", url);
        Assert.Null(captured.Content);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task Composes_wire_url_with_invoiceWithCode_false_when_caller_passes_false()
    {
        HttpRequestMessage? captured = null;
        var handler = new CapturingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(@"{""success"":true}", Encoding.UTF8, "application/json"),
            },
            r => captured = r);
        var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://testapi.meinvoice.vn/api/integration/"),
        };
        var client = new MeInvoiceClient(http, MakeOptions());

        await client.DeleteDraftAsync(RefId.From("r1"), false, default);

        Assert.Contains("invoiceWithCode=false", captured!.RequestUri!.ToString());
    }

    [Fact]
    public async Task Parses_success_envelope_into_DeleteResponse()
    {
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                @"{""success"":true,""errorCode"":null,""descriptionErrorCode"":null,""errors"":null,""data"":null,""customData"":null}",
                Encoding.UTF8,
                "application/json"),
        });

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://testapi.meinvoice.vn/api/integration/") };
        var client = new MeInvoiceClient(http, MakeOptions());

        var result = await client.DeleteDraftAsync(RefId.From("abc"), true, default);

        Assert.True(result.Success);
        Assert.Null(result.ErrorCode);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task Parses_failure_envelope_with_InvalidTransactionID_and_ErrorMessage()
    {
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                @"{""success"":false,""errorCode"":""InvalidTransactionID"",""ErrorMessage"":""Hóa đơn đã phát hành nên không thể xóa.""}",
                Encoding.UTF8,
                "application/json"),
        });

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://testapi.meinvoice.vn/api/integration/") };
        var client = new MeInvoiceClient(http, MakeOptions());

        var result = await client.DeleteDraftAsync(RefId.From("abc"), true, default);

        Assert.False(result.Success);
        Assert.Equal("InvalidTransactionID", result.ErrorCode);
        Assert.Equal("Hóa đơn đã phát hành nên không thể xóa.", result.ErrorMessage);
    }

    private static IOptions<MisaEInvoiceOptions> MakeOptions() => Options.Create(new MisaEInvoiceOptions
    {
        Environment = MeInvoiceEnvironment.Sandbox,
        BaseUrl = "https://testapi.meinvoice.vn/api/integration",
        TaxCode = "0000000000",
        AppId = "267",
        UserName = "u",
        Password = "p",
    });

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        private readonly Action<HttpRequestMessage>? _capture;

        public CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond, Action<HttpRequestMessage>? capture = null)
        {
            _respond = respond;
            _capture = capture;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _capture?.Invoke(request);
            return Task.FromResult(_respond(request));
        }
    }
}
