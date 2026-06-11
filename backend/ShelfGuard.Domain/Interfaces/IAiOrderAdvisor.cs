namespace ShelfGuard.Domain.Interfaces;

/// <summary>Full context handed to the AI for one store's order run (v2-spec §7).</summary>
public sealed record AiOrderContext(
    string StoreName,
    DateOnly OrderDate,
    DateOnly? NextDeliveryDate,
    IReadOnlyList<AiWeatherDay> WeatherForecast,
    IReadOnlyList<AiCalendarEvent> UpcomingEvents,
    IReadOnlyList<AiActivePromo> ActivePromos,
    IReadOnlyList<AiStockLine> StockLines);

public sealed record AiWeatherDay(string Date, decimal? TempMin, decimal? TempMax, decimal? Precipitation, int? WeatherCode);

public sealed record AiCalendarEvent(string Name, string EventType, string StartsAt, string EndsAt);

public sealed record AiActivePromo(Guid ProductId, string ProductName, decimal OrderCoefficient);

public sealed record AiStockLine(
    Guid ProductId,
    string ProductName,
    decimal CurrentStock,
    decimal? Adu30d,
    decimal Buffer,
    decimal SafetyBuffer,
    decimal InTransit,
    decimal Moq,
    decimal Usq,
    decimal BaseOrderQty);

public sealed record AiAdviceItem(
    Guid ProductId,
    decimal QuantitySuggested,
    string Reasoning,
    string Confidence,
    string FactorsJson);

public sealed record AiAdviceResult(
    IReadOnlyList<AiAdviceItem> Items,
    string Model,
    int TokensUsed);

/// <summary>
/// AI order advisor abstraction. Implemented by the Claude client in
/// Infrastructure/AI — prompts and provider specifics never leak past this interface.
/// </summary>
public interface IAiOrderAdvisor
{
    /// <summary>True when an API key is configured — callers can fail fast with a clear error.</summary>
    bool IsConfigured { get; }

    Task<AiAdviceResult> AdviseAsync(AiOrderContext context, CancellationToken ct = default);
}
