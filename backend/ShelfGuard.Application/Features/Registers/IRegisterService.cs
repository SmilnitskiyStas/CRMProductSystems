namespace ShelfGuard.Application.Features.Registers;

/// <summary>
/// Cash register lifecycle + operator PIN sessions (cashier app).
/// Manager methods run under an authenticated tenant context; <see cref="ActivateAsync"/> and
/// <see cref="StartSessionAsync"/> are device-token endpoints with no user JWT yet and open their
/// own tenant-scoped session. All return (result, error, statusCode) like PosService.
/// </summary>
public interface IRegisterService
{
    /// <summary>store_manager sees only their own store, network_manager only assigned stores.</summary>
    Task<IReadOnlyList<RegisterDto>> ListAsync(RegisterCaller caller, Guid? storeId, CancellationToken ct = default);

    Task<(RegisterCreatedDto? Result, string? Error, int? StatusCode)> CreateAsync(
        RegisterCaller caller, CreateRegisterRequest request, CancellationToken ct = default);

    Task<(RegisterDto? Result, string? Error, int? StatusCode)> UpdateAsync(
        RegisterCaller caller, Guid registerId, UpdateRegisterRequest request, CancellationToken ct = default);

    /// <summary>Issues a fresh single-use activation code (24 h). Re-activating an active register replaces its device token.</summary>
    Task<(ActivationCodeDto? Result, string? Error, int? StatusCode)> IssueActivationCodeAsync(
        RegisterCaller caller, Guid registerId, CancellationToken ct = default);

    /// <summary>Revokes the device token + pending code and ends all open operator sessions.</summary>
    Task<(bool Ok, string? Error, int? StatusCode)> RevokeAsync(
        RegisterCaller caller, Guid registerId, CancellationToken ct = default);

    /// <summary>Manager unlock after the register hit the daily PIN-failure cap.</summary>
    Task<(bool Ok, string? Error, int? StatusCode)> UnlockAsync(
        RegisterCaller caller, Guid registerId, CancellationToken ct = default);

    /// <summary>Anonymous: single-use activation code -> device token.</summary>
    Task<(ActivateRegisterResponse? Result, string? Error, int? StatusCode)> ActivateAsync(
        ActivateRegisterRequest request, CancellationToken ct = default);

    /// <summary>Anonymous (device token header): PIN -> operator JWT.</summary>
    Task<(RegisterSessionResponse? Result, string? Error, int? StatusCode)> StartSessionAsync(
        string? deviceToken, StartSessionRequest request, CancellationToken ct = default);

    /// <summary>Operator sign-out (explicit or idle).</summary>
    Task<(bool Ok, string? Error, int? StatusCode)> EndSessionAsync(
        Guid sessionId, Guid registerId, string? reason, CancellationToken ct = default);

    /// <summary>
    /// Manager (re)sets an employee's POS PIN: the server generates it (unique within the store,
    /// collisions retried silently) and returns it once. Target must hold a POS role, not outrank
    /// the caller and be in a store the caller manages.
    /// </summary>
    Task<(SetPinResult? Result, string? Error, int? StatusCode)> SetPinAsync(
        RegisterCaller caller, Guid userId, CancellationToken ct = default);

    Task<(bool Ok, string? Error, int? StatusCode)> ClearPinAsync(
        RegisterCaller caller, Guid userId, CancellationToken ct = default);
}

public interface IPosSyncService
{
    /// <summary>Delta catalog for register apps (barcodes, prices, active flag), paged by (UpdatedAt, Id).</summary>
    Task<SyncCatalogResponse> GetCatalogAsync(
        DateTime? since, Guid? afterId, int? limit, CancellationToken ct = default);

    /// <summary>
    /// Published promotion rules applying to a store (the register's store for operator tokens,
    /// otherwise <paramref name="fallbackStoreId"/>). Null error = success.
    /// </summary>
    Task<(SyncPromotionsResponse? Result, string? Error, int? StatusCode)> GetPromotionsAsync(
        Guid tenantId, Guid? registerId, Guid? fallbackStoreId, string? etag, CancellationToken ct = default);
}
