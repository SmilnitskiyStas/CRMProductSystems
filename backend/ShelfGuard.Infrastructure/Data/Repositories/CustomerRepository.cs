using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class CustomerRepository : ICustomerRepository
{
    private readonly AppDbContext _db;

    public CustomerRepository(AppDbContext db) => _db = db;

    public Task<List<Customer>> GetAllAsync(Guid tenantId, CancellationToken ct) =>
        _db.Customers
            .Where(c => c.TenantId == tenantId)
            .OrderBy(c => c.Name)
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
            .OrderBy(c => c.Name)
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
