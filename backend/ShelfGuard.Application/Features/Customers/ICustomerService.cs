using ShelfGuard.Application.Common;
using ShelfGuard.Application.Features.ConsumerProfile.Dtos;

namespace ShelfGuard.Application.Features.Customers;

public interface ICustomerService
{
    Task<PagedResult<CustomerDto>> GetPagedAsync(Guid tenantId, int page, int pageSize, string? search, CancellationToken ct = default);
    Task<CustomerDetailDto?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct = default);
    Task<(CustomerDto? Customer, string? Error)> CreateAsync(Guid tenantId, CreateCustomerDto dto, CancellationToken ct = default);
    Task<(CustomerDto? Customer, string? Error)> UpdateAsync(Guid id, Guid tenantId, UpdateCustomerDto dto, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// TASK-621b. Staff-facing view of a <see cref="Customer"/>'s linked
    /// <see cref="ShelfGuard.Domain.Entities.ConsumerAccountProfileChange"/> history — delegates to
    /// <see cref="ConsumerProfile.IConsumerProfileService.GetProfileChangeHistoryAsync"/> once the
    /// customer's <see cref="ShelfGuard.Domain.Entities.LoyaltyMembership"/> resolves a
    /// <c>ConsumerAccountId</c>. A customer with no linked loyalty membership (never joined the
    /// program at this tenant) has no consumer-side profile to show history for — returns an empty
    /// page, not an error, same convention as the rest of the TASK-618 detail-view fields.
    /// </summary>
    Task<PagedResult<ConsumerProfileChangeDto>> GetProfileChangeHistoryAsync(
        Guid customerId, Guid tenantId, int page, int pageSize, CancellationToken ct = default);
}
