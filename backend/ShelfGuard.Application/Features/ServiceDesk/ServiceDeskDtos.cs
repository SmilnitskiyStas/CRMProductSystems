namespace ShelfGuard.Application.Features.ServiceDesk;

public record TicketDto(
    Guid Id,
    int Number,
    string Title,
    string Description,
    string Category,
    string Priority,
    string Status,
    Guid CreatedBy,
    string CreatedByName,
    Guid? AssignedTo,
    string? AssignedToName,
    Guid? LocationId,
    string? LocationName,
    DateTimeOffset? ResolvedAt,
    DateTimeOffset CreatedAt,
    int CommentCount);

public record TicketDetailDto(
    Guid Id,
    int Number,
    string Title,
    string Description,
    string Category,
    string Priority,
    string Status,
    Guid CreatedBy,
    string CreatedByName,
    Guid? AssignedTo,
    string? AssignedToName,
    Guid? LocationId,
    string? LocationName,
    DateTimeOffset? ResolvedAt,
    DateTimeOffset CreatedAt,
    List<TicketCommentDto> Comments);

public record TicketCommentDto(
    Guid Id,
    Guid AuthorId,
    string AuthorName,
    string Body,
    bool IsInternal,
    DateTimeOffset CreatedAt);

public record CreateTicketDto(
    string Title,
    string Description,
    string Category = "general",
    string Priority = "medium",
    Guid? LocationId = null);

public record UpdateTicketDto(
    string? Title,
    string? Description,
    string? Category,
    string? Priority,
    string? Status,
    Guid? AssignedTo);

public record AddCommentDto(
    string Body,
    bool IsInternal = false);
