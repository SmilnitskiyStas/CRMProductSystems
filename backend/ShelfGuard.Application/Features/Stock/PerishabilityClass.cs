namespace ShelfGuard.Application.Features.Stock;

public static class PerishabilityClass
{
    public const string Fresh    = "fresh";
    public const string Chilled  = "chilled";
    public const string Standard = "standard";
    public const string Durable  = "durable";

    private static readonly Dictionary<string, (int Critical, int Warning)> Thresholds = new()
    {
        [Fresh]    = (1, 3),
        [Chilled]  = (2, 5),
        [Standard] = (6, 14),
        [Durable]  = (14, 30),
    };

    public static (int Critical, int Warning) GetThresholds(string? perishabilityClass) =>
        Thresholds.TryGetValue(perishabilityClass ?? Standard, out var t) ? t : Thresholds[Standard];

    public static bool IsValid(string? value) =>
        value is null or Fresh or Chilled or Standard or Durable;
}
