using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

/// <summary>
/// Client-confirmed marketplace order receipts (TASK-586, ADR-033). No AsNoTracking on the
/// single-row reads — mirrors ReceiptRepository.GetByIdAsync, since MarketplaceOrderReceiptService
/// mutates the loaded entity in place before calling SaveChangesAsync.
/// </summary>
public sealed class MarketplaceOrderReceiptRepository : IMarketplaceOrderReceiptRepository
{
    private readonly AppDbContext _db;

    public MarketplaceOrderReceiptRepository(AppDbContext db) => _db = db;

    public Task<MarketplaceOrderReceipt?> GetByOrderIdAsync(
        Guid marketplaceOrderId, CancellationToken ct = default) =>
        _db.MarketplaceOrderReceipts
            .Include(r => r.Items).ThenInclude(i => i.Product)
            .Include(r => r.Items).ThenInclude(i => i.OrderItem)
            .Include(r => r.DestinationStore)
            .FirstOrDefaultAsync(r => r.MarketplaceOrderId == marketplaceOrderId, ct);

    public Task<MarketplaceOrderReceipt?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.MarketplaceOrderReceipts
            .Include(r => r.Items).ThenInclude(i => i.Product)
            .Include(r => r.Items).ThenInclude(i => i.OrderItem)
            .Include(r => r.DestinationStore)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task AddAsync(MarketplaceOrderReceipt receipt, CancellationToken ct = default) =>
        await _db.MarketplaceOrderReceipts.AddAsync(receipt, ct);

    public void Update(MarketplaceOrderReceipt receipt) =>
        _db.MarketplaceOrderReceipts.Update(receipt);

    public void UpdateItem(MarketplaceOrderReceiptItem item) =>
        _db.MarketplaceOrderReceiptItems.Update(item);

    public async Task AddStockAsync(ProductStock stock, CancellationToken ct = default) =>
        await _db.ProductStocks.AddAsync(stock, ct);

    public async Task AddMovementAsync(StockMovement movement, CancellationToken ct = default) =>
        await _db.StockMovements.AddAsync(movement, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
