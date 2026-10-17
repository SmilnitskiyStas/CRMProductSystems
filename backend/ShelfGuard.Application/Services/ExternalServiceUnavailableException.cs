namespace ShelfGuard.Application.Services;

/// <summary>
/// An optional third-party dependency (AI provider, weather, geocoder…) is down or its circuit
/// breaker is open. Callers that can degrade catch it; anything that reaches the API boundary is
/// mapped to a clean 503 (not a 500) by <c>GlobalExceptionHandler</c>.
/// </summary>
public sealed class ExternalServiceUnavailableException : Exception
{
    /// <summary>Short stable key of the dependency, e.g. "ai", "weather", "geocoder".</summary>
    public string Service { get; }

    public ExternalServiceUnavailableException(string service, string message, Exception? inner = null)
        : base(message, inner) => Service = service;
}
