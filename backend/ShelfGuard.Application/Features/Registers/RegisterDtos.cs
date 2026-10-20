using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Application.Features.Registers;

public sealed record RegisterDto(
    Guid Id,
    Guid StoreId,
    string? StoreName,
    int Number,
    string Type,
    string Name,
    string Status,
    DateTime? LastSeenAt,
    string? AppVersion,
    DateTime? ActivatedAt,
    bool HasPendingActivationCode,
    DateTime? ActivationCodeExpiresAt,
    DateTime CreatedAt)
{
    public static RegisterDto From(CashRegister r) => new(
        r.Id, r.StoreId, r.Store?.Name, r.Number, r.Type, r.Name, r.Status, r.LastSeenAt, r.AppVersion,
        r.ActivatedAt,
        r.ActivationCodeHash != null && r.ActivationCodeExpiresAt > DateTime.UtcNow,
        r.ActivationCodeExpiresAt, r.CreatedAt);
}

/// <param name="Number">Optional; auto-assigned (max + 1 per tenant) when omitted.</param>
/// <param name="Type">"line" (default) or "self_checkout".</param>
public sealed record CreateRegisterRequest(Guid StoreId, string Name, int? Number = null, string? Type = null);

public sealed record UpdateRegisterRequest(string? Name = null, Guid? StoreId = null);

/// <summary>The plaintext activation code is returned exactly once, here.</summary>
public sealed record ActivationCodeDto(string ActivationCode, DateTime ExpiresAt);

public sealed record RegisterCreatedDto(RegisterDto Register, string ActivationCode, DateTime ActivationCodeExpiresAt);

public sealed record ActivateRegisterRequest(string ActivationCode, string? AppVersion = null);

/// <summary>DeviceToken is returned exactly once; only its hash is stored server-side.</summary>
public sealed record ActivateRegisterResponse(
    string DeviceToken,
    Guid RegisterId,
    int Number,
    string Name,
    string Type,
    Guid StoreId,
    string? StoreName);

/// <param name="Pin">4-6 digits.</param>
/// <param name="CashierId">Optional (badge / pick-from-list sign-in). When omitted the PIN alone identifies the operator.</param>
public sealed record StartSessionRequest(string Pin, Guid? CashierId = null, string? AppVersion = null);

public sealed record RegisterSessionResponse(
    string AccessToken,
    DateTime ExpiresAt,
    Guid SessionId,
    Guid RegisterId,
    Guid StoreId,
    Guid CashierId,
    string CashierName);

/// <param name="Reason">logout (default) or idle.</param>
public sealed record EndSessionRequest(string? Reason = null);

/// <summary>The caller of a manager endpoint (from JWT claims) - drives store scoping.</summary>
public sealed record RegisterCaller(Guid TenantId, Guid UserId, string Role, Guid? StoreId);

/// <summary>Server-generated POS PIN. Returned exactly once; only its bcrypt hash is stored.</summary>
public sealed record SetPinResult(string Pin);

// ── Delta catalog sync ──────────────────────────────────────────────────────

public sealed record SyncCatalogItemDto(
    Guid Id,
    string Name,
    IReadOnlyList<string> Barcodes,
    decimal? PriceRetail,
    decimal VatRate,
    string Unit,
    bool IsActive,
    DateTime UpdatedAt,
    // Target of category-wide promotion rules. Added after the first cashier release: rows synced
    // earlier lack it until they change, so the cashier must run one full resync (since=null).
    Guid? CategoryId = null);

/// <summary>
/// Page of changed items. Call again with since=NextSince&amp;afterId=NextAfterId while HasMore.
/// Persist NextSince/NextAfterId of the last page as the next sync cursor. Clients should resume
/// from a cursor moved back by a small overlap (e.g. 2 minutes) and upsert by id: UpdatedAt is
/// stamped by the DB at statement time, so a long-running writer transaction can commit a row
/// with a timestamp older than a cursor already delivered.
/// </summary>
public sealed record SyncCatalogResponse(
    IReadOnlyList<SyncCatalogItemDto> Items,
    bool HasMore,
    DateTime? NextSince,
    Guid? NextAfterId,
    DateTime ServerTime);

// ── Promotion rules sync ────────────────────────────────────────────────────

/// <param name="Definition">Published version definition JSON, verbatim (promotion-rules.md shape).</param>
/// <param name="CreatedAt">Rule creation time - the engine's "older rule" tie-break.</param>
public sealed record SyncPromotionRuleDto(
    Guid Id,
    Guid VersionId,
    string Name,
    DateTime CreatedAt,
    System.Text.Json.JsonElement Definition);

/// <summary>
/// Published rules for the register's store that are valid now or later. When the client's etag
/// equals <see cref="Etag"/>, <see cref="NotModified"/> is true and <see cref="Rules"/> is empty:
/// keep the local copy.
/// </summary>
public sealed record SyncPromotionsResponse(
    string Etag,
    bool NotModified,
    DateTime ServerTime,
    IReadOnlyList<SyncPromotionRuleDto> Rules,
    string? StoreTimeZone = null);
