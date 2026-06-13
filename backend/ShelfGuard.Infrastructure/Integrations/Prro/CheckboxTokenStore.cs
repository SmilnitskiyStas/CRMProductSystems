namespace ShelfGuard.Infrastructure.Integrations.Prro;

/// <summary>
/// Cache for one cash register's cashier bearer token. Clients are created per call
/// (IFiscalServiceFactory), so the token must live outside them; instances are held
/// per tenant + register in <see cref="CheckboxTokenStoreRegistry"/> (ADR-013).
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
