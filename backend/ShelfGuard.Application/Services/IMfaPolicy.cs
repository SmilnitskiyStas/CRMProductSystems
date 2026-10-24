using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Application.Services;

public interface IMfaPolicy
{
    /// <summary>Legacy privilege classification; does not gate voluntary login enrollment or self-service MFA disable.</summary>
    Task<bool> IsRequiredAsync(User user, CancellationToken ct = default);
}
