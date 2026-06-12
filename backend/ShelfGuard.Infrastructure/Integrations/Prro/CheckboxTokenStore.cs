namespace ShelfGuard.Infrastructure.Integrations.Prro;

/// <summary>
/// Singleton cache for the cashier bearer token. The typed HttpClient is transient,
/// so the token must live outside it; one cashier session is shared process-wide.
/// </summary>
public sealed class CheckboxTokenStore
{
    private string? _token;

    /// <summary>Serializes signin so concurrent 401s don't trigger parallel re-auth.</summary>
    public SemaphoreSlim SigninLock { get; } = new(1, 1);

    public string? Token => Volatile.Read(ref _token);

    public void Set(string token) => Volatile.Write(ref _token, token);

    public void Invalidate() => Volatile.Write(ref _token, null);
}
