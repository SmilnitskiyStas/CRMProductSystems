using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

public interface IConsumerSessionStore
{
    Task<bool> IssueAsync(ConsumerAccount account, ConsumerRefreshToken token, CancellationToken ct = default);
    Task<ConsumerRefreshToken?> FindAsync(string tokenHash, CancellationToken ct = default);
    Task<bool> RotateAsync(ConsumerRefreshToken existing, ConsumerRefreshToken next, CancellationToken ct = default);
    Task RevokeAsync(Guid accountId, Guid? sessionId, CancellationToken ct = default);
}
