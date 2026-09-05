using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

/// <summary>
/// Buyer→supplier-employee ratings (TASK-695, Phase 8). All buyer-side reads/writes here run on
/// the buyer's own RLS session; <c>supplier_employee_reviews.tenant_isolation</c> is keyed on
/// <c>ClientTenantId</c> so the buyer only ever sees / writes its own ratings, and the explicit
/// <c>ClientTenantId</c> predicate below is defence-in-depth.
/// </summary>
public sealed class SupplierEmployeeReviewRepository : ISupplierEmployeeReviewRepository
{
    private readonly AppDbContext _db;

    public SupplierEmployeeReviewRepository(AppDbContext db) => _db = db;

    public Task<SupplierEmployeeReview?> GetByOrderAsync(
        Guid clientTenantId, Guid orderId, CancellationToken ct = default) =>
        _db.SupplierEmployeeReviews
            .FirstOrDefaultAsync(
                r => r.ClientTenantId == clientTenantId && r.OrderId == orderId && r.Source == "order", ct);

    public Task<SupplierEmployeeReview?> GetByChatParticipantAsync(
        Guid clientTenantId, Guid chatSessionId, Guid supplierUserId, CancellationToken ct = default) =>
        _db.SupplierEmployeeReviews
            .FirstOrDefaultAsync(
                r => r.ClientTenantId == clientTenantId
                  && r.ChatSessionId == chatSessionId
                  && r.SupplierUserId == supplierUserId
                  && r.Source == "chat", ct);

    public async Task<IReadOnlyList<SupplierEmployeeReview>> ListByChatSessionForClientAsync(
        Guid clientTenantId, Guid chatSessionId, CancellationToken ct = default) =>
        await _db.SupplierEmployeeReviews.AsNoTracking()
            .Where(r => r.ClientTenantId == clientTenantId
                     && r.ChatSessionId == chatSessionId
                     && r.Source == "chat")
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

    public async Task AddAsync(SupplierEmployeeReview review, CancellationToken ct = default) =>
        await _db.SupplierEmployeeReviews.AddAsync(review, ct);

    public void Update(SupplierEmployeeReview review) =>
        _db.SupplierEmployeeReviews.Update(review);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
