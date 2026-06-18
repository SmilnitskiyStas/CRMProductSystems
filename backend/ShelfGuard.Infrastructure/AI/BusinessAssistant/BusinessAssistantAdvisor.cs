using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ShelfGuard.Application.Features.AiAssistant;
using ShelfGuard.Infrastructure.Data;

namespace ShelfGuard.Infrastructure.AI.BusinessAssistant;

/// <summary>
/// Claude-backed business assistant (TASK-250, v4 Phase 6).
///
/// Aggregates cross-module context (critical stock batches, pending AI orders,
/// last-7-days sales, active suppliers) per tenant, then calls Claude to answer
/// the manager's natural-language question.
///
/// AI isolation rule (ADR-015): all prompt templates, provider wiring, and parsing
/// live exclusively here — never in Application services or controllers.
///
/// Key resolution mirrors ClaudeOrderAdvisor:
///   tenant integration_configs row (service='claude') → fallback to Claude:ApiKey env.
/// </summary>
public sealed class BusinessAssistantAdvisor : IBusinessAssistantAdvisor
{
    private readonly AppDbContext _db;
    private readonly string? _envApiKey;
    private readonly string _defaultModel;

    public BusinessAssistantAdvisor(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _envApiKey = config["Claude:ApiKey"];
        _defaultModel = config["Claude:Model"] ?? "claude-sonnet-4-6";
    }

    public async Task<bool> IsConfiguredAsync(CancellationToken ct = default) =>
        (await ResolveAsync(ct)).ApiKey is not null;

    private async Task<(string? ApiKey, string Model)> ResolveAsync(CancellationToken ct)
    {
        var row = await _db.IntegrationConfigs
            .Where(i => i.Service == "claude" && i.IsEnabled)
            .Select(i => i.Config)
            .FirstOrDefaultAsync(ct);

        if (row is not null)
        {
            try
            {
                using var doc = JsonDocument.Parse(row);
                var key   = doc.RootElement.TryGetProperty("api_key", out var k) ? k.GetString() : null;
                var model = doc.RootElement.TryGetProperty("model",   out var m) ? m.GetString() : null;
                if (!string.IsNullOrWhiteSpace(key))
                    return (key, string.IsNullOrWhiteSpace(model) ? _defaultModel : model!);
            }
            catch (JsonException) { /* malformed config — fall through to env */ }
        }

        return (string.IsNullOrWhiteSpace(_envApiKey) ? null : _envApiKey, _defaultModel);
    }

    public async Task<BusinessAssistantResult> AdviseAsync(
        Guid tenantId,
        string message,
        CancellationToken ct = default)
    {
        var (apiKey, model) = await ResolveAsync(ct);
        if (apiKey is null)
            throw new InvalidOperationException(
                "Claude API key is not configured. Add it in Налаштування → Інтеграції → Claude AI.");

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

        var client = new AnthropicClient { ApiKey = apiKey };

        var parameters = new MessageCreateParams
        {
            Model    = model,
            MaxTokens = 2048,
            System   = BuildSystemPrompt(),
            Messages = [new() { Role = Role.User, Content = userPrompt }],
        };

        var response = await client.Messages.Create(parameters, cancellationToken: ct);

        var reply = response.Content
            .Select(b => b.Value)
            .OfType<TextBlock>()
            .Select(t => t.Text)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("Claude returned no text content.");

        var tokens = (int)(response.Usage.InputTokens + response.Usage.OutputTokens);

        var contextSummary = new BusinessAssistantContextSummary(
            criticalLines.Count,
            pendingOrders.Count,
            salesLines.Count,
            suppliers.Count);

        return new BusinessAssistantResult(reply, contextSummary, model, tokens);
    }

    private static string BuildSystemPrompt() =>
        "Ти — AI бізнес-асистент для менеджера роздрібного магазину в Україні (ShelfGuard). " +
        "Твоє завдання — відповідати на запитання про стан магазину, запаси, продажі, " +
        "постачальників та замовлення на основі наданого контексту. " +
        "Відповідай конкретно, коротко (2-5 речень), українською мовою. " +
        "Якщо даних недостатньо для точної відповіді — чесно про це скажи. " +
        "Не вигадуй цифри яких немає в контексті.";
}
