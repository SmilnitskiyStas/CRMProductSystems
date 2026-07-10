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
    private readonly LandingLeadService _sut;

    public LandingLeadServiceTests() =>
        _sut = new LandingLeadService(_repo, NullLogger<LandingLeadService>.Instance);

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
}
