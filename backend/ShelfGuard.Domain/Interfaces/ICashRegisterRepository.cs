using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

/// <summary>
/// Cash registers + operator sessions (cash_registers, register_sessions). RLS-scoped: the caller
/// must have the tenant context set (authenticated request, or ITenantSessionOverride for the
/// device-token endpoints that run before any user JWT exists).
/// </summary>
public sealed record PinFailureOutcome(int WindowCount, int DailyCount, string Status);

public interface ICashRegisterRepository
{
    Task<CashRegister?> GetByIdAsync(Guid id, CancellationToken ct = default);
    /// <summary>Register holding this (hashed) pending activation code within the current tenant scope, or null.</summary>
    Task<CashRegister?> GetByActivationCodeHashAsync(string codeHash, CancellationToken ct = default);
    Task<IReadOnlyList<CashRegister>> ListAsync(Guid? storeId, CancellationToken ct = default);
    Task<int> GetNextNumberAsync(Guid tenantId, CancellationToken ct = default);
    Task<bool> NumberExistsAsync(Guid tenantId, int number, CancellationToken ct = default);
    Task AddAsync(CashRegister register, CancellationToken ct = default);

    /// <summary>
    /// Atomic single-use activation: in ONE conditional UPDATE sets Status=active, stores the
    /// device-token hash and clears the code, only while the code hash still matches and has not
    /// expired. Returns false when it lost the race / code was used / expired / register revoked.
    /// </summary>
    Task<bool> TryActivateAsync(
        Guid registerId, string codeHash, string deviceTokenHash, DateTime nowUtc, CancellationToken ct = default);

    /// <summary>
    /// Records one failed sign-in on the register in a single atomic UPDATE ... RETURNING (no
    /// read-modify-write race). Window count decays after <paramref name="window"/>; reaching
    /// <paramref name="maxDaily"/> failures in a UTC day flips Status to 'locked'.
    /// </summary>
    Task<PinFailureOutcome> RegisterPinFailureAsync(
        Guid registerId, DateTime nowUtc, TimeSpan window, int maxDaily, CancellationToken ct = default);

    Task AddSessionAsync(RegisterSession session, CancellationToken ct = default);
    Task<RegisterSession?> GetSessionAsync(Guid sessionId, CancellationToken ct = default);
    Task<IReadOnlyList<RegisterSession>> GetOpenSessionsAsync(Guid registerId, CancellationToken ct = default);

    /// <summary>Active users of the store that have a POS PIN set (PIN uniqueness / PIN-only sign-in candidates).</summary>
    Task<IReadOnlyList<User>> GetPinUsersByStoreAsync(Guid storeId, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
