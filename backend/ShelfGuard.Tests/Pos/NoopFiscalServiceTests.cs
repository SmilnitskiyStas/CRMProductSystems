using ShelfGuard.Application.Features.Pos.Fiscal;
using Xunit;

namespace ShelfGuard.Tests.Pos;

public sealed class NoopFiscalServiceTests
{
    private readonly NoopFiscalService _service = new();

    [Fact]
    public async Task Receipt_stays_pending_fiscalization()
    {
        var result = await _service.CreateReceiptAsync(new FiscalReceiptRequest(
            Items: [new FiscalReceiptItem("P-001", "Товар", 10m, 1)],
            Payments: [new FiscalPayment(FiscalPaymentKind.Cash, 10m)]));

        Assert.Equal(FiscalReceiptStatus.PendingFiscalization, result.Status);
        Assert.Null(result.FiscalNumber);
        Assert.Null(result.ProviderReceiptId);
    }

    [Fact]
    public async Task Shift_operations_stay_pending()
    {
        Assert.Equal(FiscalShiftStatus.PendingFiscalization, (await _service.OpenShiftAsync()).Status);
        Assert.Equal(FiscalShiftStatus.PendingFiscalization, (await _service.CloseShiftAsync()).Status);
    }

    [Fact]
    public async Task Ping_reports_no_provider()
    {
        var health = await _service.PingAsync();
        Assert.False(health.Reachable);
        Assert.Equal("none", health.Provider);
        Assert.Contains("PRRO__PROVIDER", health.Error);
    }
}
