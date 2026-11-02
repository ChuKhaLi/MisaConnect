using System.Net;
using Microsoft.Extensions.Options;
using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.Infrastructure.Configuration;

namespace MisaConnect.ESign.Infrastructure.Http;

/// <summary>
/// Bounded exponential-backoff retry with full jitter (FR-023). Retries 5xx,
/// 429, transient <see cref="HttpRequestException"/>, and operation-timeout
/// <see cref="TaskCanceledException"/>. On 429 with a <c>Retry-After</c>
/// header, the header value (clamped to <c>MaxDelay</c>) replaces the computed
/// backoff. Caller cancellation propagates unchanged. On exhausted budget,
/// surfaces <see cref="ESignTransportException"/> per FR-025.
/// </summary>
internal sealed class TransientFailureRetryHandler : DelegatingHandler
{
    private readonly IOptions<MisaESignOptions> _options;
    private readonly IDelayer _delayer;
    private readonly ICorrelationIdAccessor _correlation;
    private readonly Func<int, int> _randomBudget;

    public TransientFailureRetryHandler(
        IOptions<MisaESignOptions> options,
        IDelayer delayer,
        ICorrelationIdAccessor correlation)
        : this(options, delayer, correlation, max => Random.Shared.Next(0, Math.Max(1, max)))
    {
    }

    internal TransientFailureRetryHandler(
        IOptions<MisaESignOptions> options,
        IDelayer delayer,
        ICorrelationIdAccessor correlation,
        Func<int, int> randomBudget)
    {
        _options = options;
        _delayer = delayer;
        _correlation = correlation;
        _randomBudget = randomBudget;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var retry = _options.Value.TransportRetry;
        var maxAttempts = Math.Max(1, retry.MaxAttempts);
        var baseDelayMs = (int)retry.BaseDelay.TotalMilliseconds;
        var maxDelayMs = (int)retry.MaxDelay.TotalMilliseconds;

        HttpResponseMessage? lastResponse = null;
        Exception? lastException = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                lastResponse?.Dispose();
                lastResponse = null;
                lastException = null;

                var resp = await base.SendAsync(CloneRequest(request), cancellationToken).ConfigureAwait(false);
                if (!IsTransient(resp.StatusCode))
                {
                    return resp;
                }

                lastResponse = resp;
            }
            catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (TaskCanceledException ex)
            {
                lastException = ex;
            }
            catch (HttpRequestException ex)
            {
                lastException = ex;
            }

            if (attempt == maxAttempts) break;

            var delayMs = ComputeBackoffMs(attempt, baseDelayMs, maxDelayMs, lastResponse);
            await _delayer.DelayAsync(TimeSpan.FromMilliseconds(delayMs), cancellationToken).ConfigureAwait(false);
        }

        var statusCode = lastResponse?.StatusCode;
        lastResponse?.Dispose();
        throw new ESignTransportException(
            lastStatusCode: statusCode,
            attemptCount: maxAttempts,
            detail: BuildExhaustedDetail(statusCode, lastException, maxAttempts),
            correlationId: _correlation.Current,
            inner: lastException);
    }

    private int ComputeBackoffMs(int attempt, int baseDelayMs, int maxDelayMs, HttpResponseMessage? response)
    {
        if (response is not null && response.StatusCode == (HttpStatusCode)429)
        {
            var retryAfter = response.Headers.RetryAfter;
            if (retryAfter is not null)
            {
                if (retryAfter.Delta is { } delta)
                {
                    return Math.Min((int)delta.TotalMilliseconds, maxDelayMs);
                }
                if (retryAfter.Date is { } date)
                {
                    var ms = (int)(date.ToUniversalTime() - DateTimeOffset.UtcNow).TotalMilliseconds;
                    if (ms > 0) return Math.Min(ms, maxDelayMs);
                }
            }
        }

        var capped = Math.Min(maxDelayMs, baseDelayMs * (1 << Math.Min(attempt - 1, 16)));
        return _randomBudget(Math.Max(1, capped));
    }

    private static bool IsTransient(HttpStatusCode code) =>
        code == HttpStatusCode.TooManyRequests || (int)code >= 500;

    private static string BuildExhaustedDetail(HttpStatusCode? lastStatus, Exception? lastException, int attempts)
    {
        if (lastStatus is not null)
        {
            return $"MISA eSign transport retries exhausted (attempts={attempts}, lastStatus={(int)lastStatus.Value}).";
        }
        if (lastException is not null)
        {
            return $"MISA eSign transport retries exhausted (attempts={attempts}, lastException={lastException.GetType().Name}).";
        }
        return $"MISA eSign transport retries exhausted (attempts={attempts}).";
    }

    private static HttpRequestMessage CloneRequest(HttpRequestMessage source)
    {
        var clone = new HttpRequestMessage(source.Method, source.RequestUri)
        {
            Version = source.Version,
            VersionPolicy = source.VersionPolicy,
            Content = source.Content,
        };
        foreach (var header in source.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        return clone;
    }
}
