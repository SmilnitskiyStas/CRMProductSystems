using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class CustomerRepository : ICustomerRepository
{
    private readonly AppDbContext _db;

    /// <summary>Ukrainian-collation ordering only where the provider supports it (Npgsql).</summary>
    private bool Uk => _db.Database.IsNpgsql();

    public CustomerRepository(AppDbContext db) => _db = db;

    // A fiscalization_failed receipt is a sale that never completed — excluded everywhere (same rule
    // as the marketing analytics and POS aggregates).
    private IQueryable<PosTransaction> CustomerTransactions(Guid customerId, Guid tenantId) =>
        _db.PosTransactions.AsNoTracking().Where(t =>
            t.TenantId == tenantId && t.CustomerId == customerId && t.Status != "fiscalization_failed");

    public async Task<CustomerActivityStatsRow> GetActivityStatsAsync(
        Guid customerId, Guid tenantId, DateTime nowUtc, CancellationToken ct)
    {
        var from30 = nowUtc.AddDays(-30);
        var from90 = nowUtc.AddDays(-90);
        var tx = CustomerTransactions(customerId, tenantId);

        var agg = await tx
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Count = g.Count(),
                Spent = g.Sum(t => t.TotalAmount),
                First = g.Min(t => (DateTime?)t.CreatedAt),
                Last = g.Max(t => (DateTime?)t.CreatedAt),
                C30 = g.Count(t => t.CreatedAt >= from30),
                S30 = g.Where(t => t.CreatedAt >= from30).Sum(t => (decimal?)t.TotalAmount) ?? 0m,
                C90 = g.Count(t => t.CreatedAt >= from90),
                S90 = g.Where(t => t.CreatedAt >= from90).Sum(t => (decimal?)t.TotalAmount) ?? 0m,
            })
            .FirstOrDefaultAsync(ct);

        var qty = await _db.PosTransactionItems.AsNoTracking()
            .Where(i => i.Transaction!.TenantId == tenantId && i.Transaction.CustomerId == customerId
                        && i.Transaction.Status != "fiscalization_failed")
            .SumAsync(i => (decimal?)i.Quantity, ct) ?? 0m;

        return agg is null
            ? new CustomerActivityStatsRow(0, 0m, null, null, 0, 0m, 0, 0m, 0m)
            : new CustomerActivityStatsRow(agg.Count, agg.Spent, agg.First, agg.Last, agg.C30, agg.S30, agg.C90, agg.S90, qty);
    }

    public async Task<IReadOnlyList<CustomerStoreActivityRow>> GetStoreActivityAsync(
        Guid customerId, Guid tenantId, CancellationToken ct)
    {
        var rows = await CustomerTransactions(customerId, tenantId)
            .GroupBy(t => new { t.StoreId, Name = t.Store != null ? t.Store.Name : "" })
            .Select(g => new
            {
                g.Key.StoreId,
                g.Key.Name,
                Receipts = g.Count(),
                Spent = g.Sum(t => t.TotalAmount),
                First = g.Min(t => t.CreatedAt),
                Last = g.Max(t => t.CreatedAt),
            })
            .OrderByDescending(x => x.Spent)
            .ToListAsync(ct);

        return rows.Select(r => new CustomerStoreActivityRow(r.StoreId, r.Name, r.Receipts, r.Spent, r.First, r.Last)).ToList();
    }

    public async Task<IReadOnlyList<CustomerMonthActivityRow>> GetMonthlyActivityAsync(
        Guid customerId, Guid tenantId, DateTime fromUtc, CancellationToken ct)
    {
        var rows = await CustomerTransactions(customerId, tenantId)
            .Where(t => t.CreatedAt >= fromUtc)
            .GroupBy(t => new { t.CreatedAt.Year, t.CreatedAt.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Receipts = g.Count(), Spent = g.Sum(t => t.TotalAmount) })
            .OrderBy(x => x.Year).ThenBy(x => x.Month)
            .ToListAsync(ct);

        return rows.Select(r => new CustomerMonthActivityRow(r.Year, r.Month, r.Receipts, r.Spent)).ToList();
    }

    public async Task<IReadOnlyList<CustomerTopProductRow>> GetTopProductsAsync(
        Guid customerId, Guid tenantId, int take, CancellationToken ct)
    {
        var rows = await _db.PosTransactionItems.AsNoTracking()
            .Where(i => i.Transaction!.TenantId == tenantId && i.Transaction.CustomerId == customerId
                        && i.Transaction.Status != "fiscalization_failed")
            .GroupBy(i => new { i.ProductId, Name = i.Product != null ? i.Product.Name : "" })
            .Select(g => new
            {
                g.Key.ProductId,
                g.Key.Name,
                Quantity = g.Sum(i => i.Quantity),
                Spent = g.Sum(i => i.PriceFinal * i.Quantity),
                Receipts = g.Select(i => i.TransactionId).Distinct().Count(),
                Last = g.Max(i => i.Transaction!.CreatedAt),
            })
            .OrderByDescending(x => x.Spent)
            .Take(take)
            .ToListAsync(ct);

        return rows.Select(r => new CustomerTopProductRow(r.ProductId, r.Name, r.Quantity, r.Spent, r.Receipts, r.Last)).ToList();
    }

    public async Task<(IReadOnlyList<CustomerReceiptRow> Items, int Total)> GetReceiptsPagedAsync(
        Guid customerId, Guid tenantId, int page, int pageSize, CancellationToken ct)
    {
        var query = CustomerTransactions(customerId, tenantId);
        var total = await query.CountAsync(ct);

        var tx = await query
            .Include(t => t.Store)
            .Include(t => t.Items).ThenInclude(i => i.Product)
            .OrderByDescending(t => t.CreatedAt).ThenBy(t => t.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = tx.Select(t => new CustomerReceiptRow(
                t.Id, t.ReceiptNumber, t.FiscalNumber, t.CreatedAt, t.StoreId, t.Store?.Name ?? "",
                t.TotalAmount, t.PaymentType, t.Status,
                t.Items.Select(i => new CustomerReceiptLineRow(
                    i.ProductId, i.Product?.Name ?? "", i.Quantity, i.PriceRetail, i.DiscountAmount,
                    i.PriceFinal * i.Quantity)).ToList()))
            .ToList();

        return (items, total);
    }

    public Task<List<Customer>> GetAllAsync(Guid tenantId, CancellationToken ct) =>
        _db.Customers
            .Where(c => c.TenantId == tenantId)
            .OrderByUk(c => c.Name, Uk)
            .ToListAsync(ct);

    public async Task<(List<Customer> Items, int Total)> GetPagedAsync(
        Guid tenantId, int page, int pageSize, string? search, CancellationToken ct)
    {
        var query = _db.Customers.Where(c => c.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(c =>
                EF.Functions.ILike(c.Name, $"%{term}%") ||
                (c.Phone != null && c.Phone.Contains(term)) ||
                (c.Email != null && EF.Functions.ILike(c.Email, $"%{term}%")));
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByUk(c => c.Name, Uk)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public Task<Customer?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct) =>
        _db.Customers.FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId, ct);

    public Task<Customer?> GetByIdWithTransactionsAsync(Guid id, Guid tenantId, CancellationToken ct) =>
        _db.Customers
            .Include(c => c.Transactions.OrderByDescending(t => t.CreatedAt).Take(20))
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId, ct);

    public Task<bool> ExistsByPhoneAsync(string phone, Guid tenantId, Guid? excludeId, CancellationToken ct)
    {
        var query = _db.Customers.Where(c => c.TenantId == tenantId && c.Phone == phone);
        if (excludeId.HasValue)
            query = query.Where(c => c.Id != excludeId.Value);
        return query.AnyAsync(ct);
    }

    public Task<Customer?> FindByPhoneAsync(string phone, Guid tenantId, CancellationToken ct) =>
        _db.Customers.FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Phone == phone, ct);

    public async Task<Customer> CreateAsync(Customer customer, CancellationToken ct)
    {
        await _db.Customers.AddAsync(customer, ct);
        await _db.SaveChangesAsync(ct);
        return customer;
    }

    public async Task UpdateAsync(Customer customer, CancellationToken ct)
    {
        _db.Customers.Update(customer);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, Guid tenantId, CancellationToken ct)
    {
        var customer = await _db.Customers
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId, ct);
        if (customer is null) return;

        _db.Customers.Remove(customer);
        await _db.SaveChangesAsync(ct);
    }
}
