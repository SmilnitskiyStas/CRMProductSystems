namespace ShelfGuard.Infrastructure.Integrations.Prro;

/// <summary>
/// Bound from the "PRRO" configuration section — env vars PRRO__PROVIDER, PRRO__BASEURL,
/// PRRO__LICENSEKEY, PRRO__CASHIER__LOGIN / PRRO__CASHIER__PASSWORD / PRRO__CASHIER__PINCODE.
/// Secrets only in .env / appsettings.Secrets.json — never committed (ADR-012).
/// </summary>
public sealed class PrroOptions
{
    public const string SectionName = "PRRO";

    /// <summary>"checkbox" enables CheckboxFiscalClient; empty → NoopFiscalService.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Checkbox test API. Prod: https://api.checkbox.ua/api/v1.
    /// Note: docs mention dev-api.checkbox.in.ua, but that host does not resolve —
    /// the working test host is api.checkbox.in.ua (verified live 2026-06-12).</summary>
    public string BaseUrl { get; set; } = "https://api.checkbox.in.ua/api/v1";

    /// <summary>X-License-Key — identifies the cash register.</summary>
    public string LicenseKey { get; set; } = string.Empty;

    public CashierOptions Cashier { get; set; } = new();

    public int TimeoutSeconds { get; set; } = 30;

    public sealed class CashierOptions
    {
        public string Login { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        /// <summary>If set, PIN signin (X-License-Key required) is preferred over login/password.</summary>
        public string PinCode { get; set; } = string.Empty;
    }
}
