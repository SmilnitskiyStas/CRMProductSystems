using NSubstitute;
using ShelfGuard.Application.Features.Integrations;
using ShelfGuard.Application.Features.Integrations.Dtos;
using ShelfGuard.Application.Features.Pos.Fiscal;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Prro;

/// <summary>
/// Unit tests for PrroSettingsService (TASK-071):
/// - secret masking in GET
/// - keep-on-masked-PUT semantics
/// - resolution: DB row → env fallback → none
/// - factory.Create called from TestAsync
/// </summary>
public sealed class PrroSettingsServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private const string CheckboxJson =
        """{"provider":"checkbox","base_url":"https://api.checkbox.in.ua/api/v1","license_key":"LK-ABC123456789","cashier_login":"kasir","cashier_password":"secret","cashier_pin_code":"1234"}""";

    private readonly IIntegrationRepository _repo = Substitute.For<IIntegrationRepository>();
    private readonly IFiscalServiceFactory _factory = Substitute.For<IFiscalServiceFactory>();
    private readonly PrroSettingsService _sut;

    public PrroSettingsServiceTests()
    {
        _sut = new PrroSettingsService(_repo, _factory);
    }

    // ── GET masking ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_masks_license_key_with_last_4_chars()
    {
        _repo.GetByServiceAsync(TenantId, "prro", Arg.Any<CancellationToken>())
            .Returns(MakeRow(CheckboxJson, isEnabled: true));

        var dto = await _sut.GetAsync(TenantId);

        Assert.Equal(PrroSecrets.MaskToken + "6789", dto.LicenseKey); // last 4 of "LK-ABC123456789"
        Assert.Equal("checkbox", dto.Provider);
        Assert.Equal("kasir", dto.CashierLogin);       // non-secret, not masked
        Assert.Equal("tenant", dto.Source);
    }

    [Fact]
    public async Task GetAsync_masks_cashier_password_fully()
    {
        _repo.GetByServiceAsync(TenantId, "prro", Arg.Any<CancellationToken>())
            .Returns(MakeRow(CheckboxJson, isEnabled: true));

        var dto = await _sut.GetAsync(TenantId);

        Assert.Equal(PrroSecrets.MaskToken, dto.CashierPassword);
        Assert.Equal(PrroSecrets.MaskToken, dto.CashierPinCode);
    }

    [Fact]
    public async Task GetAsync_returns_null_secrets_when_not_set()
    {
        var json = """{"provider":"checkbox","base_url":"https://api.checkbox.in.ua/api/v1","license_key":"LK-ABC"}""";
        _repo.GetByServiceAsync(TenantId, "prro", Arg.Any<CancellationToken>())
            .Returns(MakeRow(json, isEnabled: true));

        var dto = await _sut.GetAsync(TenantId);

        Assert.Null(dto.CashierPassword);  // not in JSON → masked as null
        Assert.Null(dto.CashierPinCode);
    }

    [Fact]
    public async Task GetAsync_falls_back_to_env_when_no_db_row()
    {
        _repo.GetByServiceAsync(TenantId, "prro", Arg.Any<CancellationToken>())
            .Returns((IntegrationConfig?)null);
        _factory.EnvFallback.Returns(new PrroConnectionConfig(
            "checkbox", "https://api.checkbox.in.ua/api/v1",
            LicenseKey: "ENV-KEY-7890", CashierLogin: "env-login"));

        var dto = await _sut.GetAsync(TenantId);

        Assert.Equal("env", dto.Source);
        Assert.Equal(PrroSecrets.MaskToken + "7890", dto.LicenseKey);
        Assert.Equal("env-login", dto.CashierLogin);
    }

    [Fact]
    public async Task GetAsync_returns_none_when_neither_db_nor_env()
    {
        _repo.GetByServiceAsync(TenantId, "prro", Arg.Any<CancellationToken>())
            .Returns((IntegrationConfig?)null);
        _factory.EnvFallback.Returns((PrroConnectionConfig?)null);

        var dto = await _sut.GetAsync(TenantId);

        Assert.Equal("none", dto.Source);
        Assert.Equal(PrroConnectionConfig.ProviderDisabled, dto.Provider);
        Assert.False(dto.IsEnabled);
    }

    // ── PUT: masked-value keeps stored secret ──────────────────────────────

    [Fact]
    public async Task UpsertAsync_masked_license_key_keeps_stored()
    {
        _repo.GetByServiceAsync(TenantId, "prro", Arg.Any<CancellationToken>())
            .Returns(MakeRow(CheckboxJson, isEnabled: true));

        var request = new UpsertPrroSettingsRequest(
            Provider: "checkbox",
            IsEnabled: true,
            LicenseKey: PrroSecrets.MaskToken + "6789",   // masked round-trip
            CashierPassword: PrroSecrets.MaskToken,
            CashierPinCode: PrroSecrets.MaskToken);

        var (dto, error) = await _sut.UpsertAsync(TenantId, request);

        Assert.Null(error);
        // The stored JSON should contain the original un-masked license key.
        await _repo.Received(1).UpsertAsync(
            TenantId, "prro",
            Arg.Is<string>(json => json.Contains("LK-ABC123456789")),
            Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpsertAsync_empty_string_clears_cashier_password()
    {
        _repo.GetByServiceAsync(TenantId, "prro", Arg.Any<CancellationToken>())
            .Returns(MakeRow(CheckboxJson, isEnabled: true));

        var request = new UpsertPrroSettingsRequest(
            Provider: "checkbox",
            IsEnabled: true,
            LicenseKey: PrroSecrets.MaskToken + "6789",
            CashierPassword: string.Empty);   // explicit clear

        await _sut.UpsertAsync(TenantId, request);

        await _repo.Received(1).UpsertAsync(
            TenantId, "prro",
            Arg.Is<string>(json => !json.Contains("cashier_password")),
            Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpsertAsync_unknown_provider_returns_error()
    {
        var (dto, error) = await _sut.UpsertAsync(TenantId,
            new UpsertPrroSettingsRequest(Provider: "fake-provider", IsEnabled: true));

        Assert.Null(dto);
        Assert.NotNull(error);
        Assert.Contains("fake-provider", error);
    }

    [Fact]
    public async Task UpsertAsync_checkbox_without_license_key_returns_error()
    {
        _repo.GetByServiceAsync(TenantId, "prro", Arg.Any<CancellationToken>())
            .Returns((IntegrationConfig?)null);

        var (dto, error) = await _sut.UpsertAsync(TenantId,
            new UpsertPrroSettingsRequest(Provider: "checkbox", IsEnabled: true,
                LicenseKey: null, CashierPinCode: "1234"));

        Assert.Null(dto);
        Assert.NotNull(error);
        Assert.Contains("License key", error);
    }

    // ── TestAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task TestAsync_disabled_provider_returns_not_configured_error()
    {
        _repo.GetByServiceAsync(TenantId, "prro", Arg.Any<CancellationToken>())
            .Returns(MakeRow("""{"provider":"disabled"}""", isEnabled: true));
        _factory.EnvFallback.Returns((PrroConnectionConfig?)null);

        var result = await _sut.TestAsync(TenantId, candidate: null);

        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
        _factory.DidNotReceive().Create(Arg.Any<Guid>(), Arg.Any<PrroConnectionConfig>());
    }

    [Fact]
    public async Task TestAsync_no_config_no_env_returns_not_configured_error()
    {
        _repo.GetByServiceAsync(TenantId, "prro", Arg.Any<CancellationToken>())
            .Returns((IntegrationConfig?)null);
        _factory.EnvFallback.Returns((PrroConnectionConfig?)null);

        var result = await _sut.TestAsync(TenantId, candidate: null);

        Assert.False(result.Ok);
        Assert.Contains("налаштований", result.Error);
    }

    [Fact]
    public async Task TestAsync_with_candidate_merges_masked_secrets_from_stored()
    {
        _repo.GetByServiceAsync(TenantId, "prro", Arg.Any<CancellationToken>())
            .Returns(MakeRow(CheckboxJson, isEnabled: true));

        var mockClient = Substitute.For<IFiscalService>();
        mockClient.PingAsync(Arg.Any<CancellationToken>())
            .Returns(new FiscalHealthResult(true, "checkbox", "TEST582378", true, false, null));
        mockClient.CheckCashierAsync(Arg.Any<CancellationToken>())
            .Returns(new FiscalCashierResult(true, "Тест Касир", null));

        // Factory should be called with merged config that has the real license key.
        _factory.Create(TenantId, Arg.Is<PrroConnectionConfig>(c => c.LicenseKey == "LK-ABC123456789"))
            .Returns(mockClient);

        var candidate = new UpsertPrroSettingsRequest(
            Provider: "checkbox",
            IsEnabled: true,
            LicenseKey: PrroSecrets.MaskToken + "6789");   // masked

        var result = await _sut.TestAsync(TenantId, candidate);

        Assert.True(result.Ok);
        Assert.True(result.CashierOk);
        Assert.Equal("TEST582378", result.FiscalNumber);
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private static IntegrationConfig MakeRow(string json, bool isEnabled) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            Service = "prro",
            Config = json,
            IsEnabled = isEnabled,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
}
