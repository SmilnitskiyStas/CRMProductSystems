using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ShelfGuard.Application.Features.AiAssistant;
using ShelfGuard.Application.Services;
using ShelfGuard.Infrastructure.Data;

namespace ShelfGuard.Infrastructure.AI.BusinessAssistant;

/// <summary>
/// Business assistant (TASK-250, v4 Phase 6). Aggregates cross-module context (critical stock
/// batches, pending AI orders, last-7-days sales, active suppliers) per tenant, then asks the
/// model. Provider-agnostic since managed-AI Phase 2 — the model call goes through
/// <see cref="IAiClientFactory"/> / <see cref="IAiChatClient"/>. The per-tenant isolation
/// guardrail is applied by <see cref="IAiPromptResolver"/> (Phase 1).
/// </summary>
public sealed class BusinessAssistantAdvisor : IBusinessAssistantAdvisor
{
    private readonly AppDbContext _db;
    private readonly IAiClientFactory _ai;
    private readonly IAiPromptResolver _prompt;

    public BusinessAssistantAdvisor(AppDbContext db, IAiClientFactory ai, IAiPromptResolver prompt)
    {
        _db = db;
        _ai = ai;
        _prompt = prompt;
    }

    public Task<bool> IsConfiguredAsync(CancellationToken ct = default) => _ai.IsConfiguredAsync(ct);

    public async Task<BusinessAssistantResult> AdviseAsync(
        Guid tenantId,
        string message,
        CancellationToken ct = default)
    {
        var client = await _ai.ResolveAsync(ct)
            ?? throw new InvalidOperationException("AI-агент не налаштований. Зверніться до вашого провайдера.");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // ── 1. Critical / expiring stock batches ──────────────────────────────
        var expiryThreshold = today.AddDays(7);
        var criticalStock = await _db.ProductStocks
            .Where(s => s.TenantId == tenantId
                     && s.Quantity > 0
                     && (s.Status != "safe" || s.ExpiryDate <= expiryThreshold))
            .OrderBy(s => s.ExpiryDate)
            .Take(20)
            .Select(s => new
            {
                ProductName = s.Product != null ? s.Product.Name : "Unknown",
                s.BatchNumber,
                s.Quantity,
                s.Status,
                s.ExpiryDate,
            })
            .ToListAsync(ct);

        var criticalLines = criticalStock.Select(s => new
        {
            s.ProductName,
            s.BatchNumber,
            s.Quantity,
            s.Status,
            ExpiryDate       = s.ExpiryDate.ToString("yyyy-MM-dd"),
            DaysUntilExpiry  = s.ExpiryDate.DayNumber - today.DayNumber,
        }).ToList<object>();

        // ── 2. Pending AI order suggestions ───────────────────────────────────
        var pendingOrders = await _db.AiOrderSuggestions
            .Where(s => s.TenantId == tenantId && s.Status == "pending")
            .OrderByDescending(s => s.GeneratedAt)
            .Take(5)
            .Select(s => new
            {
                SuggestionId = s.Id,
                StoreName    = s.Store != null ? s.Store.Name : "Unknown",
                OrderDate    = s.OrderDate.ToString("yyyy-MM-dd"),
                ItemsCount   = s.Items.Count,
            })
            .ToListAsync(ct);

        // ── 3. Sales last 7 days — top 20 lines by volume ────────────────────
        var sevenDaysAgo = today.AddDays(-7);
        var salesLines = await _db.DailySales
            .Where(d => d.TenantId == tenantId && d.Date >= sevenDaysAgo && !d.IsAnomaly)
            .OrderByDescending(d => d.QuantitySold)
            .Take(20)
            .Select(d => new
            {
                Date        = d.Date.ToString("yyyy-MM-dd"),
                ProductName = d.Product != null ? d.Product.Name : "Unknown",
                d.QuantitySold,
            })
            .ToListAsync(ct);

        // ── 4. Active suppliers ───────────────────────────────────────────────
        var suppliers = await _db.Suppliers
            .Where(s => s.TenantId == tenantId && s.IsActive)
            .OrderBy(s => s.Name)
            .Take(15)
            .Select(s => new { s.Name, s.Phone, s.DeliveryDays })
            .ToListAsync(ct);

        // ── 5. Call Claude ────────────────────────────────────────────────────
        var opts = new JsonSerializerOptions { WriteIndented = false };
        var userPrompt =
            $"ЗАПИТ МЕНЕДЖЕРА: {message}\n\n" +
            $"=== КОНТЕКСТ МАГАЗИНУ ===\n\n" +
            $"КРИТИЧНІ ЗАЛИШКИ (закінчуються / прострочуються):\n" +
            $"{JsonSerializer.Serialize(criticalLines, opts)}\n\n" +
            $"PENDING AI-ЗАМОВЛЕННЯ (не підтверджені):\n" +
            $"{JsonSerializer.Serialize(pendingOrders, opts)}\n\n" +
            $"ПРОДАЖІ ЗА ОСТАННІ 7 ДНІВ (топ-20 за обсягом):\n" +
            $"{JsonSerializer.Serialize(salesLines, opts)}\n\n" +
            $"АКТИВНІ ПОСТАЧАЛЬНИКИ:\n" +
            $"{JsonSerializer.Serialize(suppliers, opts)}\n\n" +
            $"Надай корисну відповідь на запит менеджера з урахуванням наведеного контексту.";

        var system = await _prompt.WrapSystemPromptAsync(BuildSystemPrompt(), ct);
        var result = await client.CompleteAsync(new AiChatRequest(system, userPrompt, MaxTokens: 2048), ct);

        var contextSummary = new BusinessAssistantContextSummary(
            criticalLines.Count,
            pendingOrders.Count,
            salesLines.Count,
            suppliers.Count);

        return new BusinessAssistantResult(result.Text, contextSummary, result.Model, result.TokensUsed);
    }

    private static string BuildSystemPrompt() =>
        "Ти — AI бізнес-асистент для менеджера роздрібного магазину в Україні (ShelfGuard). " +
        "Твоє завдання — відповідати на запитання про стан магазину, запаси, продажі, " +
        "постачальників та замовлення на основі наданого контексту. " +
        "Відповідай конкретно, коротко (2-5 речень), українською мовою. " +
        "Якщо даних недостатньо для точної відповіді — чесно про це скажи. " +
        "Не вигадуй цифри яких немає в контексті.";
}
