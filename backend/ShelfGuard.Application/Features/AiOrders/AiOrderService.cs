using System.Text.Json;
using ShelfGuard.Application.Features.Orders;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.AiOrders;

// ── DTOs ────────────────────────────────────────────────────────────────────

public sealed record AiOrderListItemDto(
    Guid Id, Guid StoreId, string StoreName, DateTime GeneratedAt, string OrderDate,
    string Status, int ItemsCount, string AiModel, int? TokensUsed);

public sealed record AiOrderItemDto(
    Guid Id, Guid ProductId, string ProductName, string? Barcode,
    decimal QuantityBase, decimal QuantitySuggested, decimal QuantityFinal,
    string? Reasoning, string? Confidence, string? Factors, bool WasEdited, string? EditReason);

public sealed record AiOrderDto(
    Guid Id, Guid StoreId, string StoreName, DateTime GeneratedAt, string OrderDate,
    string Status, string AiModel, int? TokensUsed, List<AiOrderItemDto> Items);

public sealed record GenerateAiOrderRequest(Guid StoreId);
public sealed record UpdateAiOrderItemRequest(decimal QuantityFinal, string? EditReason);

// ── Service ─────────────────────────────────────────────────────────────────

public interface IAiOrderService
{
    /// <summary>
    /// TASK-610: storeIds is a repeated query param — omitted or empty means "all stores".
    /// </summary>
    Task<List<AiOrderListItemDto>> GetListAsync(Guid[]? storeIds, CancellationToken ct = default);
    Task<(AiOrderDto? Order, string? Error)> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Full §7 pipeline: context → base formula → Claude → persist.</summary>
    Task<(AiOrderDto? Order, string? Error)> GenerateAsync(Guid tenantId, Guid storeId, CancellationToken ct = default);

    Task<(AiOrderItemDto? Item, string? Error)> UpdateItemAsync(
        Guid suggestionId, Guid itemId, UpdateAiOrderItemRequest request, CancellationToken ct = default);

    Task<(AiOrderDto? Order, string? Error)> AcceptAsync(Guid id, Guid acceptedBy, CancellationToken ct = default);
    Task<(AiOrderDto? Order, string? Error)> RejectAsync(Guid id, CancellationToken ct = default);
}

public sealed class AiOrderService : IAiOrderService
{
    private readonly IAiOrderRepository _repo;
    private readonly IAiOrderAdvisor _advisor;
    private readonly IOrderCalcService _orderCalc;
    private readonly IWeatherRepository _weather;
    private readonly IEventRepository _events;
    private readonly ISupplyScheduleRepository _schedules;
    private readonly ICannibalizationRepository _promos;

    public AiOrderService(
        IAiOrderRepository repo,
        IAiOrderAdvisor advisor,
        IOrderCalcService orderCalc,
        IWeatherRepository weather,
        IEventRepository events,
        ISupplyScheduleRepository schedules,
        ICannibalizationRepository promos)
    {
        _repo = repo;
        _advisor = advisor;
        _orderCalc = orderCalc;
        _weather = weather;
        _events = events;
        _schedules = schedules;
        _promos = promos;
    }

    public async Task<List<AiOrderListItemDto>> GetListAsync(Guid[]? storeIds, CancellationToken ct = default)
    {
        // GetListAsync now eager-loads Items, so Items.Count is free — previously this looped
        // and called GetByIdAsync per suggestion (N+1: up to 30 extra full round-trips just to
        // count items). Fixed as part of the Block 7 AI Orders/Assistant audit.
        var list = await _repo.GetListAsync(storeIds, limit: 30, ct);
        return list.Select(s => new AiOrderListItemDto(
            s.Id, s.StoreId, s.Store?.Name ?? "", s.GeneratedAt, s.OrderDate.ToString("yyyy-MM-dd"),
            s.Status, s.Items.Count, s.AiModel, s.TokensUsed)).ToList();
    }

