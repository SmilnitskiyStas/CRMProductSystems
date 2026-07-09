namespace ShelfGuard.Application.Features.LegalEntities.Dtos;

public sealed record LegalEntityDto(
    Guid Id,
    string LegalName,
    string? Edrpou,
    string? LegalAddress,
    string? DirectorName,
    string? Phone,
    string? Email,
    string? Iban,
    string? BankName,
    bool IsVatPayer,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public sealed record CreateLegalEntityRequest(
    string LegalName,
    string? Edrpou,
    string? LegalAddress,
    string? DirectorName,
    string? Phone,
    string? Email,
    string? Iban,
    string? BankName,
    bool IsVatPayer
);

public sealed record UpdateLegalEntityRequest(
    string LegalName,
    string? Edrpou,
    string? LegalAddress,
    string? DirectorName,
    string? Phone,
    string? Email,
    string? Iban,
    string? BankName,
    bool IsVatPayer,
    bool IsActive
);
