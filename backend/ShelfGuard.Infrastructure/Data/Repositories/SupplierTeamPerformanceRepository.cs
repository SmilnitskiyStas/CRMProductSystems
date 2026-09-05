using Microsoft.EntityFrameworkCore;
using ShelfGuard.Application.Features.SupplierAnalytics;
using ShelfGuard.Domain.Constants;

namespace ShelfGuard.Infrastructure.Data.Repositories;

/// <summary>
/// Supplier team performance (TASK-695, Phase 8). Runs on the supplier's own RLS session — see
/// <see cref="ISupplierTeamPerformanceRepository"/> for the per-table policy notes. Explicit
/// <c>SupplierTenantId</c> predicates below are defence-in-depth on top of RLS.
/// </summary>
public sealed class SupplierTeamPerformanceRepository : ISupplierTeamPerformanceRepository
{
    private readonly AppDbContext _db;

    public SupplierTeamPerformanceRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<TeamPerfOrderRow>> GetOrdersSinceAsync(
        Guid supplierTenantId, DateTimeOffset since, CancellationToken ct = default) =>
        await _db.MarketplaceOrders.AsNoTracking()
            .Where(o => o.SupplierTenantId == supplierTenantId
                     && o.Status != MarketplaceOrderStatus.Cancelled
                     && (o.CreatedAt >= since
                       || o.ConfirmedAt >= since
                       || o.ShippedAt >= since
                       || o.DeliveredAt >= since))
            .Select(o => new TeamPerfOrderRow(
                o.Id,
                o.ConfirmedByUserId,
                o.ShippedByUserId,
                o.CreatedAt,
                o.ConfirmedAt,
                o.ShippedAt,
                o.DeliveredAt,
                o.ExpectedDeliveryDate))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<TeamPerfReceiptRow>> GetFinalizedReceiptFlagsAsync(
        Guid supplierTenantId, IReadOnlyCollection<Guid> orderIds, CancellationToken ct = default)
    {
        if (orderIds.Count == 0)
            return Array.Empty<TeamPerfReceiptRow>();

        var ids = orderIds as Guid[] ?? orderIds.ToArray();

        return await _db.MarketplaceOrderReceipts.AsNoTracking()
            .Where(r => r.SupplierTenantId == supplierTenantId
                     && r.Status == "received"
                     && ids.Contains(r.MarketplaceOrderId))
            .Select(r => new TeamPerfReceiptRow(
                r.MarketplaceOrderId,
                r.Items.Any(i => i.DiscrepancyNotes != null && i.DiscrepancyNotes.Trim() != "")))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<TeamPerfChatMessageRow>> GetChatMessagesSinceAsync(
        Guid supplierTenantId, DateTimeOffset since, CancellationToken ct = default) =>
        await (
            from m in _db.SupplierChatMessages.AsNoTracking()
            join s in _db.SupplierChatSessions.AsNoTracking() on m.SessionId equals s.Id
            where s.SupplierTenantId == supplierTenantId && m.CreatedAt >= since
            select new TeamPerfChatMessageRow(m.SessionId, m.SenderUserId, m.SenderTenantId, m.CreatedAt))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<TeamPerfReviewRow>> GetEmployeeReviewsSinceAsync(
        Guid supplierTenantId, DateTimeOffset since, CancellationToken ct = default) =>
        await _db.SupplierEmployeeReviews.AsNoTracking()
            .Where(r => r.SupplierTenantId == supplierTenantId && r.CreatedAt >= since)
            .Select(r => new TeamPerfReviewRow(r.SupplierUserId, r.Rating, r.CreatedAt))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<TeamPerfReviewDetailRow>> GetEmployeeReviewDetailsAsync(
        Guid supplierTenantId, Guid supplierUserId, CancellationToken ct = default) =>
        await _db.SupplierEmployeeReviews.AsNoTracking()
            .Where(r => r.SupplierTenantId == supplierTenantId && r.SupplierUserId == supplierUserId)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new TeamPerfReviewDetailRow(
                r.Id,
                r.SupplierUserId,
                r.SupplierUserName,
                r.Rating,
                r.Comment,
                r.Source,
                r.OrderId,
                r.ChatSessionId,
                r.RatedByName,
                r.CreatedAt))
            .ToListAsync(ct);
}
