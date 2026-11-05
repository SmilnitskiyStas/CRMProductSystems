using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Application.Features.Stock;

/// <summary>
/// Computes batch status from current observable state.
/// Used by service on every read to ensure accuracy independent of cron run cadence.
/// </summary>
public static class StockStatus
{
    public const string Safe = "safe";
    public const string Warning = "warning";
    public const string Critical = "critical";
    public const string Expired = "expired";
    public const string SoldOut = "sold_out";
    public const string NeedsVerification = "needs_verification";
    public const string TemperatureHold = "temp_violation";

    public static bool IsUsable(string status, decimal quantity, DateOnly expiry, DateOnly today) =>
        quantity > 0 && expiry > today && status is not (Expired or SoldOut or "archived" or TemperatureHold);

    public static bool IsUsable(ProductStock stock, DateOnly today) => IsUsable(stock.Status, stock.Quantity, stock.ExpiryDate, today);

    // Quantity restoration and ordinary reads must never release a quality hold.
    public static string Recalculate(ProductStock stock) => stock.Status is TemperatureHold or "archived"
        ? stock.Status : Compute(stock.Quantity, stock.ExpiryDate, stock.LastCheckedAt);

    public const int NeedsVerificationDays = 90;
    public const int CriticalDays = 6;
    public const int WarningDays = 14;

    public static string Compute(decimal quantity, DateOnly expiryDate, DateTime lastCheckedAt) =>
        Compute(quantity, expiryDate, lastCheckedAt, perishabilityClass: null);

    public static string Compute(decimal quantity, DateOnly expiryDate, DateTime lastCheckedAt, string? perishabilityClass)
    {
        if (quantity <= 0)
            return SoldOut;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var daysLeft = expiryDate.DayNumber - today.DayNumber;

        if (daysLeft <= 0)
            return Expired;

        var (criticalDays, warningDays) = PerishabilityClass.GetThresholds(perishabilityClass);

        if (daysLeft <= criticalDays)
            return Critical;

        if (daysLeft <= warningDays)
            return Warning;

        var daysSinceCheck = (DateTime.UtcNow - lastCheckedAt).TotalDays;
        if (daysSinceCheck > NeedsVerificationDays)
            return NeedsVerification;

        return Safe;
    }

    public static int DaysLeft(DateOnly expiryDate)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return expiryDate.DayNumber - today.DayNumber;
    }
}
