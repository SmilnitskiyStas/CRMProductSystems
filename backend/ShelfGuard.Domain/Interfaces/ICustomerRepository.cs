using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

public interface ICustomerRepository
{
    Task<List<Customer>> GetAllAsync(Guid tenantId, CancellationToken ct);
    Task<(List<Customer> Items, int Total)> GetPagedAsync(Guid tenantId, int page, int pageSize, string? search, CancellationToken ct);
    Task<Customer?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct);
    Task<Customer?> GetByIdWithTransactionsAsync(Guid id, Guid tenantId, CancellationToken ct);
    Task<bool> ExistsByPhoneAsync(string phone, Guid tenantId, Guid? excludeId, CancellationToken ct);
    Task<Customer> CreateAsync(Customer customer, CancellationToken ct);
    Task UpdateAsync(Customer customer, CancellationToken ct);
    Task DeleteAsync(Guid id, Guid tenantId, CancellationToken ct);
}
