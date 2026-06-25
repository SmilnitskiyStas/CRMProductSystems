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

    public const int NeedsVerificationDays = 90;
    public const int CriticalDays = 6;
    public const int WarningDays = 14;

    public static string Compute(decimal quantity, DateOnly expiryDate, DateTime lastCheckedAt)
    {
        if (quantity <= 0)
            return SoldOut;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var daysLeft = expiryDate.DayNumber - today.DayNumber;

        if (daysLeft <= 0)
            return Expired;

        if (daysLeft <= CriticalDays)
            return Critical;

        if (daysLeft <= WarningDays)
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
