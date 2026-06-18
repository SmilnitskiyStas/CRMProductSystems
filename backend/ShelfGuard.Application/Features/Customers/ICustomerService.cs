using ShelfGuard.Application.Common;

namespace ShelfGuard.Application.Features.Customers;

public interface ICustomerService
{
    Task<PagedResult<CustomerDto>> GetPagedAsync(Guid tenantId, int page, int pageSize, string? search, CancellationToken ct = default);
    Task<CustomerDetailDto?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct = default);
    Task<(CustomerDto? Customer, string? Error)> CreateAsync(Guid tenantId, CreateCustomerDto dto, CancellationToken ct = default);
    Task<(CustomerDto? Customer, string? Error)> UpdateAsync(Guid id, Guid tenantId, UpdateCustomerDto dto, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, Guid tenantId, CancellationToken ct = default);
}
