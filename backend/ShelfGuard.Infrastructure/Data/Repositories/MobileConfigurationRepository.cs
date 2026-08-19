using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Exceptions;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

/// <summary>See <see cref="IMobileConfigurationRepository"/> for the contract.</summary>
public sealed class MobileConfigurationRepository : IMobileConfigurationRepository
{
    private readonly AppDbContext _db;

    public MobileConfigurationRepository(AppDbContext db) => _db = db;

    public Task<MobileConfiguration?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
        _db.MobileConfigurations
            .Include(c => c.DraftVersion)
            .Include(c => c.Theme)
            .FirstOrDefaultAsync(c => c.TenantId == tenantId, ct);

    public Task<MobileConfiguration?> GetPublishedByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
        _db.MobileConfigurations
            .AsNoTracking()
            .Include(c => c.PublishedVersion)
            .FirstOrDefaultAsync(c => c.TenantId == tenantId, ct);

    public async Task AddAsync(MobileConfiguration configuration, CancellationToken ct = default) =>
        await _db.MobileConfigurations.AddAsync(configuration, ct);

    public void Update(MobileConfiguration configuration) => _db.MobileConfigurations.Update(configuration);

    public async Task AddVersionAsync(MobileConfigurationVersion version, CancellationToken ct = default) =>
        await _db.MobileConfigurationVersions.AddAsync(version, ct);

    public void UpdateVersion(MobileConfigurationVersion version) => _db.MobileConfigurationVersions.Update(version);

    public async Task AddThemeAsync(MobileTheme theme, CancellationToken ct = default) =>
        await _db.MobileThemes.AddAsync(theme, ct);

    public void UpdateTheme(MobileTheme theme) => _db.MobileThemes.Update(theme);

    public async Task<int> GetMaxVersionNumberAsync(Guid tenantId, CancellationToken ct = default)
    {
        // Nullable projection avoids MaxAsync's "sequence contains no elements" throw on an
        // empty result for a non-nullable int column — same pattern used elsewhere in this repo.
        var max = await _db.MobileConfigurationVersions
            .Where(v => v.TenantId == tenantId)
            .Select(v => (int?)v.Version)
            .MaxAsync(ct);

        return max ?? 0;
    }

    public Task<MobileConfigurationVersion?> GetVersionByIdAsync(Guid tenantId, Guid versionId, CancellationToken ct = default) =>
        _db.MobileConfigurationVersions.FirstOrDefaultAsync(v => v.TenantId == tenantId && v.Id == versionId, ct);

    public async Task<IReadOnlyList<MobileConfigurationVersion>> GetVersionsForTenantAsync(Guid tenantId, CancellationToken ct = default) =>
        await _db.MobileConfigurationVersions
            .AsNoTracking()
            .Where(v => v.TenantId == tenantId)
            .OrderByDescending(v => v.Version)
            .ToListAsync(ct);

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // MobileConfiguration and MobileConfigurationVersion both carry an xmin concurrency
            // token (TASK-544) — this fires when two writers raced on the same row (e.g. two
            // concurrent PublishAsync calls for the same tenant). Translate to a Domain-level
            // exception so Application services (which must not reference EF Core) can catch it.
            throw new ConcurrencyConflictException(
                "The mobile configuration was modified by another request. Please retry.", ex);
        }
        catch (DbUpdateException ex) when (IsVersionNumberRace(ex))
        {
            // Second line of defense for the same "two concurrent publishes" race the xmin token
            // above guards: MobileConfigPublishService computes its cloned draft's next Version
            // number via GetMaxVersionNumberAsync BEFORE this SaveChangesAsync call, so two
            // publishes that both read that MAX before either committed can both pick the same
            // next number. The pre-existing unique index on (MobileConfigurationId, Version)
            // catches that as a Postgres unique-violation — translate it the same way as an xmin
            // conflict, since from the caller's perspective it is the identical "someone else
            // published first, please retry" situation, not a real data problem.
            throw new ConcurrencyConflictException(
                "The mobile configuration was modified by another request. Please retry.", ex);
        }
    }

    private static bool IsVersionNumberRace(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg &&
        pg.ConstraintName == "uq_mobile_configuration_versions_config_version";
}
