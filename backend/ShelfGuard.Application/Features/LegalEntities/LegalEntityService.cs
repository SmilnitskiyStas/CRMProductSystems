using System.Text.RegularExpressions;
using ShelfGuard.Application.Features.LegalEntities.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.LegalEntities;

public sealed partial class LegalEntityService : ILegalEntityService
{
    private readonly ILegalEntityRepository _repo;

    public LegalEntityService(ILegalEntityRepository repo) => _repo = repo;

    public async Task<List<LegalEntityDto>> GetAllAsync(Guid tenantId, CancellationToken ct = default)
    {
        var entities = await _repo.GetAllByTenantAsync(tenantId, ct);
        return entities.Select(ToDto).ToList();
    }

    public async Task<(LegalEntityDto? LegalEntity, string? Error)> GetByIdAsync(
        Guid tenantId, Guid id, CancellationToken ct = default)
    {
        var entity = await _repo.GetByIdAsync(id, ct);
        if (entity is null || entity.TenantId != tenantId)
            return (null, "Legal entity not found.");

        return (ToDto(entity), null);
    }

    public async Task<bool> BelongsToTenantAsync(Guid tenantId, Guid legalEntityId, CancellationToken ct = default)
    {
        var entity = await _repo.GetByIdAsync(legalEntityId, ct);
        return entity is not null && entity.TenantId == tenantId;
    }

    public async Task<(LegalEntityDto? LegalEntity, string? Error)> CreateAsync(
        Guid tenantId, CreateLegalEntityRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.LegalName))
            return (null, "Назва юридичної особи обов'язкова.");

        var edrpouError = ValidateEdrpou(request.Edrpou);
        if (edrpouError is not null)
            return (null, edrpouError);

        var entity = new LegalEntity
        {
            TenantId = tenantId,
            LegalName = request.LegalName.Trim(),
            Edrpou = Normalize(request.Edrpou),
            LegalAddress = Normalize(request.LegalAddress),
            DirectorName = Normalize(request.DirectorName),
            Phone = Normalize(request.Phone),
            Email = Normalize(request.Email),
            Iban = Normalize(request.Iban),
            BankName = Normalize(request.BankName),
            IsVatPayer = request.IsVatPayer,
        };

        await _repo.AddAsync(entity, ct);
        await _repo.SaveChangesAsync(ct);

        return (ToDto(entity), null);
    }

    public async Task<(LegalEntityDto? LegalEntity, string? Error)> UpdateAsync(
        Guid tenantId, Guid id, UpdateLegalEntityRequest request, CancellationToken ct = default)
    {
        var entity = await _repo.GetByIdAsync(id, ct);
        if (entity is null || entity.TenantId != tenantId)
            return (null, "Legal entity not found.");

        if (string.IsNullOrWhiteSpace(request.LegalName))
            return (null, "Назва юридичної особи обов'язкова.");

        var edrpouError = ValidateEdrpou(request.Edrpou);
        if (edrpouError is not null)
            return (null, edrpouError);

        entity.LegalName = request.LegalName.Trim();
        entity.Edrpou = Normalize(request.Edrpou);
        entity.LegalAddress = Normalize(request.LegalAddress);
        entity.DirectorName = Normalize(request.DirectorName);
        entity.Phone = Normalize(request.Phone);
        entity.Email = Normalize(request.Email);
        entity.Iban = Normalize(request.Iban);
        entity.BankName = Normalize(request.BankName);
        entity.IsVatPayer = request.IsVatPayer;
        entity.IsActive = request.IsActive;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        _repo.Update(entity);
        await _repo.SaveChangesAsync(ct);

        return (ToDto(entity), null);
    }

    public async Task<string?> DeactivateAsync(Guid tenantId, Guid id, CancellationToken ct = default)
    {
        var entity = await _repo.GetByIdAsync(id, ct);
        if (entity is null || entity.TenantId != tenantId)
            return "Legal entity not found.";

        entity.IsActive = false;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        _repo.Update(entity);

        await _repo.SaveChangesAsync(ct);
        return null;
    }

    // ── validation ───────────────────────────────────────────────────────────

    /// <summary>
    /// ЄДРПОУ must be null/empty, or exactly 8 digits (legal entity) or 10 digits (ФОП/ІПН).
    /// </summary>
    private static string? ValidateEdrpou(string? edrpou)
    {
        if (string.IsNullOrWhiteSpace(edrpou))
            return null;

        var trimmed = edrpou.Trim();
        if (!EdrpouRegex().IsMatch(trimmed))
            return "Невірний формат ЄДРПОУ: очікується 8 або 10 цифр.";

        return null;
    }

    [GeneratedRegex(@"^\d{8}(\d{2})?$")]
    private static partial Regex EdrpouRegex();

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // ── mapping ────────────────────────────────────────────────────────────

    private static LegalEntityDto ToDto(LegalEntity e) => new(
        e.Id,
        e.LegalName,
        e.Edrpou,
        e.LegalAddress,
        e.DirectorName,
        e.Phone,
        e.Email,
        e.Iban,
        e.BankName,
        e.IsVatPayer,
        e.IsActive,
        e.CreatedAt,
        e.UpdatedAt
    );
}
