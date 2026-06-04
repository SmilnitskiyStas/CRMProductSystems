namespace ShelfGuard.Application.Services;

public interface IJwtService
{
    string GenerateAccessToken(Guid userId, string email, string role, Guid? tenantId, Guid? storeId);
    (string RawToken, string TokenHash) GenerateRefreshToken();
    string HashToken(string token);
}
