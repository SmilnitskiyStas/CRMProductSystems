namespace ShelfGuard.Application.Features.ConsumerAuth.Dtos;

/// <summary>Phone is normalized server-side (PhoneNormalizer) — any common UA format is accepted.</summary>
public sealed record ConsumerRegisterRequest(
    string Phone,
    string Password,
    string FullName,
    string? Email = null);

public sealed record ConsumerLoginRequest(
    string Phone,
    string Password);

public sealed record ConsumerAuthResponse(
    string AccessToken,
    Guid ConsumerAccountId,
    string FullName,
    string Phone);
