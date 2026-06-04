using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class ProductRepository : IProductRepository
{
    private readonly AppDbContext _db;

    public ProductRepository(AppDbContext db) => _db = db;

    public async Task<IEnumerable<Product>> GetAllAsync(CancellationToken ct = default) =>
        await _db.Products.OrderBy(p => p.Name).ToListAsync(ct);

    public Task<Product?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.Products.FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<bool> ExistsBySkuAsync(string sku, CancellationToken ct = default) =>
        _db.Products.AnyAsync(p => p.Sku == sku, ct);

    public async Task AddAsync(Product product, CancellationToken ct = default) =>
        await _db.Products.AddAsync(product, ct);

    public void Update(Product product) =>
        _db.Products.Update(product);

    public void Remove(Product product) =>
        _db.Products.Remove(product);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);
}
