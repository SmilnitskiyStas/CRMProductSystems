using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ShelfGuard.Application.Features.Pos.Fiscal;
using ShelfGuard.Infrastructure.Integrations.Prro;
using Xunit;

namespace ShelfGuard.Tests.Pos;

public sealed class CheckboxFiscalClientTests
{
    private const string BaseUrl = "https://api.checkbox.test/api/v1";
    private const string LicenseKey = "test-license-key";

    // ── fake transport ─────────────────────────────────────────────────────

    private sealed record RecordedRequest(HttpMethod Method, string Path, string? Body, string? Bearer, string? LicenseKey);

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();
        public List<RecordedRequest> Requests { get; } = [];

        public void Enqueue(HttpStatusCode status, string json) =>
            _responses.Enqueue(_ => new HttpResponseMessage(status)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            });

        public void EnqueueTimeout() =>
            _responses.Enqueue(_ => throw new TaskCanceledException("timed out"));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri!.AbsolutePath,
                body,
                request.Headers.Authorization?.Parameter,
                request.Headers.TryGetValues("X-License-Key", out var keys) ? keys.First() : null));

            if (_responses.Count == 0)
                throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}");
            return _responses.Dequeue()(request);
        }
    }

    private static (CheckboxFiscalClient Client, FakeHandler Handler, CheckboxTokenStore Tokens) Build(
        string? pinCode = "1234", string? login = null, string? password = null)
    {
        var handler = new FakeHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl.TrimEnd('/') + "/") };
        var options = Options.Create(new PrroOptions
        {
            Provider = "checkbox",
            BaseUrl = BaseUrl,
            LicenseKey = LicenseKey,
            Cashier = new PrroOptions.CashierOptions
            {
                PinCode = pinCode ?? string.Empty,
                Login = login ?? string.Empty,
                Password = password ?? string.Empty,
            },
        });
        var tokens = new CheckboxTokenStore();
        return (new CheckboxFiscalClient(http, options, tokens), handler, tokens);
    }

    private const string TokenJson = """{"type":"bearer","token_type":"bearer","access_token":"tok-1"}""";
    private const string ShiftOpenedJson =
        """{"id":"shift-1","serial":7,"status":"OPENED","opened_at":"2026-06-12T10:00:00+00:00","closed_at":null}""";
    private const string ReceiptDoneJson =
        """{"id":"rcpt-1","serial":1,"status":"DONE","fiscal_code":"FC123456","fiscal_date":"2026-06-12T10:05:00+00:00","total_sum":12550,"tax_url":"https://cabinet.tax.gov.ua/r/123"}""";

    // ── signin flow ────────────────────────────────────────────────────────

    [Fact]
    public async Task First_call_signs_in_with_pin_and_license_key_then_uses_bearer()
    {
        var (client, handler, _) = Build(pinCode: "9876");
        handler.Enqueue(HttpStatusCode.OK, TokenJson);
        handler.Enqueue(HttpStatusCode.Accepted, ShiftOpenedJson);

        var result = await client.OpenShiftAsync();

        Assert.Equal(2, handler.Requests.Count);
        var signin = handler.Requests[0];
        Assert.Equal("/api/v1/cashier/signinPinCode", signin.Path);
        Assert.Equal(LicenseKey, signin.LicenseKey);
        Assert.Contains("\"pin_code\":\"9876\"", signin.Body);

        var open = handler.Requests[1];
        Assert.Equal("/api/v1/shifts", open.Path);
        Assert.Equal("tok-1", open.Bearer);
        Assert.Equal(LicenseKey, open.LicenseKey);

        Assert.Equal(FiscalShiftStatus.Opened, result.Status);
        Assert.Equal("shift-1", result.ProviderShiftId);
        Assert.Equal(7, result.Serial);
    }

    [Fact]
    public async Task Signs_in_with_login_password_when_no_pin_configured()
    {
        var (client, handler, _) = Build(pinCode: null, login: "kasir", password: "secret");
        handler.Enqueue(HttpStatusCode.OK, TokenJson);
        handler.Enqueue(HttpStatusCode.Accepted, ShiftOpenedJson);

        await client.OpenShiftAsync();

        var signin = handler.Requests[0];
        Assert.Equal("/api/v1/cashier/signin", signin.Path);
        Assert.Contains("\"login\":\"kasir\"", signin.Body);
        Assert.Contains("\"password\":\"secret\"", signin.Body);
    }

    [Fact]
    public async Task Missing_cashier_credentials_throw_config_error_without_http_call()
    {
        var (client, handler, _) = Build(pinCode: null);

        var ex = await Assert.ThrowsAsync<FiscalProviderException>(() => client.OpenShiftAsync());

        Assert.Equal("config.missing_cashier_credentials", ex.ProviderCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Token_is_cached_across_calls()
    {
        var (client, handler, _) = Build();
        handler.Enqueue(HttpStatusCode.OK, TokenJson);
        handler.Enqueue(HttpStatusCode.Accepted, ShiftOpenedJson);
        handler.Enqueue(HttpStatusCode.OK, ReceiptDoneJson);

        await client.OpenShiftAsync();
        await client.GetReceiptStatusAsync("rcpt-1");

        // Only one signin for two business calls.
        Assert.Equal(3, handler.Requests.Count);
        Assert.Single(handler.Requests, r => r.Path.EndsWith("signinPinCode"));
        Assert.Equal("tok-1", handler.Requests[2].Bearer);
    }

    [Fact]
    public async Task On_401_reauthenticates_and_retries_once()
    {
        var (client, handler, tokens) = Build();
        tokens.Set("stale-token");
        handler.Enqueue(HttpStatusCode.Unauthorized, """{"code":"token_expired","message":"token expired"}""");
        handler.Enqueue(HttpStatusCode.OK, """{"access_token":"tok-2"}""");
        handler.Enqueue(HttpStatusCode.Accepted, ShiftOpenedJson);

        var result = await client.OpenShiftAsync();

        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal("stale-token", handler.Requests[0].Bearer);
        Assert.Equal("/api/v1/cashier/signinPinCode", handler.Requests[1].Path);
        Assert.Equal("tok-2", handler.Requests[2].Bearer);
        Assert.Equal(FiscalShiftStatus.Opened, result.Status);
        Assert.Equal("tok-2", tokens.Token);
    }

    [Fact]
    public async Task On_403_not_authenticated_reauthenticates_and_retries_once()
    {
        // Live behavior (2026-06-12): Checkbox returns 403 {"message":"Not authenticated"}
        // for a missing/invalid token, not 401.
        var (client, handler, tokens) = Build();
        tokens.Set("stale-token");
        handler.Enqueue(HttpStatusCode.Forbidden, """{"message":"Not authenticated","code":null}""");
        handler.Enqueue(HttpStatusCode.OK, """{"access_token":"tok-2"}""");
        handler.Enqueue(HttpStatusCode.Accepted, ShiftOpenedJson);

        var result = await client.OpenShiftAsync();

        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal("tok-2", handler.Requests[2].Bearer);
        Assert.Equal(FiscalShiftStatus.Opened, result.Status);
    }

    [Fact]
    public async Task Genuine_permission_403_does_not_trigger_reauth()
    {
        var (client, handler, tokens) = Build();
        tokens.Set("tok-1");
        handler.Enqueue(HttpStatusCode.Forbidden,
            """{"code":"cashier.no_permission","message":"Недостатньо прав"}""");

        var ex = await Assert.ThrowsAsync<FiscalProviderException>(() => client.OpenShiftAsync());

        Assert.Single(handler.Requests); // no signin attempt
        Assert.Equal("cashier.no_permission", ex.ProviderCode);
    }

    // ── shifts ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CloseShift_posts_to_shifts_close_and_maps_status()
    {
        var (client, handler, tokens) = Build();
        tokens.Set("tok-1");
        handler.Enqueue(HttpStatusCode.Accepted,
            """{"id":"shift-1","serial":7,"status":"CLOSED","opened_at":"2026-06-12T10:00:00+00:00","closed_at":"2026-06-12T20:00:00+00:00"}""");

        var result = await client.CloseShiftAsync();

        Assert.Equal("/api/v1/shifts/close", handler.Requests[0].Path);
        Assert.Equal(FiscalShiftStatus.Closed, result.Status);
        Assert.NotNull(result.ClosedAt);
    }

    [Fact]
    public async Task GetShiftStatus_gets_shift_by_id_and_maps_status()
    {
        var (client, handler, tokens) = Build();
        tokens.Set("tok-1");
        handler.Enqueue(HttpStatusCode.OK,
            """{"id":"shift-1","serial":7,"status":"CREATED","opened_at":null,"closed_at":null}""");

        var result = await client.GetShiftStatusAsync("shift-1");

        Assert.Equal("/api/v1/shifts/shift-1", handler.Requests[0].Path);
        Assert.Equal("tok-1", handler.Requests[0].Bearer);
        Assert.Equal(FiscalShiftStatus.Created, result.Status);
        Assert.Equal("shift-1", result.ProviderShiftId);
        Assert.Equal(7, result.Serial);
    }

    // ── receipts ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateReceipt_maps_items_to_goods_in_kopecks_and_thousandth_quantities()
    {
        var (client, handler, tokens) = Build();
        tokens.Set("tok-1");
        handler.Enqueue(HttpStatusCode.Created, ReceiptDoneJson);

        var request = new FiscalReceiptRequest(
            Items:
            [
                new FiscalReceiptItem("P-001", "Молоко 1л", UnitPrice: 45.50m, Quantity: 2, Barcode: "4820000000017"),
                new FiscalReceiptItem("P-002", "Сир ваговий", UnitPrice: 320.00m, Quantity: 0.250m),
            ],
            Payments: [new FiscalPayment(FiscalPaymentKind.Cash, 171.00m)],
            LocalReceiptId: "11111111-2222-3333-4444-555555555555",
            CashierName: "Тест Касир");

        var result = await client.CreateReceiptAsync(request);

        var sent = handler.Requests[0];
        Assert.Equal("/api/v1/receipts/sell", sent.Path);

        using var doc = JsonDocument.Parse(sent.Body!);
        var root = doc.RootElement;
        Assert.Equal("11111111-2222-3333-4444-555555555555", root.GetProperty("id").GetString());
        Assert.Equal("Тест Касир", root.GetProperty("cashier_name").GetString());

        var goods = root.GetProperty("goods");
        Assert.Equal(2, goods.GetArrayLength());

        var milk = goods[0];
        Assert.Equal("P-001", milk.GetProperty("good").GetProperty("code").GetString());
        Assert.Equal("4820000000017", milk.GetProperty("good").GetProperty("barcode").GetString());
        Assert.Equal(4550, milk.GetProperty("good").GetProperty("price").GetInt64());   // 45.50 грн → kopecks
        Assert.Equal(2000, milk.GetProperty("quantity").GetInt64());                    // 2 шт → 2000

        var cheese = goods[1];
        Assert.Equal(32000, cheese.GetProperty("good").GetProperty("price").GetInt64()); // 320.00 грн
        Assert.Equal(250, cheese.GetProperty("quantity").GetInt64());                    // 0.250 кг → 250
        Assert.False(cheese.GetProperty("good").TryGetProperty("barcode", out _));       // null omitted

        var payment = root.GetProperty("payments")[0];
        Assert.Equal("CASH", payment.GetProperty("type").GetString());
        Assert.Equal(17100, payment.GetProperty("value").GetInt64());

        Assert.Equal(FiscalReceiptStatus.Done, result.Status);
        Assert.Equal("rcpt-1", result.ProviderReceiptId);
        Assert.Equal("FC123456", result.FiscalNumber);
        Assert.Equal(125.50m, result.TotalAmount);
        Assert.Equal("https://cabinet.tax.gov.ua/r/123", result.TaxUrl);
    }

    [Fact]
    public async Task Cashless_payment_maps_to_CASHLESS()
    {
        var (client, handler, tokens) = Build();
        tokens.Set("tok-1");
        handler.Enqueue(HttpStatusCode.Created, ReceiptDoneJson);

        await client.CreateReceiptAsync(new FiscalReceiptRequest(
            Items: [new FiscalReceiptItem("P-001", "Товар", 10m, 1)],
            Payments: [new FiscalPayment(FiscalPaymentKind.Cashless, 10m)]));

        Assert.Contains("\"type\":\"CASHLESS\"", handler.Requests[0].Body);
    }

    [Fact]
    public async Task GetReceiptStatus_maps_created_receipt_without_fiscal_code()
    {
        var (client, handler, tokens) = Build();
        tokens.Set("tok-1");
        handler.Enqueue(HttpStatusCode.OK,
            """{"id":"rcpt-2","serial":2,"status":"CREATED","fiscal_code":null,"fiscal_date":null,"total_sum":500,"tax_url":null}""");

        var result = await client.GetReceiptStatusAsync("rcpt-2");

        Assert.Equal("/api/v1/receipts/rcpt-2", handler.Requests[0].Path);
        Assert.Equal(FiscalReceiptStatus.Created, result.Status);
        Assert.Null(result.FiscalNumber);
        Assert.Equal(5.00m, result.TotalAmount);
    }

    // ── ping ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Ping_uses_license_key_only_and_maps_register_info()
    {
        var (client, handler, _) = Build();
        handler.Enqueue(HttpStatusCode.OK,
            """{"id":"reg-1","fiscal_number":"TEST582378","is_test":true,"has_shift":false}""");

        var result = await client.PingAsync();

        var sent = handler.Requests[0];
        Assert.Equal("/api/v1/cash-registers/info", sent.Path);
        Assert.Null(sent.Bearer); // no cashier signin needed
        Assert.Equal(LicenseKey, sent.LicenseKey);

        Assert.True(result.Reachable);
        Assert.Equal("TEST582378", result.FiscalNumber);
        Assert.True(result.IsTestRegister);
        Assert.False(result.HasOpenShift);
    }

    [Fact]
    public async Task Ping_never_throws_on_provider_error()
    {
        var (client, handler, _) = Build();
        handler.Enqueue(HttpStatusCode.Forbidden, """{"code":"bad_license","message":"Невірний ключ ліцензії"}""");

        var result = await client.PingAsync();

        Assert.False(result.Reachable);
        Assert.Contains("Невірний ключ ліцензії", result.Error);
    }

    // ── error mapping ──────────────────────────────────────────────────────

    [Fact]
    public async Task Provider_403_maps_to_FiscalProviderException_with_code()
    {
        var (client, handler, _) = Build();
        handler.Enqueue(HttpStatusCode.Forbidden,
            """{"code":"cashier.invalid_credentials","message":"Невірний пінкод"}""");

        var ex = await Assert.ThrowsAsync<FiscalProviderException>(() => client.OpenShiftAsync());

        Assert.Equal("cashier.invalid_credentials", ex.ProviderCode);
        Assert.Equal(403, ex.HttpStatus);
        Assert.False(ex.IsTransient);
        Assert.Contains("Невірний пінкод", ex.Message);
    }

    [Fact]
    public async Task Server_5xx_is_transient()
    {
        var (client, handler, tokens) = Build();
        tokens.Set("tok-1");
        handler.Enqueue(HttpStatusCode.BadGateway, "<html>bad gateway</html>");

        var ex = await Assert.ThrowsAsync<FiscalProviderException>(() => client.OpenShiftAsync());

        Assert.Equal(502, ex.HttpStatus);
        Assert.True(ex.IsTransient);
    }

    [Fact]
    public async Task Timeout_maps_to_transient_transport_error()
    {
        var (client, handler, tokens) = Build();
        tokens.Set("tok-1");
        handler.EnqueueTimeout();

        var ex = await Assert.ThrowsAsync<FiscalProviderException>(() => client.OpenShiftAsync());

        Assert.Equal("transport.timeout", ex.ProviderCode);
        Assert.True(ex.IsTransient);
    }

    // ── CheckCashierAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task CheckCashier_signs_in_and_reads_cashier_profile()
    {
        var (client, handler, _) = Build(pinCode: "9876");
        handler.Enqueue(HttpStatusCode.OK, TokenJson);
        handler.Enqueue(HttpStatusCode.OK,
            """{"id":"c-1","full_name":"Тест Касир","login":"kasir"}""");

        var result = await client.CheckCashierAsync();

        Assert.True(result.Ok);
        Assert.Equal("Тест Касир", result.CashierName);
        Assert.Null(result.Error);
        Assert.Equal("/api/v1/cashier/profile", handler.Requests[1].Path);
        Assert.Equal("tok-1", handler.Requests[1].Bearer);
    }

    [Fact]
    public async Task CheckCashier_returns_error_on_bad_credentials()
    {
        var (client, handler, _) = Build(pinCode: "0000");
        handler.Enqueue(HttpStatusCode.Forbidden,
            """{"code":"cashier.invalid_credentials","message":"Невірний пінкод"}""");

        var result = await client.CheckCashierAsync();

        Assert.False(result.Ok);
        Assert.Null(result.CashierName);
        Assert.Contains("Невірний пінкод", result.Error);
    }

    // ── unit conversion ────────────────────────────────────────────────────

    [Theory]
    [InlineData("45.50", 4550)]
    [InlineData("0.01", 1)]
    [InlineData("99.995", 10000)] // away-from-zero rounding
    [InlineData("171", 17100)]
    public void ToKopecks_rounds_correctly(string uah, long expected) =>
        Assert.Equal(expected, CheckboxFiscalClient.ToKopecks(decimal.Parse(uah, System.Globalization.CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData("1", 1000)]
    [InlineData("2.25", 2250)]
    [InlineData("0.2505", 251)]
    public void ToQuantity_uses_thousandths(string units, long expected) =>
        Assert.Equal(expected, CheckboxFiscalClient.ToQuantity(decimal.Parse(units, System.Globalization.CultureInfo.InvariantCulture)));
}
