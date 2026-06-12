using System.Text.Json;
using Microsoft.Extensions.Options;
using ShelfGuard.Application.Features.Pos.Fiscal;
using ShelfGuard.Infrastructure.Integrations.Prro;
using Xunit;
using Xunit.Abstractions;

namespace ShelfGuard.Tests.Pos;

/// <summary>
/// Live end-to-end test against the real Checkbox TEST API (api.checkbox.in.ua).
/// Skipped (passes trivially) unless env var PRRO_LIVE_E2E=1 — normal `dotnet test`
/// and CI never hit the network. Credentials are read from PRRO__* env vars, with
/// fallback to backend/ShelfGuard.Api/appsettings.Secrets.json (gitignored).
///
/// Run:  PRRO_LIVE_E2E=1 dotnet test --filter "FullyQualifiedName~CheckboxLiveE2E"
///
/// Flow: ping → cashier signin (PIN) → open shift (poll CREATED→OPENED) →
///       sell receipt (poll →DONE, fiscal_code/tax_url) → close shift (poll →CLOSED).
/// Uses a TEST register (is_test=true) — no real fiscal documents are produced.
/// </summary>
public sealed class CheckboxLiveE2ETests
{
    private readonly ITestOutputHelper _log;

    public CheckboxLiveE2ETests(ITestOutputHelper log) => _log = log;

    private static bool Enabled =>
        Environment.GetEnvironmentVariable("PRRO_LIVE_E2E") == "1";

    [Fact]
    public async Task Full_fiscal_flow_against_live_test_register()
    {
        if (!Enabled)
        {
            _log.WriteLine("PRRO_LIVE_E2E != 1 — live e2e skipped.");
            return;
        }

        var options = LoadOptions();
        Assert.False(string.IsNullOrEmpty(options.LicenseKey), "PRRO license key is not configured.");

        using var http = new HttpClient
        {
            BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds),
        };
        IFiscalService client = new CheckboxFiscalClient(http, Options.Create(options), new CheckboxTokenStore());

        // ── a) ping / cash register info (license key only) ────────────────
        var health = await client.PingAsync();
        _log.WriteLine($"[ping] reachable={health.Reachable} fiscal_number={health.FiscalNumber} " +
                       $"is_test={health.IsTestRegister} has_shift={health.HasOpenShift} error={health.Error}");
        Assert.True(health.Reachable, $"Ping failed: {health.Error}");
        Assert.Equal("TEST582378", health.FiscalNumber);
        Assert.True(health.IsTestRegister, "Refusing to run e2e against a non-test register.");

        // ── b+c) signin happens lazily inside the first authorized call: open shift ──
        FiscalShiftResult shift;
        try
        {
            shift = await client.OpenShiftAsync();
            _log.WriteLine($"[open-shift] id={shift.ProviderShiftId} serial={shift.Serial} status={shift.Status}");
        }
        catch (FiscalProviderException ex) when (health.HasOpenShift == true || ex.ProviderCode?.Contains("already") == true)
        {
            // A shift is already open on the register (e.g. previous run crashed) — reuse it.
            _log.WriteLine($"[open-shift] already open ({ex.ProviderCode}: {ex.Message}) — reusing current shift.");
            shift = new FiscalShiftResult(null, FiscalShiftStatus.Opened, null, null, null);
        }

        // Checkbox opens shifts asynchronously: CREATED → OPENED. Poll until OPENED.
        if (shift.ProviderShiftId is not null && shift.Status != FiscalShiftStatus.Opened)
        {
            shift = await PollAsync(
                () => client.GetShiftStatusAsync(shift.ProviderShiftId!),
                s => s.Status == FiscalShiftStatus.Opened,
                "shift→OPENED",
                s => $"status={s.Status}");
        }
        Assert.Equal(FiscalShiftStatus.Opened, shift.Status);
        _log.WriteLine($"[shift] OPENED id={shift.ProviderShiftId} serial={shift.Serial} opened_at={shift.OpenedAt:O}");

