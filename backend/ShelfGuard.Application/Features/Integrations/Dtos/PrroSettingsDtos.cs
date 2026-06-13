namespace ShelfGuard.Application.Features.Integrations.Dtos;

/// <summary>
/// ПРРО provider settings as returned to the web UI (TASK-071). Secrets are
/// write-only: licenseKey is masked as «••••» + last 4 chars, cashierPassword /
/// cashierPinCode as «••••»; all three are null when not set.
/// </summary>
/// <param name="Provider">"checkbox" | "disabled".</param>
/// <param name="Source">"tenant" (integration_configs row) | "env" (PRRO__* fallback) | "none".</param>
public sealed record PrroSettingsDto(
    string Provider,
    bool IsEnabled,
    string? BaseUrl,
    string? LicenseKey,
    string? CashierLogin,
    string? CashierPassword,
    string? CashierPinCode,
    string Source,
    DateTime? UpdatedAt);

/// <summary>
/// PUT /api/settings/prro body (also accepted by POST /test as the candidate config).
/// Secret fields (licenseKey, cashierPassword, cashierPinCode): null or the masked
/// placeholder keeps the stored value; empty string clears it.
/// </summary>
public sealed record UpsertPrroSettingsRequest(
    string? Provider,
    bool IsEnabled = true,
    string? BaseUrl = null,
    string? LicenseKey = null,
    string? CashierLogin = null,
    string? CashierPassword = null,
    string? CashierPinCode = null);

/// <summary>POST /api/settings/prro/test result. Always HTTP 200; failures land in Ok/Error.</summary>
public sealed record PrroTestResult(
    bool Ok,
    string Provider,
    string? FiscalNumber,
    bool? IsTest,
    bool? HasOpenShift,
    bool CashierOk,
    string? Error);
