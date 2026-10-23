using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Application.Services;

public interface IMfaPolicy
{
    Task<bool> IsRequiredAsync(User user, CancellationToken ct = default);
}
