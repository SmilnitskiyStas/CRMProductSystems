using ShelfGuard.Application.Features.Marketplace.Dtos;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Marketplace;

/// <inheritdoc />
public sealed class SupplierEmployeeReviewService : ISupplierEmployeeReviewService
{
    public const string RatingOutOfRangeError   = "Оцінка має бути від 1 до 5.";
    public const string OrderNotFoundError      = "Замовлення не знайдено.";
    public const string OrderNotDeliveredError  = "Оцінити менеджера можна лише після доставки замовлення.";
    public const string NoResponsibleManagerError = "У замовлення немає відповідального менеджера для оцінки.";
    public const string SupplierNotFoundError   = "Постачальника не знайдено.";
    public const string ChatNotFoundError       = "З цим постачальником ще немає листування.";
    public const string ParticipantNotInChatError = "Цей співробітник не брав участі в листуванні.";

    private const string ManagerNameFallback = "Менеджер";

    private readonly ISupplierEmployeeReviewRepository _reviews;
    private readonly IMarketplaceOrderRepository _orders;
    private readonly IMarketplaceRepository _marketplace;
    private readonly ISupplierChatRepository _chat;
    private readonly IUserRepository _users;

    public SupplierEmployeeReviewService(
        ISupplierEmployeeReviewRepository reviews,
        IMarketplaceOrderRepository orders,
        IMarketplaceRepository marketplace,
        ISupplierChatRepository chat,
        IUserRepository users)
    {
        _reviews     = reviews;
        _orders      = orders;
        _marketplace = marketplace;
        _chat        = chat;
        _users       = users;
    }

    public async Task<(SupplierEmployeeReviewDto? Review, string? Error)> RateOrderManagerAsync(
        Guid clientTenantId, Guid orderId, RateSupplierEmployeeDto request, Guid ratedByUserId,
        CancellationToken ct = default)
    {
        if (request.Rating is < 1 or > 5)
            return (null, RatingOutOfRangeError);

        var order = await _orders.GetByIdAsync(orderId, ct);
        if (order is null || order.ClientTenantId != clientTenantId)
            return (null, OrderNotFoundError);

        if (order.Status != MarketplaceOrderStatus.Delivered)
            return (null, OrderNotDeliveredError);

        if (order.ConfirmedByUserId is null)
            return (null, NoResponsibleManagerError);

        var existing = await _reviews.GetByOrderAsync(clientTenantId, orderId, ct);
        var review = await UpsertAsync(
            existing,
            supplierTenantId: order.SupplierTenantId,
            clientTenantId:   clientTenantId,
            supplierUserId:   order.ConfirmedByUserId.Value,
            supplierUserName: order.ConfirmedByUserName ?? ManagerNameFallback,
            ratedByUserId:    ratedByUserId,
            rating:           request.Rating,
            comment:          request.Comment,
            source:           "order",
            orderId:          orderId,
            chatSessionId:    null,
            ct);

        return (ToDto(review), null);
    }

