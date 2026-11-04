using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ShelfGuard.Application.Features.Leads;
using ShelfGuard.Application.Features.Leads.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Leads;

/// <summary>
/// TASK-333: public landing lead capture — honeypot silently discards bot submissions,
/// validation enforces the fixed frontend contract, happy path persists the lead.
/// </summary>
public sealed class LandingLeadServiceTests
{
    private readonly ILandingLeadRepository _repo = Substitute.For<ILandingLeadRepository>();
    private readonly ILeadNotifier _notifier = Substitute.For<ILeadNotifier>();
    private readonly LandingLeadService _sut;

    public LandingLeadServiceTests()
    {
        _repo.GetPhonesSinceAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new List<string>());
        _sut = new LandingLeadService(_repo, _notifier, NullLogger<LandingLeadService>.Instance);
    }

    private static CaptureLeadRequest Valid(
        string? name = "Іван Петренко",
        string? phone = "+380671234567",
        string? company = "ТОВ Агро",
        string? message = "Цікавить демо",
        string? website = null) =>
        new(name, phone, company, message, website);

    // ── Honeypot ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CaptureAsync_HoneypotFilled_ReturnsSuccess_DoesNotSave()
    {
        var error = await _sut.CaptureAsync(Valid(website: "http://spam.example"), CancellationToken.None);

        Assert.Null(error);
        await _repo.DidNotReceive().AddAsync(Arg.Any<LandingLead>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CaptureAsync_HoneypotFilled_SkipsValidationEntirely()
    {
        // Bot fills honeypot AND sends garbage — still a silent 204, nothing saved.
        var error = await _sut.CaptureAsync(
            new CaptureLeadRequest(Name: null, Phone: null, Company: null, Message: null, Website: "x"),
            CancellationToken.None);

        Assert.Null(error);
        await _repo.DidNotReceive().AddAsync(Arg.Any<LandingLead>(), Arg.Any<CancellationToken>());
    }

    // ── Validation ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("A")]
    public async Task CaptureAsync_InvalidName_ReturnsError_DoesNotSave(string? name)
    {
        var error = await _sut.CaptureAsync(Valid(name: name), CancellationToken.None);

        Assert.NotNull(error);
        await _repo.DidNotReceive().AddAsync(Arg.Any<LandingLead>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CaptureAsync_NameTooLong_ReturnsError()
    {
        var error = await _sut.CaptureAsync(Valid(name: new string('x', 101)), CancellationToken.None);

        Assert.NotNull(error);
        await _repo.DidNotReceive().AddAsync(Arg.Any<LandingLead>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1234")]
    public async Task CaptureAsync_InvalidPhone_ReturnsError_DoesNotSave(string? phone)
    {
        var error = await _sut.CaptureAsync(Valid(phone: phone), CancellationToken.None);

        Assert.NotNull(error);
        await _repo.DidNotReceive().AddAsync(Arg.Any<LandingLead>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CaptureAsync_PhoneTooLong_ReturnsError()
    {
        var error = await _sut.CaptureAsync(Valid(phone: new string('1', 31)), CancellationToken.None);

        Assert.NotNull(error);
        await _repo.DidNotReceive().AddAsync(Arg.Any<LandingLead>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CaptureAsync_CompanyTooLong_ReturnsError()
    {
        var error = await _sut.CaptureAsync(Valid(company: new string('c', 151)), CancellationToken.None);

        Assert.NotNull(error);
        await _repo.DidNotReceive().AddAsync(Arg.Any<LandingLead>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CaptureAsync_MessageTooLong_ReturnsError()
    {
        var error = await _sut.CaptureAsync(Valid(message: new string('m', 1001)), CancellationToken.None);

        Assert.NotNull(error);
        await _repo.DidNotReceive().AddAsync(Arg.Any<LandingLead>(), Arg.Any<CancellationToken>());
    }

    // ── Happy path ─────────────────────────────────────────────────────────

    [Fact]
    public async Task CaptureAsync_ValidRequest_SavesLead()
    {
        var error = await _sut.CaptureAsync(Valid(), CancellationToken.None);

        Assert.Null(error);
        await _repo.Received(1).AddAsync(
            Arg.Is<LandingLead>(l =>
                l.Name == "Іван Петренко" &&
                l.Phone == "+380671234567" &&
                l.Company == "ТОВ Агро" &&
                l.Message == "Цікавить демо" &&
                l.Source == "landing" &&
                !l.IsProcessed),
            Arg.Any<CancellationToken>());
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CaptureAsync_OptionalFieldsBlank_SavesLeadWithNulls()
    {
        var error = await _sut.CaptureAsync(
            Valid(company: "  ", message: null), CancellationToken.None);

        Assert.Null(error);
        await _repo.Received(1).AddAsync(
            Arg.Is<LandingLead>(l => l.Company == null && l.Message == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CaptureAsync_TrimsWhitespace_BeforeSaving()
    {
        var error = await _sut.CaptureAsync(
            Valid(name: "  Іван  ", phone: " +380671234567 "), CancellationToken.None);

        Assert.Null(error);
        await _repo.Received(1).AddAsync(
            Arg.Is<LandingLead>(l => l.Name == "Іван" && l.Phone == "+380671234567"),
            Arg.Any<CancellationToken>());
    }

    // ── TASK-721: attribution, whitelist, persistence failure ──────────────

    [Fact]
    public async Task CaptureAsync_StoresAttributionFields_TruncatingLongValues()
    {
        var request = Valid() with
        {
            Source = "Retail",
            PageUrl = "/uk/retail",
            Locale = "UK",
            Referrer = new string('r', 500),
            UtmSource = "google",
            UtmMedium = "cpc",
            UtmCampaign = new string('c', 400),
        };

        var error = await _sut.CaptureAsync(request, CancellationToken.None);

        Assert.Null(error);
        await _repo.Received(1).AddAsync(
            Arg.Is<LandingLead>(l =>
                l.Source == "retail" && l.PageUrl == "/uk/retail" && l.Locale == "uk" &&
                l.Referrer!.Length == 300 && l.UtmSource == "google" && l.UtmMedium == "cpc" &&
                l.UtmCampaign!.Length == 150),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("evil-source")]
    [InlineData("")]
    [InlineData(null)]
    public async Task CaptureAsync_UnknownSource_FallsBackToLanding(string? source)
    {
        await _sut.CaptureAsync(Valid() with { Source = source, Locale = "xx" }, CancellationToken.None);

        await _repo.Received(1).AddAsync(
            Arg.Is<LandingLead>(l => l.Source == "landing" && l.Locale == null),
            Arg.Any<CancellationToken>());
    }

    // ── Beauty source, criticality, Telegram push ──────────────────────────

    [Theory]
    [InlineData("beauty")]
    [InlineData("Beauty-Join")]
    public async Task CaptureAsync_BeautySource_Accepted(string source)
    {
        await _sut.CaptureAsync(Valid() with { Source = source }, CancellationToken.None);

        await _repo.Received(1).AddAsync(
            Arg.Is<LandingLead>(l => l.Source == source.ToLowerInvariant()), Arg.Any<CancellationToken>());
    }

    private static async Task<LeadNotification> WaitForNotification(ILeadNotifier notifier)
    {
        for (var i = 0; i < 100; i++)
        {
            var call = notifier.ReceivedCalls().FirstOrDefault();
            if (call is not null) return (LeadNotification)call.GetArguments()[0]!;
            await Task.Delay(20);
        }
        throw new TimeoutException("notifier was not called");
    }

    [Fact]
    public async Task CaptureAsync_Urgent_SavedAsCritical_AndNotifiesCritical()
    {
        await _sut.CaptureAsync(Valid(message: "Потрібно терміново"), CancellationToken.None);

        await _repo.Received(1).AddAsync(
            Arg.Is<LandingLead>(l => l.IsCritical && l.CriticalReason == "urgent"), Arg.Any<CancellationToken>());
        var n = await WaitForNotification(_notifier);
        Assert.True(n.IsCritical);
    }

    [Fact]
    public async Task CaptureAsync_Ordinary_SavedAsNotCritical_NotifiesOrdinary()
    {
        await _sut.CaptureAsync(Valid(), CancellationToken.None);

        await _repo.Received(1).AddAsync(
            Arg.Is<LandingLead>(l => !l.IsCritical && l.CriticalReason == null), Arg.Any<CancellationToken>());
        var n = await WaitForNotification(_notifier);
        Assert.False(n.IsCritical);
    }

    [Fact]
    public async Task CaptureAsync_SamePhoneWithinWindow_IsCriticalRepeat()
    {
        _repo.GetPhonesSinceAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new List<string> { "+38 (067) 123-45-67" });

        await _sut.CaptureAsync(Valid(), CancellationToken.None);

        await _repo.Received(1).AddAsync(
            Arg.Is<LandingLead>(l => l.IsCritical && l.CriticalReason == "repeat"), Arg.Any<CancellationToken>());
        // lookup window is 7 days back from now
        await _repo.Received(1).GetPhonesSinceAsync(
            Arg.Is<DateTime>(d => Math.Abs((DateTime.UtcNow - d - TimeSpan.FromDays(7)).TotalMinutes) < 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CaptureAsync_RepeatLookupFails_StillSavesLead()
    {
        _repo.GetPhonesSinceAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<List<string>>(new InvalidOperationException("db")));

        var error = await _sut.CaptureAsync(Valid(), CancellationToken.None);

        Assert.Null(error);
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CaptureAsync_Honeypot_DoesNotNotify()
    {
        await _sut.CaptureAsync(Valid(website: "http://spam.example"), CancellationToken.None);
        await Task.Delay(100);

        Assert.Empty(_notifier.ReceivedCalls());
    }

    [Fact]
    public async Task CaptureAsync_ValidationError_DoesNotNotify()
    {
        await _sut.CaptureAsync(Valid(name: "A"), CancellationToken.None);
        await Task.Delay(100);

        Assert.Empty(_notifier.ReceivedCalls());
    }

    [Fact]
    public async Task CaptureAsync_NotifierThrows_DoesNotBreakCapture()
    {
        _notifier.NotifyAsync(Arg.Any<LeadNotification>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new HttpRequestException("telegram down")));

        var error = await _sut.CaptureAsync(Valid(), CancellationToken.None);

        Assert.Null(error);
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CaptureAsync_NotifierHangs_DoesNotDelayResponse()
    {
        var gate = new TaskCompletionSource();
        _notifier.NotifyAsync(Arg.Any<LeadNotification>(), Arg.Any<CancellationToken>()).Returns(gate.Task);

        var capture = _sut.CaptureAsync(Valid(), CancellationToken.None);
        var finished = await Task.WhenAny(capture, Task.Delay(2000));
        gate.SetResult();

        Assert.Same(capture, finished);
    }

    [Fact]
    public async Task CaptureAsync_DbFailure_DoesNotNotify()
    {
        _repo.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("db down")));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.CaptureAsync(Valid(), CancellationToken.None));
        await Task.Delay(100);

        Assert.Empty(_notifier.ReceivedCalls());
    }

    [Fact]
    public async Task CaptureAsync_DbFailure_Propagates_SoApiReturns5xx()
    {
        _repo.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("db down")));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.CaptureAsync(Valid(), CancellationToken.None));
    }

    [Fact]
    public async Task ListAsync_NormalizesStatusPagingAndSearch()
    {
        _repo.ListAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((new List<LandingLead>(), 0));

        var result = await _sut.ListAsync("bogus", "  ivan ", 0, 5000, CancellationToken.None);

        await _repo.Received(1).ListAsync("all", "ivan", 1, 100, Arg.Any<CancellationToken>());
        Assert.Equal(1, result.Page);
        Assert.Equal(100, result.PageSize);
    }

    [Fact]
    public async Task CountUnprocessedAsync_ReturnsRepositoryCount()
    {
        _repo.CountUnprocessedAsync(Arg.Any<CancellationToken>()).Returns(7);

        var dto = await _sut.CountUnprocessedAsync(CancellationToken.None);

        Assert.Equal(7, dto.Unprocessed);
    }

    [Fact]
    public async Task UpdateAsync_NotFound_ReturnsError()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((LandingLead?)null);

        var (lead, error) = await _sut.UpdateAsync(
            Guid.NewGuid(), new UpdateLeadRequest(true, null), Guid.NewGuid(), CancellationToken.None);

        Assert.Null(lead);
        Assert.Contains("not found", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateAsync_MarkProcessed_SetsActorAndTimestamp_ThenUnmarkClears()
    {
        var entity = LandingLead.Create("Ivan", "+380671234567", null, null);
        _repo.GetByIdAsync(entity.Id, Arg.Any<CancellationToken>()).Returns(entity);
        var actor = Guid.NewGuid();

        var (processed, err1) = await _sut.UpdateAsync(
            entity.Id, new UpdateLeadRequest(true, "  called back "), actor, CancellationToken.None);

        Assert.Null(err1);
        Assert.True(processed!.IsProcessed);
        Assert.Equal(actor, processed.ProcessedByUserId);
        Assert.NotNull(processed.ProcessedAt);
        Assert.Equal("called back", processed.AdminNote);

        var (reopened, err2) = await _sut.UpdateAsync(
            entity.Id, new UpdateLeadRequest(false, ""), actor, CancellationToken.None);

        Assert.Null(err2);
        Assert.False(reopened!.IsProcessed);
        Assert.Null(reopened.ProcessedAt);
        Assert.Null(reopened.ProcessedByUserId);
        Assert.Null(reopened.AdminNote);
        await _repo.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_NoteTooLong_ReturnsError_DoesNotSave()
    {
        var entity = LandingLead.Create("Ivan", "+380671234567", null, null);
        _repo.GetByIdAsync(entity.Id, Arg.Any<CancellationToken>()).Returns(entity);

        var (_, error) = await _sut.UpdateAsync(
            entity.Id, new UpdateLeadRequest(null, new string('n', 1001)), null, CancellationToken.None);

        Assert.NotNull(error);
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
