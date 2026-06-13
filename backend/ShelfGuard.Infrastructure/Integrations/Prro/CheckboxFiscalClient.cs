using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using ShelfGuard.Application.Features.Pos.Fiscal;

namespace ShelfGuard.Infrastructure.Integrations.Prro;

/// <summary>
/// Checkbox (checkbox.ua) SaaS ПРРО client — IFiscalService implementation (ADR-012).
/// Wire facts (verified against /api/openapi.json v2.99 + live test register 2026-06-12):
///   • auth: POST cashier/signin {login,password} or cashier/signinPinCode {pin_code}+X-License-Key
///     → {access_token}; shift/receipt calls use Bearer token; X-License-Key identifies the register
///   • money is integer kopecks; quantity is integer thousandths (1 шт = 1000, 2.25 кг = 2250)
///   • shift statuses: CREATED/OPENING/OPENED/CLOSING/CLOSED; receipt: CREATED/DONE/ERROR/CANCELLATION/CANCELLED
///   • errors: {"code": "...", "message": "..."} (403/4xx), FastAPI {"detail": [...]} on 422
/// Checkbox shapes never leave this file — Application sees only the Fiscal* records.
/// </summary>
public sealed class CheckboxFiscalClient : IFiscalService
{
    private const string ClientName = "ShelfGuard";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly PrroOptions _options;
    private readonly CheckboxTokenStore _tokens;

    public CheckboxFiscalClient(HttpClient http, IOptions<PrroOptions> options, CheckboxTokenStore tokens)
    {
        _http = http;
        _options = options.Value;
        _tokens = tokens;

        if (_http.BaseAddress is null)
            _http.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
    }

    // ── IFiscalService ─────────────────────────────────────────────────────

    public async Task<FiscalHealthResult> PingAsync(CancellationToken ct = default)
    {
        try
        {
            // License-key-only endpoint — works without cashier signin (verified live).
            using var request = NewRequest(HttpMethod.Get, "cash-registers/info");
            using var response = await SendRawAsync(request, ct);
            await EnsureSuccessAsync(response, ct);

            var info = await ReadAsync<CashRegisterInfo>(response, ct);
            return new FiscalHealthResult(
                Reachable: true,
                Provider: "checkbox",
                FiscalNumber: info.FiscalNumber,
                IsTestRegister: info.IsTest,
                HasOpenShift: info.HasShift,
                Error: null);
        }
        catch (Exception ex) when (ex is FiscalProviderException or HttpRequestException or TaskCanceledException)
        {
            return new FiscalHealthResult(false, "checkbox", null, null, null, ex.Message);
        }
    }

    public async Task<FiscalCashierResult> CheckCashierAsync(CancellationToken ct = default)
    {
        try
        {
            var token = _tokens.Token ?? await SigninAsync(ct);
            using var request = NewRequest(HttpMethod.Get, "cashier/profile");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await SendRawAsync(request, ct);
            await EnsureSuccessAsync(response, ct);
            var profile = await ReadAsync<CashierProfile>(response, ct);
            return new FiscalCashierResult(Ok: true, CashierName: profile.FullName, Error: null);
        }
        catch (FiscalProviderException ex)
        {
            return new FiscalCashierResult(Ok: false, CashierName: null, Error: ex.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new FiscalCashierResult(Ok: false, CashierName: null, Error: ex.Message);
        }
    }

    public async Task<FiscalShiftResult> OpenShiftAsync(CancellationToken ct = default)
    {
        // POST shifts requires both the bearer token and X-License-Key. 202 → async opening.
        var shift = await SendAuthorizedAsync<ShiftModel>(
            () => NewRequest(HttpMethod.Post, "shifts", body: new { }), ct);
        return MapShift(shift);
    }

    public async Task<FiscalShiftResult> CloseShiftAsync(CancellationToken ct = default)
    {
        var shift = await SendAuthorizedAsync<ShiftModel>(
            () => NewRequest(HttpMethod.Post, "shifts/close", body: new { }), ct);
        return MapShift(shift);
    }

    public async Task<FiscalShiftResult> GetShiftStatusAsync(string providerShiftId, CancellationToken ct = default)
    {
        var shift = await SendAuthorizedAsync<ShiftModel>(
            () => NewRequest(HttpMethod.Get, $"shifts/{providerShiftId}"), ct);
        return MapShift(shift);
    }

    public async Task<FiscalReceiptResult> CreateReceiptAsync(FiscalReceiptRequest request, CancellationToken ct = default)
    {
        var payload = new ReceiptSellPayload
        {
            Id = request.LocalReceiptId,
            CashierName = string.IsNullOrWhiteSpace(request.CashierName) ? null : request.CashierName,
            Goods = request.Items.Select(i => new GoodItemPayload
            {
                Good = new GoodPayload
                {
                    Code = i.Code,
                    Name = i.Name,
                    Barcode = string.IsNullOrWhiteSpace(i.Barcode) ? null : i.Barcode,
                    Price = ToKopecks(i.UnitPrice),
                },
                Quantity = ToQuantity(i.Quantity),
            }).ToList(),
            Payments = request.Payments.Select(p => new PaymentPayload
            {
                Type = p.Kind == FiscalPaymentKind.Cash ? "CASH" : "CASHLESS",
                Value = ToKopecks(p.Amount),
            }).ToList(),
        };

        var receipt = await SendAuthorizedAsync<ReceiptModel>(
            () => NewRequest(HttpMethod.Post, "receipts/sell", body: payload), ct);
        return MapReceipt(receipt);
    }

    public async Task<FiscalReceiptResult> GetReceiptStatusAsync(string providerReceiptId, CancellationToken ct = default)
    {
        var receipt = await SendAuthorizedAsync<ReceiptModel>(
            () => NewRequest(HttpMethod.Get, $"receipts/{providerReceiptId}"), ct);
        return MapReceipt(receipt);
    }

    // ── auth + transport ───────────────────────────────────────────────────

    private async Task<T> SendAuthorizedAsync<T>(Func<HttpRequestMessage> requestFactory, CancellationToken ct)
    {
        var token = _tokens.Token ?? await SigninAsync(ct);

        using (var request = requestFactory())
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await SendRawAsync(request, ct);

            if (!await IsAuthFailureAsync(response, ct))
            {
                await EnsureSuccessAsync(response, ct);
                return await ReadAsync<T>(response, ct);
            }
        }

        // Token expired/revoked → re-auth once and retry.
        _tokens.Invalidate();
        token = await SigninAsync(ct);

        using (var retry = requestFactory())
        {
            retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await SendRawAsync(retry, ct);
            await EnsureSuccessAsync(response, ct);
            return await ReadAsync<T>(response, ct);
        }
    }