        try
        {
            // ── d) sell receipt: 1 test item, CASH ──────────────────────────
            var localId = Guid.NewGuid().ToString();
            var request = new FiscalReceiptRequest(
                Items: [new FiscalReceiptItem(Code: "SG-TEST-001", Name: "Тестовий товар", UnitPrice: 5.50m, Quantity: 1m)],
                Payments: [new FiscalPayment(FiscalPaymentKind.Cash, 5.50m)],
                LocalReceiptId: localId,
                CashierName: "Тестовий касир");

            var receipt = await client.CreateReceiptAsync(request);
            _log.WriteLine($"[receipt] created id={receipt.ProviderReceiptId} (local id={localId}) " +
                           $"status={receipt.Status} total={receipt.TotalAmount}");
            Assert.NotNull(receipt.ProviderReceiptId);
            Assert.Equal(localId, receipt.ProviderReceiptId); // idempotency id is honored

            // ── e) poll receipt status until DONE via GetReceiptStatusAsync ──
            if (receipt.Status != FiscalReceiptStatus.Done)
            {
                receipt = await PollAsync(
                    () => client.GetReceiptStatusAsync(receipt.ProviderReceiptId!),
                    r => r.Status is FiscalReceiptStatus.Done or FiscalReceiptStatus.Error,
                    "receipt→DONE",
                    r => $"status={r.Status} fiscal={r.FiscalNumber}");
            }
            Assert.Equal(FiscalReceiptStatus.Done, receipt.Status);
            _log.WriteLine($"[receipt] DONE id={receipt.ProviderReceiptId} fiscal_code={receipt.FiscalNumber} " +
                           $"fiscal_date={receipt.FiscalDate:O} total={receipt.TotalAmount} tax_url={receipt.TaxUrl}");
            Assert.Equal(5.50m, receipt.TotalAmount);
        }
        finally
        {
            // ── f) close shift (Z-report) and poll until CLOSED ─────────────
            var closing = await client.CloseShiftAsync();
            _log.WriteLine($"[close-shift] id={closing.ProviderShiftId} status={closing.Status}");
            if (closing.ProviderShiftId is not null && closing.Status != FiscalShiftStatus.Closed)
            {
                closing = await PollAsync(
                    () => client.GetShiftStatusAsync(closing.ProviderShiftId!),
                    s => s.Status == FiscalShiftStatus.Closed,
                    "shift→CLOSED",
                    s => $"status={s.Status}");
            }
            Assert.Equal(FiscalShiftStatus.Closed, closing.Status);
            _log.WriteLine($"[shift] CLOSED id={closing.ProviderShiftId} closed_at={closing.ClosedAt:O}");
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private async Task<T> PollAsync<T>(
        Func<Task<T>> fetch, Func<T, bool> done, string what, Func<T, string> describe,
        int timeoutSeconds = 60)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        var attempt = 0;
        while (true)
        {
            var result = await fetch();
            attempt++;
            _log.WriteLine($"[poll {what}] #{attempt} {describe(result)}");
            if (done(result))
                return result;
            if (DateTimeOffset.UtcNow >= deadline)
                throw new TimeoutException($"Polling '{what}' did not finish within {timeoutSeconds}s.");
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }

    /// <summary>Env vars PRRO__* win; otherwise fall back to the gitignored Api secrets file.</summary>
    private static PrroOptions LoadOptions()
    {
        var options = new PrroOptions { Provider = "checkbox" };

        var secrets = FindSecretsFile();
        if (secrets is not null)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(secrets));
            if (doc.RootElement.TryGetProperty("PRRO", out var prro))
            {
                options.BaseUrl = Str(prro, "BASEURL") ?? options.BaseUrl;
                options.LicenseKey = Str(prro, "LICENSEKEY") ?? options.LicenseKey;
                if (prro.TryGetProperty("CASHIER", out var cashier))
                {
                    options.Cashier.Login = Str(cashier, "LOGIN") ?? options.Cashier.Login;
                    options.Cashier.Password = Str(cashier, "PASSWORD") ?? options.Cashier.Password;
                    options.Cashier.PinCode = Str(cashier, "PINCODE") ?? options.Cashier.PinCode;
                }
            }
        }

        options.BaseUrl = Env("PRRO__BASEURL") ?? options.BaseUrl;
        options.LicenseKey = Env("PRRO__LICENSEKEY") ?? options.LicenseKey;
        options.Cashier.Login = Env("PRRO__CASHIER__LOGIN") ?? options.Cashier.Login;
        options.Cashier.Password = Env("PRRO__CASHIER__PASSWORD") ?? options.Cashier.Password;
        options.Cashier.PinCode = Env("PRRO__CASHIER__PINCODE") ?? options.Cashier.PinCode;
        return options;

        static string? Env(string name)
        {
            var v = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrWhiteSpace(v) ? null : v;
        }

        static string? Str(JsonElement parent, string name) =>
            parent.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(p.GetString()) ? p.GetString() : null;
    }

    private static string? FindSecretsFile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "ShelfGuard.Api", "appsettings.Secrets.json");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}
