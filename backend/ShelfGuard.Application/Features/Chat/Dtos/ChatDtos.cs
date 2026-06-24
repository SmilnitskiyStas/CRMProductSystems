namespace ShelfGuard.Application.Features.Chat.Dtos;

public record ChatSessionDto(
    Guid Id,
    Guid TenantId,
    string TenantName,
    string Subject,
    string Status,
    int UnreadCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ClosedAt,
    int? Rating,
    string? RatingComment,
    string? AssignedAgentName,
    ChatMessageDto? LastMessage
);

public record ChatMessageDto(
    Guid Id,
    Guid SessionId,
    Guid SenderUserId,
    string SenderName,
    string Body,
    bool IsRead,
    bool IsSystem,
    DateTimeOffset CreatedAt
);

public record CreateChatSessionRequest(string Subject, string FirstMessage);
public record SendChatMessageRequest(string Body);
public record SubmitRatingRequest(int Rating, string? Comment);
