using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ShelfGuard.Application.Features.Leads;
using ShelfGuard.Infrastructure.Integrations;
using Xunit;

namespace ShelfGuard.Tests.Leads;

public sealed class TelegramLeadNotifierTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls;
        public string? LastUri;
        public string? LastBody;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            LastUri = request.RequestUri?.ToString();
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return respond(request);
        }
    }

    private static LeadNotification Lead(bool critical = true, string? message = "hi") =>
        new("beauty", "Іван", "+380671234567", "Салон *А*_", message, critical, critical ? "urgent" : null);

    private static TelegramLeadNotifier Sut(StubHandler h, bool enabled = true, string? token = "T0KEN", string? chat = "42") =>
        new(new HttpClient(h), Options.Create(new LeadsTelegramOptions { Enabled = enabled, BotToken = token, ChatId = chat }),
            NullLogger<TelegramLeadNotifier>.Instance);

    [Fact]
    public async Task Disabled_DoesNotCallTelegram()
    {
        var h = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        await Sut(h, enabled: false).NotifyAsync(Lead(), CancellationToken.None);
        Assert.Equal(0, h.Calls);
    }

    [Theory]
    [InlineData(null, "42")]
    [InlineData("T0KEN", "")]
    public async Task MissingConfig_DoesNotCallTelegram(string? token, string? chat)
    {
        var h = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        await Sut(h, token: token, chat: chat).NotifyAsync(Lead(), CancellationToken.None);
        Assert.Equal(0, h.Calls);
    }

    [Fact]
    public async Task Enabled_PostsPlainTextToSendMessage()
    {
        var h = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        await Sut(h).NotifyAsync(Lead(), CancellationToken.None);

        Assert.Equal(1, h.Calls);
        Assert.EndsWith("/botT0KEN/sendMessage", h.LastUri);
        Assert.DoesNotContain("parse_mode", h.LastBody);
    }

    [Fact]
    public async Task HttpError_DoesNotThrow()
    {
        var h = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        await Sut(h).NotifyAsync(Lead(), CancellationToken.None);
        Assert.Equal(1, h.Calls);
    }

    [Fact]
    public async Task NetworkFailure_DoesNotThrow()
    {
        var h = new StubHandler(_ => throw new HttpRequestException("boom"));
        await Sut(h).NotifyAsync(Lead(), CancellationToken.None);
    }

    [Fact]
    public void BuildText_Critical_ContainsStatusAndFields()
    {
        var t = TelegramLeadNotifier.BuildText(Lead(true));
        Assert.Contains("КРИТИЧНО", t);
        Assert.Contains("beauty", t);
        Assert.Contains("Іван", t);
        Assert.Contains("+380671234567", t);
        Assert.Contains("Салон *А*_", t); // plain text: special chars left as-is, no parse_mode
    }

    [Fact]
    public void BuildText_Ordinary_SaysOrdinary_AndTruncatesMessage()
    {
        var t = TelegramLeadNotifier.BuildText(Lead(false, new string('x', 900)));
        Assert.Contains("звичайна", t);
        Assert.DoesNotContain("КРИТИЧНО", t);
        Assert.DoesNotContain(new string('x', 301), t);
    }
}
