namespace ShelfGuard.Application.Features.ServiceDesk;

public record ProviderTicketListItemDto(
    Guid Id,
    int Number,
    Guid TenantId,
    string TenantName,
    string Title,
    string Description,
    string Category,
    string Priority,
    string Status,
    Guid CreatedBy,
    string CreatedByName,
    bool CreatedByProvider,
    DateTimeOffset CreatedAt,
    int CommentCount);

public record CreateProviderTicketDto(
    Guid TargetTenantId,
    string Title,
    string Description,
    string Category = "general",
    string Priority = "medium");
