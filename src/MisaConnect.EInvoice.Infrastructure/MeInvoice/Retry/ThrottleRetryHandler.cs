using System.Net;
using MisaConnect.EInvoice.Domain.Errors;

namespace MisaConnect.EInvoice.Infrastructure.MeInvoice.Retry;

/// <summary>
/// Single bounded retry on transient MISA failures (HTTP 429 / 5xx / timeout)
/// per FR-026 + research R-5. Hand-rolled; no Polly.
/// </summary>
public sealed class ThrottleRetryHandler : DelegatingHandler
{
    private const int BackoffBaseMs = 1250;
    private const int BackoffJitterMs = 500;

    private readonly Func<int, Task> _delay;

    public ThrottleRetryHandler() : this(static ms => Task.Delay(ms)) { }

    internal ThrottleRetryHandler(Func<int, Task> delay) => _delay = delay;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!ShouldRetry(response.StatusCode))
            {
                return response;
            }

            response.Dispose();
            await _delay(NextBackoffMs()).ConfigureAwait(false);

            var retry = await base.SendAsync(CloneRequest(request), cancellationToken).ConfigureAwait(false);
            if (!ShouldRetry(retry.StatusCode))
            {
                return retry;
            }

            var category = retry.StatusCode == HttpStatusCode.TooManyRequests
                ? MeInvoiceErrorCategory.MisaThrottled
                : MeInvoiceErrorCategory.MisaUnavailable;
            retry.Dispose();
            throw new MeInvoiceException(category, retry.StatusCode.ToString(), $"MISA call failed with status {(int)retry.StatusCode} after one retry.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await _delay(NextBackoffMs()).ConfigureAwait(false);
            try
            {
                var retry = await base.SendAsync(CloneRequest(request), cancellationToken).ConfigureAwait(false);
                if (!ShouldRetry(retry.StatusCode))
                {
                    return retry;
                }
                retry.Dispose();
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { }
            catch (HttpRequestException) { }
            // Operation-timeout (no response from MISA) — surface as TransportFailed
            // per research R-DEL-04. Caller-cancellation continues to propagate as
            // OperationCanceledException unchanged.
            throw new MeInvoiceException(MeInvoiceErrorCategory.TransportFailed, "Timeout", "MISA call timed out after one retry.");
        }
        catch (HttpRequestException ex)
        {
            await _delay(NextBackoffMs()).ConfigureAwait(false);
            try
            {
                var retry = await base.SendAsync(CloneRequest(request), cancellationToken).ConfigureAwait(false);
                if (!ShouldRetry(retry.StatusCode))
                {
                    return retry;
                }
                retry.Dispose();
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { }
            // No HTTP response received from MISA (DNS / TLS / connect refused / host
            // unreachable). Surface as TransportFailed per research R-DEL-04.
            throw new MeInvoiceException(MeInvoiceErrorCategory.TransportFailed, "ConnectionFailed", ex.Message, inner: ex);
        }
    }

    private static bool ShouldRetry(HttpStatusCode code) =>
        code == HttpStatusCode.TooManyRequests || (int)code >= 500;

    private static int NextBackoffMs() => BackoffBaseMs + Random.Shared.Next(0, BackoffJitterMs);

    private static HttpRequestMessage CloneRequest(HttpRequestMessage source)
    {
        var clone = new HttpRequestMessage(source.Method, source.RequestUri)
        {
            Version = source.Version,
            VersionPolicy = source.VersionPolicy,
        };
        if (source.Content is not null)
        {
            clone.Content = source.Content;
        }
        foreach (var header in source.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        return clone;
    }
}
