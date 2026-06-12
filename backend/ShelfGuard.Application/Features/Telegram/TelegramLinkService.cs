using System.Security.Cryptography;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Telegram;

public sealed record LinkCodeDto(string Code, string DeepLink, DateTime ExpiresAt);

public interface ITelegramLinkService
{
    /// <summary>Issues a one-time 15-minute link code for the current user.</summary>
    Task<LinkCodeDto> CreateLinkCodeAsync(Guid userId, CancellationToken ct = default);
}

public sealed class TelegramLinkService : ITelegramLinkService
{
    internal static readonly TimeSpan CodeTtl = TimeSpan.FromMinutes(15);

    private readonly ITelegramLinkRepository _repo;
    private readonly string _botUsername;

    public TelegramLinkService(ITelegramLinkRepository repo)
    {
        _repo = repo;
        // Application layer has no IConfiguration dependency — env var with a sane default.
        _botUsername = Environment.GetEnvironmentVariable("Telegram__BotUsername") ?? "shelfguard_bot";
    }

    public async Task<LinkCodeDto> CreateLinkCodeAsync(Guid userId, CancellationToken ct = default)
    {
        // Invalidate previous unused codes so only one is active at a time.
        await _repo.InvalidateActiveCodesAsync(userId, ct);

        var code = GenerateCode();
        var entity = new TelegramLinkCode
        {
            UserId = userId,
            Code = code,
            ExpiresAt = DateTime.UtcNow.Add(CodeTtl),
        };

        await _repo.AddAsync(entity, ct);
        await _repo.SaveChangesAsync(ct);

        return new LinkCodeDto(code, $"https://t.me/{_botUsername}?start={code}", entity.ExpiresAt);
    }

    /// <summary>
    /// 8 chars from an unambiguous alphabet (no 0/O/1/I) — Telegram start payload
    /// allows [A-Za-z0-9_-], up to 64 chars. Internal+static for unit tests.
    /// </summary>
    internal static string GenerateCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var bytes = RandomNumberGenerator.GetBytes(8);
        return new string(bytes.Select(b => alphabet[b % alphabet.Length]).ToArray());
    }
}
