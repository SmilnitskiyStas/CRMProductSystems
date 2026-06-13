using ShelfGuard.Application.Features.Integrations.Dtos;
using ShelfGuard.Application.Features.Pos.Fiscal;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Integrations;

public interface IPrroSettingsService
{
    /// <summary>Current ПРРО settings with masked secrets (never returns raw credentials).</summary>
    Task<PrroSettingsDto> GetAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Upserts integration_configs (service='prro'). Masked secrets keep stored values.</summary>
    Task<(PrroSettingsDto? Result, string? Error)> UpsertAsync(
        Guid tenantId, UpsertPrroSettingsRequest request, CancellationToken ct = default);

    /// <summary>
    /// Tests connectivity using the submitted config (merged with stored secrets) or,
    /// when no body is sent, the stored/env config. Ping + cashier signin only —
    /// no shift side effects.
    /// </summary>
    Task<PrroTestResult> TestAsync(
        Guid tenantId, UpsertPrroSettingsRequest? candidate, CancellationToken ct = default);
}

public sealed class PrroSettingsService : IPrroSettingsService
{
    private const string Service = "prro";

    private static readonly string[] AllowedProviders =
        [PrroConnectionConfig.ProviderCheckbox, PrroConnectionConfig.ProviderDisabled];

    private readonly IIntegrationRepository _repo;
    private readonly IFiscalServiceFactory _factory;

    public PrroSettingsService(IIntegrationRepository repo, IFiscalServiceFactory factory)
    {
        _repo = repo;
        _factory = factory;
    }

    public async Task<PrroSettingsDto> GetAsync(Guid tenantId, CancellationToken ct = default)
    {
        var row = await _repo.GetByServiceAsync(tenantId, Service, ct);
        if (row is not null)
        {
            var stored = PrroConnectionConfig.FromJson(row.Config)
                ?? new PrroConnectionConfig(PrroConnectionConfig.ProviderDisabled);
            return ToDto(stored, row.IsEnabled, "tenant", row.UpdatedAt);
        }

        if (_factory.EnvFallback is { } env)
            return ToDto(env, isEnabled: true, "env", updatedAt: null);

        return new PrroSettingsDto(
            PrroConnectionConfig.ProviderDisabled, IsEnabled: false,
            BaseUrl: null, LicenseKey: null, CashierLogin: null,
            CashierPassword: null, CashierPinCode: null,
            Source: "none", UpdatedAt: null);
    }

    public async Task<(PrroSettingsDto? Result, string? Error)> UpsertAsync(
        Guid tenantId, UpsertPrroSettingsRequest request, CancellationToken ct = default)
    {
        var provider = request.Provider?.Trim().ToLowerInvariant();
        if (provider is null || !AllowedProviders.Contains(provider))
            return (null, $"Unknown provider: '{request.Provider}'. Allowed: {string.Join(", ", AllowedProviders)}.");

        var existing = await _repo.GetByServiceAsync(tenantId, Service, ct);
        var stored = existing is null ? null : PrroConnectionConfig.FromJson(existing.Config);

        var merged = MergeWithStored(provider, request, stored);

        if (merged.IsCheckbox && string.IsNullOrWhiteSpace(merged.LicenseKey))
            return (null, "License key is required for the Checkbox provider.");

        await _repo.UpsertAsync(tenantId, Service, merged.ToJson(), request.IsEnabled, ct);

        var saved = await _repo.GetByServiceAsync(tenantId, Service, ct);
        return (ToDto(merged, request.IsEnabled, "tenant", saved?.UpdatedAt), null);
    }

    public async Task<PrroTestResult> TestAsync(
        Guid tenantId, UpsertPrroSettingsRequest? candidate, CancellationToken ct = default)
    {
        var existing = await _repo.GetByServiceAsync(tenantId, Service, ct);
        var stored = existing is null ? null : PrroConnectionConfig.FromJson(existing.Config);

        PrroConnectionConfig? effective;
        if (candidate is not null)
        {
            var provider = candidate.Provider?.Trim().ToLowerInvariant()
                ?? stored?.Provider
                ?? PrroConnectionConfig.ProviderDisabled;
            effective = MergeWithStored(provider, candidate, stored);
        }
        else
        {
            effective = stored ?? _factory.EnvFallback;
        }

        if (effective is null || !effective.IsCheckbox)
            return new PrroTestResult(
                Ok: false, Provider: effective?.Provider ?? PrroConnectionConfig.ProviderDisabled,
                FiscalNumber: null, IsTest: null, HasOpenShift: null, CashierOk: false,
                Error: "ПРРО провайдер не налаштований або вимкнений.");

        if (string.IsNullOrWhiteSpace(effective.LicenseKey))
            return new PrroTestResult(
                Ok: false, Provider: effective.Provider,
                FiscalNumber: null, IsTest: null, HasOpenShift: null, CashierOk: false,
                Error: "License key не вказано. Введіть ключ ліцензії каси з кабінету Checkbox.");

        var client = _factory.Create(tenantId, effective);

        var health = await client.PingAsync(ct);
        if (!health.Reachable)
            return new PrroTestResult(
                Ok: false, Provider: effective.Provider,
                FiscalNumber: null, IsTest: null, HasOpenShift: null, CashierOk: false,
                Error: health.Error ?? "Касовий апарат недоступний.");

        var cashier = await client.CheckCashierAsync(ct);

        return new PrroTestResult(
            Ok: cashier.Ok,
            Provider: effective.Provider,
            FiscalNumber: health.FiscalNumber,
            IsTest: health.IsTestRegister,
            HasOpenShift: health.HasOpenShift,
            CashierOk: cashier.Ok,
            Error: cashier.Ok ? null : cashier.Error);
    }

    // ── mapping ─────────────────────────────────────────────────────────────

    /// <summary>Non-secret fields: incoming wins, null falls back to stored.
    /// Secrets: keep-on-masked/null, clear-on-empty (PrroSecrets.Merge).</summary>
    private static PrroConnectionConfig MergeWithStored(
        string provider, UpsertPrroSettingsRequest request, PrroConnectionConfig? stored) => new(
        Provider: provider,
        BaseUrl: Normalize(request.BaseUrl) ?? stored?.BaseUrl,
        LicenseKey: PrroSecrets.Merge(request.LicenseKey, stored?.LicenseKey),
        CashierLogin: request.CashierLogin ?? stored?.CashierLogin,
        CashierPassword: PrroSecrets.Merge(request.CashierPassword, stored?.CashierPassword),
        CashierPinCode: PrroSecrets.Merge(request.CashierPinCode, stored?.CashierPinCode));

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static PrroSettingsDto ToDto(
        PrroConnectionConfig config, bool isEnabled, string source, DateTime? updatedAt) => new(
        Provider: config.Provider,
        IsEnabled: isEnabled,
        BaseUrl: config.BaseUrl,
        LicenseKey: PrroSecrets.Mask(config.LicenseKey, visibleSuffix: 4),
        CashierLogin: config.CashierLogin,
        CashierPassword: PrroSecrets.Mask(config.CashierPassword),
        CashierPinCode: PrroSecrets.Mask(config.CashierPinCode),
        Source: source,
        UpdatedAt: updatedAt);

}