    public async Task<(AiOrderDto? Order, string? Error)> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var s = await _repo.GetByIdAsync(id, ct);
        return s is null ? (null, "AI order suggestion not found.") : (ToDto(s), null);
    }

    public async Task<(AiOrderDto? Order, string? Error)> GenerateAsync(
        Guid tenantId, Guid storeId, CancellationToken ct = default)
    {
        if (!await _advisor.IsConfiguredAsync(ct))
            return (null, "Claude API key не налаштовано. Додайте його: Налаштування → Інтеграції → Claude AI.");

        var storeName = await _repo.GetStoreNameAsync(storeId, ct);
        if (storeName is null)
            return (null, "Store not found.");

        // 1-2. Base order via the mathematical formula (ADU → buffer → coefficients → MOQ/USQ).
        // Phase 4 (plan D5): CalculateAsync's per-line InTransit now also counts open B2B
        // marketplace orders headed to this store, so the "already on the way" quantity the AI
        // sees (l.InTransit, fed into the Claude context below) includes them with no change
        // here — this service never computes in-transit independently of CalculateAsync.
        var (calc, calcError) = await _orderCalc.CalculateAsync(storeId, ct);
        if (calcError is not null) return (null, calcError);

        var orderableLines = calc!.Lines.Where(l => l.QuantityToOrder > 0).ToList();
        if (orderableLines.Count == 0)
            return (null, "Nothing to order — all demand is covered.");

        // 1. Context: weather, events (14d), promos, schedule
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var forecast = await _weather.GetForecastAsync(storeId, ct);
        var events = await _events.GetAsync(today, today.AddDays(14), new[] { storeId }, ct);
        var promoCoefs = await _promos.GetActivePromoCoefficientsAsync(storeId, DateTime.UtcNow, ct);
        var adu30 = await _repo.GetAdu30Async(storeId, ct);
        var nextDelivery = await ComputeNextDeliveryAsync(storeId, today, ct);

        var context = new AiOrderContext(
            storeName,
            today,
            nextDelivery,
            forecast.Select(w => new AiWeatherDay(
                w.Date.ToString("yyyy-MM-dd"), w.TempMin, w.TempMax, w.Precipitation, w.WeatherCode)).ToList(),
            events.Select(e => new AiCalendarEvent(
                e.Name, e.EventType, e.StartsAt.ToString("yyyy-MM-dd"), e.EndsAt.ToString("yyyy-MM-dd"))).ToList(),
            orderableLines
                .Where(l => promoCoefs.ContainsKey(l.ProductId))
                .Select(l => new AiActivePromo(l.ProductId, l.ProductName, promoCoefs[l.ProductId])).ToList(),
            orderableLines.Select(l => new AiStockLine(
                l.ProductId, l.ProductName, l.StockOnHand,
                adu30.TryGetValue(l.ProductId, out var a) ? a : null,
                l.BufferTotal, l.SafetyBuffer, l.InTransit, l.Moq, l.Usq, l.QuantityToOrder)).ToList());

        // 3. Claude — provider failures become readable errors, not 500s
        AiAdviceResult advice;
        try
        {
            advice = await _advisor.AdviseAsync(context, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var detail = ex.Message.Contains("credit balance", StringComparison.OrdinalIgnoreCase)
                ? "Недостатньо кредитів на Anthropic акаунті. Поповніть баланс: console.anthropic.com → Plans & Billing."
                : $"AI сервіс недоступний: {ex.Message}";
            return (null, detail);
        }
        var adviceByProduct = advice.Items.ToDictionary(i => i.ProductId);

        // 4. Persist: AI quantity when provided, base otherwise
        var suggestion = new AiOrderSuggestion
        {
            TenantId = tenantId,
            StoreId = storeId,
            OrderDate = today,
            ContextSnapshot = JsonSerializer.Serialize(context),
            AiModel = advice.Model,
            TokensUsed = advice.TokensUsed,
        };

        foreach (var line in orderableLines)
        {
            adviceByProduct.TryGetValue(line.ProductId, out var ai);
            var suggested = ai?.QuantitySuggested ?? line.QuantityToOrder;

            suggestion.Items.Add(new AiOrderSuggestionItem
            {
                SuggestionId = suggestion.Id,
                ProductId = line.ProductId,
                QuantityBase = line.QuantityToOrder,
                QuantitySuggested = suggested,
                QuantityFinal = suggested,
                Reasoning = ai?.Reasoning,
                Confidence = ai?.Confidence,
                Factors = ai?.FactorsJson,
            });
        }

        await _repo.AddAsync(suggestion, ct);
        await _repo.SaveChangesAsync(ct);

        var saved = await _repo.GetByIdAsync(suggestion.Id, ct);
        return (ToDto(saved!), null);
    }

    public async Task<(AiOrderItemDto? Item, string? Error)> UpdateItemAsync(
        Guid suggestionId, Guid itemId, UpdateAiOrderItemRequest request, CancellationToken ct = default)
    {
        if (request.QuantityFinal < 0)
            return (null, "QuantityFinal must be >= 0.");

        var item = await _repo.GetItemAsync(suggestionId, itemId, ct);
        if (item is null) return (null, "Suggestion item not found.");
        if (item.Suggestion!.Status is "accepted" or "rejected")
            return (null, "Suggestion is already finalized.");

        item.QuantityFinal = request.QuantityFinal;
        item.WasEdited = request.QuantityFinal != item.QuantitySuggested;
        item.EditReason = request.EditReason;

        await _repo.SaveChangesAsync(ct);

        var full = await _repo.GetByIdAsync(suggestionId, ct);
        var dto = ToDto(full!).Items.First(i => i.Id == itemId);
        return (dto, null);
    }

    public async Task<(AiOrderDto? Order, string? Error)> AcceptAsync(
        Guid id, Guid acceptedBy, CancellationToken ct = default)
    {
        var s = await _repo.GetByIdAsync(id, ct);
        if (s is null) return (null, "AI order suggestion not found.");
        if (s.Status is "accepted" or "rejected") return (null, "Suggestion is already finalized.");

        s.Status = s.Items.Any(i => i.WasEdited) ? "partially_accepted" : "accepted";
        s.AcceptedBy = acceptedBy;
        s.AcceptedAt = DateTime.UtcNow;

        await _repo.SaveChangesAsync(ct);
        return (ToDto(s), null);
    }

    public async Task<(AiOrderDto? Order, string? Error)> RejectAsync(Guid id, CancellationToken ct = default)
    {
        var s = await _repo.GetByIdAsync(id, ct);
        if (s is null) return (null, "AI order suggestion not found.");
        if (s.Status is "accepted" or "rejected") return (null, "Suggestion is already finalized.");

        s.Status = "rejected";
        await _repo.SaveChangesAsync(ct);
        return (ToDto(s), null);
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private async Task<DateOnly?> ComputeNextDeliveryAsync(Guid storeId, DateOnly today, CancellationToken ct)
    {
        var schedules = await _schedules.GetAsync(storeId, null, ct);
        var deliveryDays = schedules
            .Where(s => s.IsActive)
            .SelectMany(s => s.DayOfWeek)
            .Distinct()
            .ToHashSet();

        if (deliveryDays.Count == 0) return null;

        for (var i = 1; i <= 7; i++)
        {
            var candidate = today.AddDays(i);
            var isoDay = (int)candidate.DayOfWeek == 0 ? 7 : (int)candidate.DayOfWeek;
            if (deliveryDays.Contains(isoDay)) return candidate;
        }
        return null;
    }

    private static AiOrderDto ToDto(AiOrderSuggestion s) => new(
        s.Id, s.StoreId, s.Store?.Name ?? "", s.GeneratedAt, s.OrderDate.ToString("yyyy-MM-dd"),
        s.Status, s.AiModel, s.TokensUsed,
        s.Items
            .OrderByDescending(i => Math.Abs(i.QuantitySuggested - i.QuantityBase))
            .Select(i => new AiOrderItemDto(
                i.Id, i.ProductId, i.Product?.Name ?? "", i.Product?.Barcodes.Count > 0 ? i.Product.Barcodes[0] : null,
                i.QuantityBase, i.QuantitySuggested, i.QuantityFinal,
                i.Reasoning, i.Confidence, i.Factors, i.WasEdited, i.EditReason))
            .ToList());
}
