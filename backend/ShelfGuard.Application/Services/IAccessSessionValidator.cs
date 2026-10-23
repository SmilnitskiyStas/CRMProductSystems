using System.Security.Claims;

namespace ShelfGuard.Application.Services;

public interface IAccessSessionValidator
{
    Task<bool> IsValidAsync(ClaimsPrincipal principal, CancellationToken ct = default);
}
