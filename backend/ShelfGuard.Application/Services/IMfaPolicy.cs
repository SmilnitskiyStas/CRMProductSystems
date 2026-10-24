using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Application.Services;

public interface IMfaPolicy
{
    /// <summary>Whether enabled MFA must be retained for current privileges. Does not gate initial enrollment or ordinary login.</summary>
    Task<bool> IsRequiredAsync(User user, CancellationToken ct = default);
}
