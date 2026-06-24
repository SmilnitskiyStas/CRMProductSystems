using ShelfGuard.Application.Features.Chat.Dtos;

namespace ShelfGuard.Application.Features.Chat;

public interface IChatService
{
    Task<ChatSessionDto> CreateSessionAsync(Guid tenantId, Guid userId, string userName, CreateChatSessionRequest req, CancellationToken ct);
    Task<IReadOnlyList<ChatSessionDto>> GetSessionsAsync(Guid tenantId, CancellationToken ct);
    Task<IReadOnlyList<ChatMessageDto>> GetMessagesAsync(Guid sessionId, Guid tenantId, CancellationToken ct);
    Task<ChatMessageDto> SendMessageAsync(Guid sessionId, Guid tenantId, Guid userId, string userName, SendChatMessageRequest req, CancellationToken ct);
    Task CloseSessionAsync(Guid sessionId, Guid tenantId, CancellationToken ct);
    Task<IReadOnlyList<ChatSessionDto>> GetAllSessionsForProviderAsync(CancellationToken ct);
    Task<IReadOnlyList<ChatMessageDto>> GetMessagesForProviderAsync(Guid sessionId, CancellationToken ct);
    Task<ChatMessageDto> ProviderSendMessageAsync(Guid sessionId, Guid providerId, string providerName, SendChatMessageRequest req, CancellationToken ct);
    Task ProviderCloseSessionAsync(Guid sessionId, CancellationToken ct);
    Task<string?> SubmitRatingAsync(Guid sessionId, Guid tenantId, int rating, string? comment, CancellationToken ct);
}
