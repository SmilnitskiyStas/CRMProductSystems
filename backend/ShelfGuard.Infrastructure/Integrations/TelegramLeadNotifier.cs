using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShelfGuard.Application.Features.Leads;

namespace ShelfGuard.Infrastructure.Integrations;

/// <summary>Config section "Leads:Telegram" (env Leads__Telegram__BotToken / ChatId / Enabled). Secrets only from env.</summary>
public sealed class LeadsTelegramOptions
{
    public const string Section = "Leads:Telegram";
    public bool Enabled { get; set; }
    public string? BotToken { get; set; }
    public string? ChatId { get; set; }
}

/// <summary>Sends a lead alert via Telegram Bot API sendMessage (plain text, no parse_mode).</summary>
public sealed class TelegramLeadNotifier(
    HttpClient http,
    IOptions<LeadsTelegramOptions> options,
    ILogger<TelegramLeadNotifier> logger) : ILeadNotifier
{
    public const int MaxMessageChars = 300;

    public async Task NotifyAsync(LeadNotification n, CancellationToken ct)
    {
        var o = options.Value;
        if (!o.Enabled || string.IsNullOrWhiteSpace(o.BotToken) || string.IsNullOrWhiteSpace(o.ChatId))
            return;

        try
        {
            // The token is part of the URL by Telegram's API design; never log the URL or exception text.
            using var response = await http.PostAsJsonAsync(
                $"https://api.telegram.org/bot{o.BotToken}/sendMessage",
                new { chat_id = o.ChatId, text = BuildText(n), disable_web_page_preview = true }, ct);

            if (!response.IsSuccessStatusCode)
                logger.LogWarning("Lead Telegram push failed: HTTP {Status}", (int)response.StatusCode);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Lead Telegram push timed out or was cancelled.");
        }
        catch (Exception ex)
        {
            logger.LogWarning("Lead Telegram push failed: {ErrorType}", ex.GetType().Name);
        }
    }

    public static string BuildText(LeadNotification n)
    {
        var status = n.IsCritical ? "КРИТИЧНО" : "звичайна";
        var msg = string.IsNullOrWhiteSpace(n.Message) ? "-" : n.Message.Trim();
        if (msg.Length > MaxMessageChars) msg = msg[..MaxMessageChars] + "…";
        var reason = n.IsCritical && !string.IsNullOrEmpty(n.CriticalReason) ? $" ({n.CriticalReason})" : "";
        var lines = new List<string>
        {
            $"Нова заявка: {status}{reason}",
            $"Джерело: {n.Source}",
        };
        if (!string.IsNullOrWhiteSpace(n.RequestType)) lines.Add($"Тип: {n.RequestType}");
        lines.Add($"Ім’я: {n.Name}");
        lines.Add($"Телефон: {n.Phone}");
        lines.Add($"Компанія: {(string.IsNullOrWhiteSpace(n.Company) ? "-" : n.Company)}");
        lines.Add($"Повідомлення: {msg}");
        return string.Join('\n', lines);
    }
}
