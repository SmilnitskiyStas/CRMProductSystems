using System.Text.RegularExpressions;
using ShelfGuard.Application.Common;
using ShelfGuard.Application.Features.ConsumerProfile;
using ShelfGuard.Application.Features.ConsumerProfile.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Customers;

public sealed partial class CustomerService : ICustomerService
{
    // TASK-618: how many of a customer's most recent PurchaseReviews the detail view shows.
    private const int RecentReviewsTake = 5;

    private readonly ICustomerRepository _repo;
    private readonly ILoyaltyRepository _loyaltyRepo;
    private readonly IConsumerSupportTicketRepository _supportRepo;
    private readonly IPurchaseReviewRepository _reviewRepo;
    private readonly IConsumerProfileService _consumerProfile;

    public CustomerService(
        ICustomerRepository repo,
        ILoyaltyRepository loyaltyRepo,
        IConsumerSupportTicketRepository supportRepo,
        IPurchaseReviewRepository reviewRepo,
        IConsumerProfileService consumerProfile)
    {
        _repo = repo;
        _loyaltyRepo = loyaltyRepo;
        _supportRepo = supportRepo;
        _reviewRepo = reviewRepo;
        _consumerProfile = consumerProfile;
    }

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
        if (customer is null)
            return null;

        // Three independent reads beyond the customer itself — acceptable for a single-customer
        // detail page (not a list), same reasoning CustomerTransactionDto's own 20-row cap
        // already accepts. None depend on each other's result, so no reason to serialize them,
        // but repositories here share the request's AppDbContext (not thread-safe for concurrent
        // use), so they run sequentially rather than via Task.WhenAll.
        var membership = await _loyaltyRepo.GetMembershipByCustomerIdAsync(id, tenantId, ct);
        var (tierName, compositeScore, tierProgressPercent) = await ResolveTierProgressAsync(membership, tenantId, ct);
        var openTicketCount = await _supportRepo.CountOpenByCustomerIdAsync(id, tenantId, ct);
        var recentReviews = await _reviewRepo.GetRecentForCustomerAsync(id, tenantId, RecentReviewsTake, ct) ?? [];

        return ToDetailDto(customer, tierName, compositeScore, tierProgressPercent, openTicketCount, recentReviews);
    }

    /// <summary>
    /// TASK-618. Null membership (never joined loyalty) → all three null. A membership with no
    /// <see cref="LoyaltyMembership.CurrentTierId"/> yet (not recomputed, or hasn't cleared even
    /// the lowest tier's threshold) → CompositeScore still reported (it's a real, always-present
    /// number on the entity), but CurrentTierName/TierProgressPercent stay null — "no tier
    /// assigned yet" per the task brief. Progress is reported relative to the next tier's
    /// MinCompositeScore (null when already at the top tier).
    /// </summary>
    private async Task<(string? TierName, decimal? CompositeScore, decimal? ProgressPercent)> ResolveTierProgressAsync(
        LoyaltyMembership? membership, Guid tenantId, CancellationToken ct)
    {
        if (membership is null)
            return (null, null, null);

        if (membership.CurrentTierId is null || membership.CurrentTier is null)
            return (null, membership.CompositeScore, null);

        var ladder = await _loyaltyRepo.GetTierLadderAsync(tenantId, ct);
        var nextTier = ladder
            .Where(t => t.SortOrder > membership.CurrentTier.SortOrder)
            .OrderBy(t => t.SortOrder)
            .FirstOrDefault();

        decimal? progressPercent = nextTier switch
        {
            null => null,
            { MinCompositeScore: <= 0 } => 100m,
            _ => Math.Clamp(membership.CompositeScore / nextTier.MinCompositeScore * 100m, 0m, 100m),
        };

        return (membership.CurrentTier.Name, membership.CompositeScore, progressPercent);
    }

    /// <summary>
    /// TASK-621b. Resolves the customer's linked <see cref="LoyaltyMembership"/> (if any) and
    /// delegates to <see cref="IConsumerProfileService.GetProfileChangeHistoryAsync"/> for its
    /// <c>ConsumerAccountId</c>. Every <see cref="LoyaltyMembership"/> row carries a required
    /// (non-nullable) <c>ConsumerAccountId</c>, so the only "no data" case here is no membership
    /// at all — a customer who never joined this tenant's loyalty program has no consumer-side
    /// profile to show history for, which returns an empty page rather than propagating whatever
    /// error <see cref="IConsumerProfileService"/> would otherwise surface (e.g. a deactivated
    /// consumer account) — same "no membership = no data, not a failure" convention as the rest
    /// of the TASK-618 detail-view fields.
    /// </summary>
    public async Task<PagedResult<ConsumerProfileChangeDto>> GetProfileChangeHistoryAsync(
        Guid customerId, Guid tenantId, int page, int pageSize, CancellationToken ct = default)
    {
        var clampedPage = Math.Max(1, page);
        var clampedPageSize = Math.Clamp(pageSize, 1, 200);
        var empty = new PagedResult<ConsumerProfileChangeDto>
        {
            Items = [],
            TotalCount = 0,
            Page = clampedPage,
            PageSize = clampedPageSize,
        };

        var membership = await _loyaltyRepo.GetMembershipByCustomerIdAsync(customerId, tenantId, ct);
        if (membership is null)
            return empty;

        var (history, _, _) = await _consumerProfile.GetProfileChangeHistoryAsync(
            membership.ConsumerAccountId, clampedPage, clampedPageSize, ct);

        return history ?? empty;
    }

    public async Task<(CustomerDto? Customer, string? Error)> CreateAsync(
        Guid tenantId, CreateCustomerDto dto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return (null, "Customer name is required.");

        var contactError = ValidateContactInfo(dto.Phone, dto.Email);
        if (contactError is not null)
            return (null, contactError);

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

        var contactError = ValidateContactInfo(dto.Phone, dto.Email);
        if (contactError is not null)
            return (null, contactError);

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

    // ── validation ────────────────────────────────────────────────────────────
    // Previously CreateAsync/UpdateAsync only checked Name non-empty + phone uniqueness — no
    // format check on Phone/Email at all, so any string ("abc", "123") was silently accepted.
    // Found in TASK-360 (Block 9 audit). Deliberately permissive on Phone (accepts spaces,
    // dashes, parens, leading "+", 7-20 chars) since customers may be entered with various
    // regional formats — this is a sanity check, not a strict E.164 validator.

    private static string? ValidateContactInfo(string? phone, string? email)
    {
        if (!string.IsNullOrWhiteSpace(phone) && !PhoneRegex().IsMatch(phone.Trim()))
            return $"Phone '{phone.Trim()}' is not a valid phone number.";

        if (!string.IsNullOrWhiteSpace(email) && !EmailRegex().IsMatch(email.Trim()))
            return $"Email '{email.Trim()}' is not a valid email address.";

        return null;
    }

    [GeneratedRegex(@"^\+?[\d\s\-()]{7,20}$")]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailRegex();

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

    private static CustomerDetailDto ToDetailDto(
        Customer c,
        string? tierName,
        decimal? compositeScore,
        decimal? tierProgressPercent,
        int openTicketCount,
        List<PurchaseReview> recentReviews) => new(
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
            .ToList(),
        tierName,
        compositeScore,
        tierProgressPercent,
        openTicketCount,
        recentReviews
            .Select(r => new CustomerReviewSummaryDto(r.Rating, r.Comment, r.CreatedAt, r.ReplyText))
            .ToList()
    );
}