    public async Task<(SupplierEmployeeReviewDto? Review, string? Error)> RateChatParticipantAsync(
        Guid clientTenantId, Guid supplierId, RateChatParticipantDto request, Guid ratedByUserId,
        CancellationToken ct = default)
    {
        if (request.Rating is < 1 or > 5)
            return (null, RatingOutOfRangeError);

        var supplierTenantId = await _marketplace.GetSupplierTenantIdAsync(supplierId, ct);
        if (supplierTenantId is null)
            return (null, SupplierNotFoundError);

        var session = await _chat.GetSessionAsync(supplierTenantId.Value, clientTenantId, ct);
        if (session is null)
            return (null, ChatNotFoundError);

        // The ONLY validation that request.SupplierUserId is a real supplier actor: the buyer
        // can't be trusted to pass an arbitrary user id, so it must be someone who actually sent
        // a message in this thread FROM the supplier side. The name is snapshotted from there too.
        var messages = await _chat.GetMessagesAsync(session.Id, ct);
        var participantMessage = messages.FirstOrDefault(
            m => m.SenderUserId == request.SupplierUserId && m.SenderTenantId == supplierTenantId.Value);
        if (participantMessage is null)
            return (null, ParticipantNotInChatError);

        var existing = await _reviews.GetByChatParticipantAsync(
            clientTenantId, session.Id, request.SupplierUserId, ct);
        var review = await UpsertAsync(
            existing,
            supplierTenantId: supplierTenantId.Value,
            clientTenantId:   clientTenantId,
            supplierUserId:   request.SupplierUserId,
            supplierUserName: string.IsNullOrWhiteSpace(participantMessage.SenderName)
                ? ManagerNameFallback
                : participantMessage.SenderName,
            ratedByUserId:    ratedByUserId,
            rating:           request.Rating,
            comment:          request.Comment,
            source:           "chat",
            orderId:          null,
            chatSessionId:    session.Id,
            ct);

        return (ToDto(review), null);
    }

    public async Task<SupplierEmployeeReviewDto?> GetOrderManagerRatingAsync(
        Guid clientTenantId, Guid orderId, CancellationToken ct = default)
    {
        var review = await _reviews.GetByOrderAsync(clientTenantId, orderId, ct);
        return review is null ? null : ToDto(review);
    }

    public async Task<IReadOnlyList<SupplierEmployeeReviewDto>> GetMyChatParticipantRatingsAsync(
        Guid clientTenantId, Guid supplierId, CancellationToken ct = default)
    {
        var supplierTenantId = await _marketplace.GetSupplierTenantIdAsync(supplierId, ct);
        if (supplierTenantId is null)
            return Array.Empty<SupplierEmployeeReviewDto>();

        var session = await _chat.GetSessionAsync(supplierTenantId.Value, clientTenantId, ct);
        if (session is null)
            return Array.Empty<SupplierEmployeeReviewDto>();

        var rows = await _reviews.ListByChatSessionForClientAsync(clientTenantId, session.Id, ct);
        return rows.Select(ToDto).ToList();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<SupplierEmployeeReview> UpsertAsync(
        SupplierEmployeeReview? existing,
        Guid supplierTenantId, Guid clientTenantId, Guid supplierUserId, string supplierUserName,
        Guid ratedByUserId, int rating, string? comment, string source,
        Guid? orderId, Guid? chatSessionId, CancellationToken ct)
    {
        var ratedByName = (await _users.GetByIdAsync(ratedByUserId, ct))?.FullName;
        var trimmedComment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();

        if (existing is not null)
        {
            existing.Rating           = (short)rating;
            existing.Comment          = trimmedComment;
            existing.SupplierUserName = supplierUserName;
            existing.RatedByUserId    = ratedByUserId;
            existing.RatedByName      = ratedByName;
            existing.UpdatedAt        = DateTimeOffset.UtcNow;
            _reviews.Update(existing);
            await _reviews.SaveChangesAsync(ct);
            return existing;
        }

        var review = new SupplierEmployeeReview
        {
            SupplierTenantId = supplierTenantId,
            ClientTenantId   = clientTenantId,
            SupplierUserId   = supplierUserId,
            SupplierUserName = supplierUserName,
            RatedByUserId    = ratedByUserId,
            RatedByName      = ratedByName,
            Rating           = (short)rating,
            Comment          = trimmedComment,
            Source           = source,
            OrderId          = orderId,
            ChatSessionId    = chatSessionId,
        };
        await _reviews.AddAsync(review, ct);
        await _reviews.SaveChangesAsync(ct);
        return review;
    }

    private static SupplierEmployeeReviewDto ToDto(SupplierEmployeeReview r) =>
        new(r.Id, r.SupplierUserId, r.SupplierUserName, r.Rating, r.Comment,
            r.Source, r.OrderId, r.ChatSessionId, r.CreatedAt, r.UpdatedAt);
}
