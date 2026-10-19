using System.Collections.Concurrent;

namespace ShelfGuard.Application.Features.Promotions;

/// <summary>IANA store time zones (default Europe/Kyiv). Resolution is cached; "Europe/Kiev" is the legacy alias fallback.</summary>
public static class StoreTimeZones
{
    public const string Default = "Europe/Kyiv";

    private static readonly ConcurrentDictionary<string, TimeZoneInfo?> Cache = new(StringComparer.Ordinal);

    /// <summary>The zone for an IANA id; null when blank or unknown.</summary>
    public static TimeZoneInfo? Resolve(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        return Cache.GetOrAdd(id.Trim(), static key =>
        {
            if (TryFind(key, out var tz)) return tz;
            if (key == "Europe/Kyiv" && TryFind("Europe/Kiev", out tz)) return tz;
            if (key == "Europe/Kiev" && TryFind("Europe/Kyiv", out tz)) return tz;
            return null;
        });
    }

    public static bool IsValid(string? id) => Resolve(id) is not null;

    private static bool TryFind(string id, out TimeZoneInfo? tz)
    {
        try { tz = TimeZoneInfo.FindSystemTimeZoneById(id); return true; }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException) { tz = null; return false; }
    }
}
