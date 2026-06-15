namespace ShelfGuard.Application.Features.Support.Dtos;

public record SupportMessageDto(
    Guid     Id,
    Guid     SenderId,
    bool     IsProviderMessage,
    string   Body,
    DateTime CreatedAt
);

public record SupportTicketDto(
    Guid                      Id,
    Guid                      TenantId,
    Guid                      CreatedBy,
    Guid?                     AssignedTo,
    string                    Subject,
    string                    Status,
    string                    Priority,
    DateTime                  CreatedAt,
    DateTime                  UpdatedAt,
    bool                      IsUnread,
    IReadOnlyList<SupportMessageDto> Messages
);

public record CreateTicketRequest(
    string Subject,
    string Body,
    string Priority = "normal"
);

public record AddMessageRequest(string Body);

public record UpdateTicketStatusRequest(string Status);

public record AssignTicketRequest(Guid AgentId);
