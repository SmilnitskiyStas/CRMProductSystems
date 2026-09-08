namespace ShelfGuard.Domain.Interfaces;

/// <summary>Result of resolving a free-text address to a point on the map.</summary>
public sealed record GeocodeResult(decimal Latitude, decimal Longitude, string DisplayName);

/// <summary>
/// Address → coordinates. Implemented by OpenStreetMap Nominatim in Infrastructure.
/// Isolated behind this interface so the geocoding provider never leaks into business logic.
/// </summary>
public interface IGeocodingClient
{
    Task<GeocodeResult?> GeocodeAsync(string query, CancellationToken ct = default);
}
