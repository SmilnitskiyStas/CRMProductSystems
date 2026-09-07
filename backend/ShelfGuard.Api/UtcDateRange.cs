namespace ShelfGuard.Api;

/// <summary>
/// Normalizes date-only query parameters before they are used against PostgreSQL
/// <c>timestamp with time zone</c> columns.
/// </summary>
public static class UtcDateRange
{
    /// <summary>
    /// Treats the supplied calendar date as the beginning of that day in UTC.
    /// ASP.NET binds a value such as <c>2026-09-07</c> as an Unspecified
    /// <see cref="DateTime"/>, which Npgsql cannot send to a <c>timestamptz</c>
    /// parameter without this normalization.
    /// </summary>
    public static DateTime? StartOfDay(DateTime? value) => value is DateTime date
        ? DateTime.SpecifyKind(date.Date, DateTimeKind.Utc)
        : null;
}
