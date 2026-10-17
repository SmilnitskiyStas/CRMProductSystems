using System.Collections.Concurrent;
using Anthropic.Exceptions;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using ShelfGuard.Application.Services;

namespace ShelfGuard.Infrastructure.AI;

/// <summary>
/// Circuit breaker (+ optional single retry) around an AI provider call. One breaker per
/// provider endpoint — NOT per tenant key — so a tenant's bad key or exhausted credit (4xx,
/// not counted) can never open the circuit for others, while a real provider outage (5xx,
/// network, timeout) makes the endpoint fail fast for everyone. Open circuit surfaces as
/// <see cref="ExternalServiceUnavailableException"/>; the AI services already degrade on errors.
/// </summary>
internal static class AiResilience
{
    private static readonly ConcurrentDictionary<string, ResiliencePipeline> Pipelines = new();

    public static async Task<T> ExecuteAsync<T>(
        string endpointKey, bool retry, Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        var pipeline = Pipelines.GetOrAdd(endpointKey, _ => Build(retry));
        try
        {
            return await pipeline.ExecuteAsync(async token => await action(token), ct);
        }
        catch (BrokenCircuitException ex)
        {
            throw new ExternalServiceUnavailableException(
                "ai", "AI provider is temporarily unavailable. Try again shortly.", ex);
        }
    }

    private const string StatusKey = "httpStatus";

    /// <summary>
    /// A provider non-2xx answer. Stays a plain <see cref="InvalidOperationException"/> (callers and
    /// tests match that exact type and the provider's raw message); the status rides in
    /// <c>Data</c> so the breaker can tell a 5xx outage from a bad key.
    /// </summary>
    internal static InvalidOperationException ProviderError(string message, int httpStatus)
    {
        var ex = new InvalidOperationException(message);
        ex.Data[StatusKey] = httpStatus;
        return ex;
    }

    /// <summary>For tests: drop all breaker state.</summary>
    internal static void Reset() => Pipelines.Clear();

    private static ResiliencePipeline Build(bool retry)
    {
        var builder = new ResiliencePipelineBuilder();

        if (retry)
        {
            // Fast failures only (5xx / network) — a 60s timeout is never retried.
            builder.AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 1,
                Delay = TimeSpan.FromMilliseconds(500),
                UseJitter = true,
                ShouldHandle = args => ValueTask.FromResult(
                    args.Outcome.Exception is { } ex
                    && IsTransient(ex, args.Context.CancellationToken)
                    && ex is not OperationCanceledException
                    && ex is not TimeoutException),
            });
        }

        builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions
        {
            FailureRatio = 0.5,
            MinimumThroughput = 5,
            SamplingDuration = TimeSpan.FromSeconds(60),
            BreakDuration = TimeSpan.FromSeconds(30),
            ShouldHandle = args => ValueTask.FromResult(
                args.Outcome.Exception is { } ex && IsTransient(ex, args.Context.CancellationToken)),
        });

        return builder.Build();
    }

    /// <summary>Provider-side/transport failures only: 5xx, 408, network, timeout. Never 4xx (bad key, quota).</summary>
    internal static bool IsTransient(Exception ex, CancellationToken ct) => ex switch
    {
        OperationCanceledException => !ct.IsCancellationRequested,
        InvalidOperationException { Data: { } d } when d[StatusKey] is int status => status >= 500 || status == 408,
        Anthropic5xxException or AnthropicIOException => true,
        HttpRequestException or TimeoutException => true,
        _ => false,
    };
}
