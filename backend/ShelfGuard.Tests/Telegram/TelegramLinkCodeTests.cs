using ShelfGuard.Application.Features.Telegram;
using Xunit;

namespace ShelfGuard.Tests.Telegram;

public sealed class TelegramLinkCodeTests
{
    [Fact]
    public void Code_is_8_chars_from_unambiguous_alphabet()
    {
        for (var i = 0; i < 200; i++)
        {
            var code = TelegramLinkService.GenerateCode();
            Assert.Equal(8, code.Length);
            Assert.Matches("^[ABCDEFGHJKLMNPQRSTUVWXYZ23456789]+$", code);
            // Telegram start payload constraint: [A-Za-z0-9_-]
            Assert.Matches("^[A-Za-z0-9_-]+$", code);
        }
    }

    [Fact]
    public void Codes_are_unique_across_generations()
    {
        var codes = Enumerable.Range(0, 500).Select(_ => TelegramLinkService.GenerateCode()).ToHashSet();
        Assert.Equal(500, codes.Count);
    }

    [Fact]
    public void Ttl_is_15_minutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(15), TelegramLinkService.CodeTtl);
    }
}
