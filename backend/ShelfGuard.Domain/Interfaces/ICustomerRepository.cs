using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

public interface ICustomerRepository
{
    Task<List<Customer>> GetAllAsync(Guid tenantId, CancellationToken ct);
    Task<(List<Customer> Items, int Total)> GetPagedAsync(Guid tenantId, int page, int pageSize, string? search, CancellationToken ct);
    Task<Customer?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct);
    Task<Customer?> GetByIdWithTransactionsAsync(Guid id, Guid tenantId, CancellationToken ct);
    Task<bool> ExistsByPhoneAsync(string phone, Guid tenantId, Guid? excludeId, CancellationToken ct);

    /// <summary>
    /// TASK-405 (Loyalty Фаза 0): finds a tenant's existing Customer by exact phone match —
    /// used to auto-link a LoyaltyMembership to the tenant's own CRM record instead of
    /// always creating a new one. Phone comparison is exact/tenant-scoped, same as
    /// <see cref="ExistsByPhoneAsync"/> — the caller is responsible for normalizing the
    /// phone consistently before calling (ConsumerAccount.Phone is already normalized).
    /// </summary>
    Task<Customer?> FindByPhoneAsync(string phone, Guid tenantId, CancellationToken ct);
    Task<Customer> CreateAsync(Customer customer, CancellationToken ct);
    Task UpdateAsync(Customer customer, CancellationToken ct);
    Task DeleteAsync(Guid id, Guid tenantId, CancellationToken ct);
}
