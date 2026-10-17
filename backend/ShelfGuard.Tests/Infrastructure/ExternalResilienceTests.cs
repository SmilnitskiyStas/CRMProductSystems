using System.Net;
using Microsoft.Extensions.DependencyInjection;
using ShelfGuard.Application.Services;
using ShelfGuard.Infrastructure.AI;
using ShelfGuard.Infrastructure.Resilience;
using Xunit;

namespace ShelfGuard.Tests.Infrastructure;

/// <summary>Stage 2 resilience: AI circuit breaker and the shared HTTP pipeline for optional dependencies.</summary>
public sealed class ExternalResilienceTests
{
    // ── AI breaker ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Ai_ProviderOutage_OpensCircuit_AndFailsFastWithoutCallingProvider()
    {
        var key = $"t-{Guid.NewGuid()}";
        var calls = 0;
        Task<int> Fail(CancellationToken _) { calls++; throw AiResilience.ProviderError("boom", 503); }

        for (var i = 0; i < 5; i++)
            await Assert.ThrowsAsync<InvalidOperationException>(() => AiResilience.ExecuteAsync(key, false, Fail, default));

        var callsBefore = calls;
        var ex = await Assert.ThrowsAsync<ExternalServiceUnavailableException>(
            () => AiResilience.ExecuteAsync(key, false, Fail, default));

        Assert.Equal("ai", ex.Service);
        Assert.Equal(callsBefore, calls); // open circuit: provider not touched
    }

    [Fact]
    public async Task Ai_ClientErrors_NeverOpenCircuit()
    {
        // A tenant's bad key / exhausted credit (401/402/429) must not take AI down for everyone.
        var key = $"t-{Guid.NewGuid()}";
        var calls = 0;
        Task<int> Unauthorized(CancellationToken _) { calls++; throw AiResilience.ProviderError("invalid_api_key", 401); }

        for (var i = 0; i < 12; i++)
            await Assert.ThrowsAsync<InvalidOperationException>(() => AiResilience.ExecuteAsync(key, true, Unauthorized, default));

        Assert.Equal(12, calls); // no retry for 4xx, circuit still closed
    }

    [Fact]
    public async Task Ai_TransientFailure_IsRetriedOnce()
    {
        var key = $"t-{Guid.NewGuid()}";
        var calls = 0;

        var result = await AiResilience.ExecuteAsync(key, true, _ =>
        {
            calls++;
            return calls == 1 ? throw AiResilience.ProviderError("bad gateway", 502) : Task.FromResult("ok");
        }, default);

        Assert.Equal("ok", result);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void Ai_IsTransient_Classification()
    {
        var live = CancellationToken.None;
        Assert.True(AiResilience.IsTransient(AiResilience.ProviderError("x", 500), live));
        Assert.True(AiResilience.IsTransient(new HttpRequestException("net"), live));
        Assert.False(AiResilience.IsTransient(AiResilience.ProviderError("x", 401), live));
        Assert.False(AiResilience.IsTransient(AiResilience.ProviderError("x", 429), live));
        Assert.False(AiResilience.IsTransient(new InvalidOperationException("x"), live));
    }

    // ── HTTP pipeline ──────────────────────────────────────────────────────

    private sealed class CountingHandler(Func<int, HttpStatusCode> status) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status(++Calls)));
    }

    private static HttpClient BuildClient(CountingHandler handler, int retries)
    {
        var services = new ServiceCollection();
        services.AddHttpClient("t")
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .AddUnavailableTranslation("weather")
            .AddExternalResilience("t", TimeSpan.FromSeconds(5), retries);
        return services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>().CreateClient("t");
    }

    [Fact]
    public async Task Http_RepeatedServerErrors_OpenCircuit_AndSurfaceAsUnavailable()
    {
        var handler = new CountingHandler(_ => HttpStatusCode.ServiceUnavailable);
        var http = BuildClient(handler, retries: 0);

        for (var i = 0; i < 5; i++)
            (await http.GetAsync("http://x/forecast")).Dispose();

        var callsBefore = handler.Calls;
        var ex = await Assert.ThrowsAsync<ExternalServiceUnavailableException>(
            () => http.GetAsync("http://x/forecast"));

        Assert.Equal("weather", ex.Service);
        Assert.Equal(callsBefore, handler.Calls);
    }

    [Fact]
    public async Task Http_Get_IsRetriedOnTransientFailure()
    {
        var handler = new CountingHandler(n => n == 1 ? HttpStatusCode.BadGateway : HttpStatusCode.OK);
        var http = BuildClient(handler, retries: 1);

        using var resp = await http.GetAsync("http://x/forecast");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Http_ClientErrors_DoNotOpenCircuit()
    {
        var handler = new CountingHandler(_ => HttpStatusCode.NotFound);
        var http = BuildClient(handler, retries: 0);

        for (var i = 0; i < 12; i++)
            (await http.GetAsync("http://x/forecast")).Dispose();

        Assert.Equal(12, handler.Calls);
    }
}
