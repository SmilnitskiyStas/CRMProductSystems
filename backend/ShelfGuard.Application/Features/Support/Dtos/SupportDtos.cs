namespace ShelfGuard.Application.Features.Support.Dtos;

public record SupportMessageDto(
    Guid            Id,
    Guid            SenderId,
    bool            IsProviderMessage,
    string          Body,
    DateTimeOffset  CreatedAt
);

public record SupportTicketDto(
    Guid                      Id,
    Guid                      TenantId,
    Guid                      CreatedBy,
    Guid?                     AssignedTo,
    string                    Title,
    string                    Status,
    string                    Priority,
    DateTimeOffset            CreatedAt,
    DateTimeOffset            UpdatedAt,
    bool                      IsUnread,
    IReadOnlyList<SupportMessageDto> Messages
);

public record CreateTicketRequest(
    string Title,
    string Body,
    string Priority  = "medium",
    string Category  = "general",
    Guid?  LocationId = null
);

public record AddMessageRequest(string Body);

public record UpdateTicketStatusRequest(string Status);

public record AssignTicketRequest(Guid AgentId);