    private async Task<string> SigninAsync(CancellationToken ct)
    {
        await _tokens.SigninLock.WaitAsync(ct);
        try
        {
            // Another caller may have signed in while we waited.
            if (_tokens.Token is { } cached)
                return cached;

            var usePin = !string.IsNullOrWhiteSpace(_options.Cashier.PinCode);
            if (!usePin && string.IsNullOrWhiteSpace(_options.Cashier.Login))
                throw new FiscalProviderException(
                    "Checkbox cashier credentials are not configured " +
                    "(PRRO__CASHIER__PINCODE or PRRO__CASHIER__LOGIN/PASSWORD).",
                    providerCode: "config.missing_cashier_credentials");

            using var request = usePin
                ? NewRequest(HttpMethod.Post, "cashier/signinPinCode", body: new { pin_code = _options.Cashier.PinCode })
                : NewRequest(HttpMethod.Post, "cashier/signin",
                    body: new { login = _options.Cashier.Login, password = _options.Cashier.Password });

            using var response = await SendRawAsync(request, ct);
            await EnsureSuccessAsync(response, ct);

            var auth = await ReadAsync<AccessTokenResponse>(response, ct);
            if (string.IsNullOrEmpty(auth.AccessToken))
                throw new FiscalProviderException("Checkbox signin returned no access_token.", httpStatus: 200);

            _tokens.Set(auth.AccessToken);
            return auth.AccessToken;
        }
        finally
        {
            _tokens.SigninLock.Release();
        }
    }

    private HttpRequestMessage NewRequest(HttpMethod method, string path, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Client-Name", ClientName);
        if (!string.IsNullOrEmpty(_options.LicenseKey))
            request.Headers.Add("X-License-Key", _options.LicenseKey);
        if (body is not null)
            request.Content = JsonContent.Create(body, options: Json);
        return request;
    }

