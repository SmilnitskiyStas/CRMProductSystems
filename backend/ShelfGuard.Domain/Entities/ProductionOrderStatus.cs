namespace ShelfGuard.Domain.Entities;

public enum ProductionOrderStatus
{
    Planned,
    InProgress,
    Done,
    Cancelled
}

/// <summary>
/// API wire format for <see cref="ProductionOrderStatus"/>: snake_case
/// (planned | in_progress | done | cancelled), as the web and mobile clients expect.
/// The DB column keeps the enum name (HasConversion&lt;string&gt;), so only the API edge converts.
/// </summary>
public static class ProductionOrderStatusExtensions
{
    public static string ToApiString(this ProductionOrderStatus status) => status switch
    {
        ProductionOrderStatus.InProgress => "in_progress",
        _ => status.ToString().ToLowerInvariant(),
    };

    /// <summary>Accepts snake_case ("in_progress") and the enum name ("InProgress"), case-insensitive.</summary>
    public static bool TryParseApi(string? value, out ProductionOrderStatus status)
    {
        status = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        return Enum.TryParse(value.Replace("_", string.Empty), ignoreCase: true, out status)
               && Enum.IsDefined(status);
    }
}
