using Microsoft.Extensions.Caching.Memory;
using ShelfGuard.Application.Services;

namespace ShelfGuard.Infrastructure.Services;

/// <summary>
/// IMemoryCache-backed implementation of <see cref="IResolveCodeAttemptTracker"/> (TASK-405).
/// See the interface doc for the single-instance-deployment tradeoff.
/// </summary>
public sealed class MemoryResolveCodeAttemptTracker : IResolveCodeAttemptTracker
{
    private sealed class State
    {
        public int FailedAttempts;
        public DateTimeOffset? LockoutUntil;
    }

    private readonly IMemoryCache _cache;

    public MemoryResolveCodeAttemptTracker(IMemoryCache cache) => _cache = cache;

    private static string Key(Guid membershipId) => $"loyalty-resolve-attempts:{membershipId:D}";

    public bool IsLockedOut(Guid membershipId)
    {
        if (!_cache.TryGetValue<State>(Key(membershipId), out var state) || state is null)
            return false;

        return state.LockoutUntil.HasValue && state.LockoutUntil.Value > DateTimeOffset.UtcNow;
    }

    public bool RegisterFailure(Guid membershipId, int maxAttempts, TimeSpan lockoutDuration)
    {
        var key = Key(membershipId);
        var state = _cache.TryGetValue<State>(key, out var existing) && existing is not null
            ? existing
            : new State();

        state.FailedAttempts++;
        var justLockedOut = false;
        if (state.FailedAttempts >= maxAttempts)
        {
            state.LockoutUntil = DateTimeOffset.UtcNow.Add(lockoutDuration);
            state.FailedAttempts = 0;
            justLockedOut = true;
        }

        // Absolute expiry = lockout window from now — self-cleans; a quiet membership
        // never accumulates state forever.
        _cache.Set(key, state, lockoutDuration);
        return justLockedOut;
    }

    public void Reset(Guid membershipId) => _cache.Remove(Key(membershipId));
}
