using ShelfGuard.Application.Common;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Customers;

public sealed class CustomerService : ICustomerService
{
    private readonly ICustomerRepository _repo;

    public CustomerService(ICustomerRepository repo) => _repo = repo;

    public async Task<PagedResult<CustomerDto>> GetPagedAsync(
        Guid tenantId, int page, int pageSize, string? search, CancellationToken ct = default)
    {
        var (customers, total) = await _repo.GetPagedAsync(tenantId, page, pageSize, search, ct);
        return new PagedResult<CustomerDto>
        {
            Items = customers.Select(ToDto).ToList(),
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
        };
    }

    public async Task<CustomerDetailDto?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct = default)
    {
        var customer = await _repo.GetByIdWithTransactionsAsync(id, tenantId, ct);
        return customer is null ? null : ToDetailDto(customer);
    }

    public async Task<(CustomerDto? Customer, string? Error)> CreateAsync(
        Guid tenantId, CreateCustomerDto dto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return (null, "Customer name is required.");

        if (!string.IsNullOrWhiteSpace(dto.Phone))
        {
            var phoneExists = await _repo.ExistsByPhoneAsync(dto.Phone.Trim(), tenantId, null, ct);
            if (phoneExists)
                return (null, $"Customer with phone '{dto.Phone.Trim()}' already exists.");
        }

        var customer = new Customer
        {
            TenantId = tenantId,
            Name = dto.Name.Trim(),
            Phone = dto.Phone?.Trim(),
            Email = dto.Email?.Trim(),
            Notes = dto.Notes?.Trim(),
            Tags = dto.Tags?.ToList() ?? [],
        };

        await _repo.CreateAsync(customer, ct);
        return (ToDto(customer), null);
    }

    public async Task<(CustomerDto? Customer, string? Error)> UpdateAsync(
        Guid id, Guid tenantId, UpdateCustomerDto dto, CancellationToken ct = default)
    {
        var customer = await _repo.GetByIdAsync(id, tenantId, ct);
        if (customer is null)
            return (null, "Customer not found.");

        if (string.IsNullOrWhiteSpace(dto.Name))
            return (null, "Customer name is required.");

        if (!string.IsNullOrWhiteSpace(dto.Phone))
        {
            var phoneExists = await _repo.ExistsByPhoneAsync(dto.Phone.Trim(), tenantId, id, ct);
            if (phoneExists)
                return (null, $"Customer with phone '{dto.Phone.Trim()}' already exists.");
        }

        customer.Name = dto.Name.Trim();
        customer.Phone = dto.Phone?.Trim();
        customer.Email = dto.Email?.Trim();
        customer.Notes = dto.Notes?.Trim();
        customer.Tags = dto.Tags?.ToList() ?? [];
        customer.UpdatedAt = DateTimeOffset.UtcNow;

        await _repo.UpdateAsync(customer, ct);
        return (ToDto(customer), null);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid tenantId, CancellationToken ct = default)
    {
        var customer = await _repo.GetByIdAsync(id, tenantId, ct);
        if (customer is null) return false;

        await _repo.DeleteAsync(id, tenantId, ct);
        return true;
    }

    private static CustomerDto ToDto(Customer c) => new(
        c.Id,
        c.Name,
        c.Phone,
        c.Email,
        c.Notes,
        c.Tags.ToArray(),
        c.TotalOrders,
        c.TotalSpent,
        c.CreatedAt.UtcDateTime
    );

    private static CustomerDetailDto ToDetailDto(Customer c) => new(
        c.Id,
        c.Name,
        c.Phone,
        c.Email,
        c.Notes,
        c.Tags.ToArray(),
        c.TotalOrders,
        c.TotalSpent,
        c.CreatedAt.UtcDateTime,
        c.Transactions
            .OrderByDescending(t => t.CreatedAt)
            .Take(20)
            .Select(t => new CustomerTransactionDto(
                t.Id,
                t.TotalAmount,
                t.PaymentType,
                t.CreatedAt,
                t.Status))
            .ToList()
    );
}
