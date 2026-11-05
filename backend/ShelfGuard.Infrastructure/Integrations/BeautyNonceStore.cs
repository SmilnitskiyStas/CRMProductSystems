using Microsoft.Extensions.Caching.Memory;
using ShelfGuard.Application.Features.BeautyControl;

namespace ShelfGuard.Infrastructure.Integrations;

public sealed class BeautyInboundKeyProvider(
    Microsoft.Extensions.Options.IOptionsMonitor<BeautyFeedbackInboundOptions> options) : IBeautyInboundKeyProvider
{
    public bool TryGet(string kid, out BeautyInboundKey key)
    {
        if (options.CurrentValue.Keys.TryGetValue(kid, out var k) && k is not null) { key = k; return true; }
        key = new BeautyInboundKey();
        return false;
    }
}

/// <summary>
/// In-process nonce replay guard (the inbound endpoint lives on a single provider instance).
/// A restart only reopens the +-300 s window; feedbackId idempotency still prevents duplicate leads.
/// </summary>
public sealed class BeautyNonceStore(IMemoryCache cache) : IBeautyNonceStore
{
    private readonly object _gate = new();

    public bool TryRegister(string kid, string nonce, TimeSpan ttl)
    {
        var key = $"beauty-nonce:{kid}:{nonce}";
        lock (_gate)
        {
            if (cache.TryGetValue(key, out _)) return false;
            cache.Set(key, true, ttl);
            return true;
        }
    }
}
