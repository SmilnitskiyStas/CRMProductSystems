namespace ShelfGuard.Application.Features.Pos.Fiscal;

/// <summary>
/// Raised when the fiscal provider rejects a request or is unreachable.
/// <see cref="IsTransient"/> tells the retry job whether re-submitting later makes sense
/// (timeouts, 5xx, network) vs. a permanent rejection (validation, bad credentials).
/// </summary>
public sealed class FiscalProviderException : Exception
{
    /// <summary>Provider-specific error code, e.g. Checkbox "cashier.invalid_credentials".</summary>
    public string? ProviderCode { get; }

    public int? HttpStatus { get; }

    public bool IsTransient { get; }

    public FiscalProviderException(
        string message,
        string? providerCode = null,
        int? httpStatus = null,
        bool isTransient = false,
        Exception? inner = null)
        : base(message, inner)
    {
        ProviderCode = providerCode;
        HttpStatus = httpStatus;
        IsTransient = isTransient;
    }
}
