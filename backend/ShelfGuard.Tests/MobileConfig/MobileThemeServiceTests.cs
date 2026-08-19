using NSubstitute;
using ShelfGuard.Application.Features.MobileConfig;
using ShelfGuard.Application.Features.MobileConfig.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.MobileConfig;

/// <summary>
/// TASK-536 — <see cref="MobileThemeService"/> orchestration: get-or-create the
/// <see cref="MobileConfiguration"/> root row, create-vs-update-in-place theme row, default
/// proposal on GET, tenant isolation, and validation-failure short circuiting. Whitelist rule
/// coverage lives in <c>MobileThemeValidatorTests</c> — this suite wires up the real
/// <see cref="MobileThemeValidator"/> (no mock), same convention <c>MobileConfigDraftServiceTests</c>
/// established for its validator.
/// </summary>
public sealed class MobileThemeServiceTests
{
    private readonly IMobileConfigurationRepository _repo = Substitute.For<IMobileConfigurationRepository>();
    private readonly IActivityLogRepository _activityLogs = Substitute.For<IActivityLogRepository>();
    private readonly MobileThemeService _sut;

    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private static UpdateMobileThemeRequest ValidRequest(string primaryColor = "#1E40AF") => new(
        LogoUrl: "https://cdn.example.com/logo.png",
        PrimaryColor: primaryColor,
        SecondaryColor: "#222222",
        BackgroundColor: "#FFFFFF",
        SurfaceColor: "#F7F7F7",
        TextPrimaryColor: "#111111",
        TextSecondaryColor: "#777777",
        ButtonRadius: 20,
        CardRadius: 24,
        SpacingPreset: "compact");

    public MobileThemeServiceTests()
    {
        _sut = new MobileThemeService(_repo, new MobileThemeValidator(), _activityLogs);
    }

    // ── GetThemeAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetThemeAsync_returns_built_in_defaults_with_null_UpdatedAt_when_no_configuration_exists()
    {
        _repo.GetByTenantIdAsync(TenantA, Arg.Any<CancellationToken>()).Returns((MobileConfiguration?)null);

        var dto = await _sut.GetThemeAsync(TenantA);

        Assert.Null(dto.UpdatedAt);
        Assert.Equal("#1E40AF", dto.PrimaryColor); // MobileTheme.CreateDefault's built-in default
        Assert.Equal("comfortable", dto.SpacingPreset);
    }

    [Fact]
    public async Task GetThemeAsync_returns_built_in_defaults_when_configuration_exists_but_has_no_theme_yet()
    {
        var config = MobileConfiguration.Create(TenantA);
        _repo.GetByTenantIdAsync(TenantA, Arg.Any<CancellationToken>()).Returns(config);

        var dto = await _sut.GetThemeAsync(TenantA);

        Assert.Null(dto.UpdatedAt);
    }

    [Fact]
    public async Task GetThemeAsync_returns_the_persisted_theme_with_a_non_null_UpdatedAt()
    {
        var config = MobileConfiguration.Create(TenantA);
        var theme = MobileTheme.CreateDefault(config.Id, TenantA);
        theme.Update(null, "#ABCDEF", "#111111", "#FFFFFF", "#EEEEEE", "#000000", "#333333", 5, 6, "compact");
        SetThemeNavigation(config, theme);

        _repo.GetByTenantIdAsync(TenantA, Arg.Any<CancellationToken>()).Returns(config);

        var dto = await _sut.GetThemeAsync(TenantA);

        Assert.NotNull(dto.UpdatedAt);
        Assert.Equal("#ABCDEF", dto.PrimaryColor);
        Assert.Equal(5, dto.ButtonRadius);
        Assert.Equal("compact", dto.SpacingPreset);
    }

    // ── UpdateThemeAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateThemeAsync_rejects_invalid_request_and_persists_nothing()
    {
        var invalid = ValidRequest(primaryColor: "not-a-hex-color");

        var (theme, errors) = await _sut.UpdateThemeAsync(TenantA, UserId, invalid);

        Assert.Null(theme);
        Assert.NotEmpty(errors);
        await _repo.DidNotReceive().AddAsync(Arg.Any<MobileConfiguration>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().AddThemeAsync(Arg.Any<MobileTheme>(), Arg.Any<CancellationToken>());
        _repo.DidNotReceive().UpdateTheme(Arg.Any<MobileTheme>());
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _activityLogs.DidNotReceive().LogAsync(Arg.Any<ActivityLog>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateThemeAsync_creates_MobileConfiguration_and_theme_when_neither_exists_yet()
    {
        _repo.GetByTenantIdAsync(TenantA, Arg.Any<CancellationToken>()).Returns((MobileConfiguration?)null);

        var (dto, errors) = await _sut.UpdateThemeAsync(TenantA, UserId, ValidRequest());

        Assert.Empty(errors);
        Assert.NotNull(dto);
        Assert.Equal("#1E40AF", dto!.PrimaryColor);
        Assert.NotNull(dto.UpdatedAt);

        await _repo.Received(1).AddAsync(
            Arg.Is<MobileConfiguration>(c => c.TenantId == TenantA), Arg.Any<CancellationToken>());
        await _repo.Received(1).AddThemeAsync(
            Arg.Is<MobileTheme>(t => t.TenantId == TenantA && t.PrimaryColor == "#1E40AF"),
            Arg.Any<CancellationToken>());
        // Unlike MobileConfigDraftService's first-draft path (TASK-534b), MobileConfiguration
        // holds no FK pointer back at MobileTheme, so a single SaveChangesAsync suffices — no
        // mutual-pointer circular-dependency risk here.
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateThemeAsync_creates_a_theme_row_when_configuration_exists_but_has_no_theme_yet()
    {
        var config = MobileConfiguration.Create(TenantA);
        _repo.GetByTenantIdAsync(TenantA, Arg.Any<CancellationToken>()).Returns(config);

        var (dto, errors) = await _sut.UpdateThemeAsync(TenantA, UserId, ValidRequest());

        Assert.Empty(errors);
        Assert.NotNull(dto);
        await _repo.DidNotReceive().AddAsync(Arg.Any<MobileConfiguration>(), Arg.Any<CancellationToken>());
        await _repo.Received(1).AddThemeAsync(
            Arg.Is<MobileTheme>(t => t.MobileConfigurationId == config.Id), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateThemeAsync_updates_the_existing_theme_row_in_place()
    {
        var config = MobileConfiguration.Create(TenantA);
        var existingTheme = MobileTheme.CreateDefault(config.Id, TenantA);
        SetThemeNavigation(config, existingTheme);
        _repo.GetByTenantIdAsync(TenantA, Arg.Any<CancellationToken>()).Returns(config);

        var (dto, errors) = await _sut.UpdateThemeAsync(TenantA, UserId, ValidRequest(primaryColor: "#00FF00"));

        Assert.Empty(errors);
        Assert.Equal("#00FF00", dto!.PrimaryColor);
        await _repo.DidNotReceive().AddThemeAsync(Arg.Any<MobileTheme>(), Arg.Any<CancellationToken>());
        _repo.Received(1).UpdateTheme(Arg.Is<MobileTheme>(t => t.Id == existingTheme.Id && t.PrimaryColor == "#00FF00"));
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Tenant_isolation_one_tenants_theme_update_never_touches_another_tenants_configuration()
    {
        var configA = MobileConfiguration.Create(TenantA);
        var themeA = MobileTheme.CreateDefault(configA.Id, TenantA);
        SetThemeNavigation(configA, themeA);

        var configB = MobileConfiguration.Create(TenantB);
        // TenantB has no theme yet.

        _repo.GetByTenantIdAsync(TenantA, Arg.Any<CancellationToken>()).Returns(configA);
        _repo.GetByTenantIdAsync(TenantB, Arg.Any<CancellationToken>()).Returns(configB);

        var (dtoB, errorsB) = await _sut.UpdateThemeAsync(TenantB, UserId, ValidRequest());

        Assert.Empty(errorsB);
        await _repo.Received(1).AddThemeAsync(
            Arg.Is<MobileTheme>(t => t.TenantId == TenantB), Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().AddThemeAsync(
            Arg.Is<MobileTheme>(t => t.TenantId == TenantA), Arg.Any<CancellationToken>());
        _repo.DidNotReceive().UpdateTheme(Arg.Any<MobileTheme>());
    }

    // ── Audit logging (TASK-550) ────────────────────────────────────────────

    [Fact]
    public async Task UpdateThemeAsync_logs_mobileconfig_theme_updated_on_success()
    {
        var config = MobileConfiguration.Create(TenantA);
        var existingTheme = MobileTheme.CreateDefault(config.Id, TenantA);
        SetThemeNavigation(config, existingTheme);
        _repo.GetByTenantIdAsync(TenantA, Arg.Any<CancellationToken>()).Returns(config);

        await _sut.UpdateThemeAsync(TenantA, UserId, ValidRequest(primaryColor: "#00FF00"));

        await _activityLogs.Received(1).LogAsync(
            Arg.Is<ActivityLog>(a =>
                a.TenantId == TenantA && a.UserId == UserId &&
                a.Action == "mobileconfig.theme_updated" &&
                a.EntityType == "mobile_theme" &&
                a.EntityId == existingTheme.Id),
            Arg.Any<CancellationToken>());
        await _activityLogs.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // MobileConfiguration.Theme has a private setter (only fixed up by EF Core's real Include()
    // during materialization) — tests simulate that same post-load shape via reflection rather
    // than adding a test-only public mutator to the domain entity. Same pattern
    // MobileConfigDraftServiceTests uses for DraftVersion.
    private static void SetThemeNavigation(MobileConfiguration config, MobileTheme theme)
    {
        typeof(MobileConfiguration)
            .GetProperty(nameof(MobileConfiguration.Theme))!
            .SetValue(config, theme);
    }
}