    private async Task<HttpResponseMessage> SendRawAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            return await _http.SendAsync(request, ct);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new FiscalProviderException(
                $"Checkbox request timed out after {_http.Timeout.TotalSeconds:0}s.",
                providerCode: "transport.timeout", isTransient: true, inner: ex);
        }
        catch (HttpRequestException ex)
        {
            throw new FiscalProviderException(
                $"Checkbox is unreachable: {ex.Message}",
                providerCode: "transport.network", isTransient: true, inner: ex);
        }
    }

    /// <summary>
    /// Checkbox signals a missing/expired token as 401 OR as
    /// 403 {"message":"Not authenticated","code":null} (verified live 2026-06-12) —
    /// both must trigger re-auth, while a genuine permission 403 must not.
    /// </summary>
    private static async Task<bool> IsAuthFailureAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            return true;
        if (response.StatusCode != HttpStatusCode.Forbidden)
            return false;

        var body = await response.Content.ReadAsStringAsync(ct); // buffered — EnsureSuccessAsync can re-read
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("message", out var m)
                && m.ValueKind == JsonValueKind.String
                && string.Equals(m.GetString(), "Not authenticated", StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;

        var status = (int)response.StatusCode;
        var body = await response.Content.ReadAsStringAsync(ct);

        string? code = null;
        var message = $"Checkbox returned HTTP {status}.";
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (doc.RootElement.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String)
                    code = c.GetString();
                if (doc.RootElement.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
                    message = m.GetString() ?? message;
                else if (doc.RootElement.TryGetProperty("detail", out var d))
                    message = $"Checkbox validation error: {d.GetRawText()}";
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body — keep the generic message.
        }

        throw new FiscalProviderException(message, code, status, isTransient: status >= 500);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        var result = await response.Content.ReadFromJsonAsync<T>(Json, ct);
        return result ?? throw new FiscalProviderException(
            "Checkbox returned an empty response body.", httpStatus: (int)response.StatusCode);
    }

    // ── unit + status mapping ──────────────────────────────────────────────

    /// <summary>UAH → integer kopecks (Checkbox money unit).</summary>
    internal static long ToKopecks(decimal uah) =>
        (long)Math.Round(uah * 100m, MidpointRounding.AwayFromZero);

    internal static decimal FromKopecks(long? kopecks) =>
        (kopecks ?? 0) / 100m;

    /// <summary>Units → integer thousandths (Checkbox: 1 шт = 1000, 2.25 кг = 2250).</summary>
    internal static long ToQuantity(decimal units) =>
        (long)Math.Round(units * 1000m, MidpointRounding.AwayFromZero);

    private static FiscalShiftResult MapShift(ShiftModel shift) => new(
        ProviderShiftId: shift.Id,
        Status: shift.Status?.ToUpperInvariant() switch
        {
            "CREATED" => FiscalShiftStatus.Created,
            "OPENING" => FiscalShiftStatus.Opening,
            "OPENED" => FiscalShiftStatus.Opened,
            "CLOSING" => FiscalShiftStatus.Closing,
            "CLOSED" => FiscalShiftStatus.Closed,
            _ => FiscalShiftStatus.Created,
        },
        Serial: shift.Serial,
        OpenedAt: shift.OpenedAt,
        ClosedAt: shift.ClosedAt);

    private static FiscalReceiptResult MapReceipt(ReceiptModel receipt) => new(
        ProviderReceiptId: receipt.Id,
        Status: receipt.Status?.ToUpperInvariant() switch
        {
            "CREATED" => FiscalReceiptStatus.Created,
            "DONE" => FiscalReceiptStatus.Done,
            "ERROR" => FiscalReceiptStatus.Error,
            "CANCELLATION" or "CANCELLED" => FiscalReceiptStatus.Cancelled,
            _ => FiscalReceiptStatus.Created,
        },
        FiscalNumber: string.IsNullOrEmpty(receipt.FiscalCode) ? null : receipt.FiscalCode,
        FiscalDate: receipt.FiscalDate,
        TotalAmount: FromKopecks(receipt.TotalSum),
        TaxUrl: receipt.TaxUrl);

    // ── wire DTOs (Checkbox shapes — never leave this file) ────────────────

    private sealed class AccessTokenResponse
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
    }

    private sealed class CashierProfile
    {
        [JsonPropertyName("full_name")] public string? FullName { get; set; }
    }

    private sealed class CashRegisterInfo
    {
        [JsonPropertyName("fiscal_number")] public string? FiscalNumber { get; set; }
        [JsonPropertyName("is_test")] public bool? IsTest { get; set; }
        [JsonPropertyName("has_shift")] public bool? HasShift { get; set; }
    }

    private sealed class ShiftModel
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("serial")] public int? Serial { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("opened_at")] public DateTimeOffset? OpenedAt { get; set; }
        [JsonPropertyName("closed_at")] public DateTimeOffset? ClosedAt { get; set; }
    }

    private sealed class ReceiptModel
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("fiscal_code")] public string? FiscalCode { get; set; }
        [JsonPropertyName("fiscal_date")] public DateTimeOffset? FiscalDate { get; set; }
        [JsonPropertyName("total_sum")] public long? TotalSum { get; set; }
        [JsonPropertyName("tax_url")] public string? TaxUrl { get; set; }
    }

    private sealed class ReceiptSellPayload
    {
        [JsonPropertyName("id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Id { get; set; }

        [JsonPropertyName("cashier_name")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? CashierName { get; set; }

        [JsonPropertyName("goods")] public List<GoodItemPayload> Goods { get; set; } = [];
        [JsonPropertyName("payments")] public List<PaymentPayload> Payments { get; set; } = [];
    }

    private sealed class GoodItemPayload
    {
        [JsonPropertyName("good")] public GoodPayload Good { get; set; } = new();
        [JsonPropertyName("quantity")] public long Quantity { get; set; }
    }

    private sealed class GoodPayload
    {
        [JsonPropertyName("code")] public string Code { get; set; } = string.Empty;
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;

        [JsonPropertyName("barcode")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Barcode { get; set; }

        /// <summary>Kopecks per 1000 quantity units.</summary>
        [JsonPropertyName("price")] public long Price { get; set; }
    }

    private sealed class PaymentPayload
    {
        [JsonPropertyName("type")] public string Type { get; set; } = "CASH";
        /// <summary>Kopecks.</summary>
        [JsonPropertyName("value")] public long Value { get; set; }
    }
}
