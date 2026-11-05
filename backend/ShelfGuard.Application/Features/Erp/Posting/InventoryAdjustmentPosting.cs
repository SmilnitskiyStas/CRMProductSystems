using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ShelfGuard.Application.Services;

namespace ShelfGuard.Application.Features.Erp.Posting;

/// <summary>Absolute count, with a required optimistic precondition. Tenant/actor come from live access context.</summary>
public sealed record InventoryAdjustmentRequest(Guid RequestId, Guid LocationId, Guid BatchId,
    decimal ExpectedQuantity, decimal Quantity, string Reason);

public sealed record InventoryAdjustmentPlan(Guid TenantId, Guid ActorId, InventoryAdjustmentRequest Request,
    string RequestHash)
{
    public string IdempotencyKey => $"inventory.adjustment.v1:{Request.RequestId:N}";
}

public sealed record InventoryAdjustmentSnapshot(Guid ItemId, string Unit, string ItemName,
    string? BatchNumber, DateOnly ExpiryDate, Guid? ZoneId, DateTime OccurredAt);

/// <summary>Receipt for the original posting, including on replay; quantities are not a current-stock read.</summary>
public sealed record InventoryAdjustmentReceipt(Guid TenantId, Guid RequestId, Guid DocumentId,
    Guid PostingId, Guid EntryId, Guid EventId, Guid BatchId,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] decimal QuantityBefore,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] decimal QuantityAfter, DateTime PostedAt, bool Replayed);

public interface IInventoryAdjustmentPosting
{
    Task<InventoryAdjustmentReceipt> AdjustAsync(InventoryAdjustmentRequest request, CancellationToken ct = default);
}

public interface IInventoryAdjustmentStore
{
    Task<InventoryAdjustmentReceipt> PostAsync(InventoryAdjustmentPlan plan, CancellationToken ct = default);
}

public sealed record InventoryPostingScope(Guid TenantId, Guid LocationId);
public interface IInventoryPostingGate { bool Allows(Guid tenantId, Guid locationId); }

/// <summary>Immutable explicit scope allowlist. No wildcard or default-on behavior.</summary>
public sealed class InventoryPostingGate : IInventoryPostingGate
{
    private readonly bool _enabled;
    private readonly HashSet<InventoryPostingScope> _scopes;

    public InventoryPostingGate(bool enabled, IEnumerable<InventoryPostingScope> scopes)
    {
        _enabled = enabled;
        _scopes = scopes.ToHashSet();
        if (_scopes.Any(s => s.TenantId == Guid.Empty || s.LocationId == Guid.Empty)
            || enabled && _scopes.Count == 0)
            throw new ArgumentException("Inventory posting requires explicit nonempty tenant/location scopes.");
    }

    public bool Allows(Guid tenantId, Guid locationId) => _enabled
        && _scopes.Contains(new(tenantId, locationId));
}

public sealed class InventoryPostingDisabledException() : Exception("ERP inventory posting is disabled for this scope.");
public sealed class InventoryPostingConflictException(string message) : Exception(message);
public sealed class InventoryPostingNotFoundException() : Exception("Stock batch is not visible in this scope.");

public static class InventoryAdjustmentPlanner
{
    public const string Version = "inventory.adjustment.v1";
    // The compatibility stock column is numeric(10,2); never round silently to ledger's wider precision.
    public const decimal MaximumQuantity = 99_999_999.99m;

    public static bool CanAdjust(string role) => role is "enterprise_admin" or "network_manager" or "store_manager";

    public static InventoryAdjustmentPlan Prepare(Guid tenantId, Guid actorId, InventoryAdjustmentRequest request)
    {
        if (tenantId == Guid.Empty || actorId == Guid.Empty || request.RequestId == Guid.Empty
            || request.LocationId == Guid.Empty || request.BatchId == Guid.Empty)
            throw new ArgumentException("Explicit request, tenant, actor, location and batch identities are required.");
        if (!ValidQuantity(request.ExpectedQuantity) || !ValidQuantity(request.Quantity))
            throw new ArgumentException("Quantities must be nonnegative numeric(10,2) values.");
        if (request.ExpectedQuantity == request.Quantity)
            throw new ArgumentException("A stock adjustment must change the quantity.");
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500
            || request.Reason.Any(char.IsControl))
            throw new ArgumentException("An adjustment reason of 1–500 characters without control characters is required.");
        request = request with { Reason = request.Reason.Trim() };
        // Explicit positional contract and fixed-scale decimals make retries independent of process culture/decimal scale.
        var hash = Hash(new { protocol = Version, tenantId, actorId, request.RequestId, request.LocationId,
            request.BatchId, expected = Number(request.ExpectedQuantity), target = Number(request.Quantity), request.Reason });
        return new(tenantId, actorId, request, hash);
    }

    public static string ContentHash(InventoryAdjustmentPlan plan, InventoryAdjustmentSnapshot snapshot) =>
        Hash(new { protocol = "inventory.adjustment.content.v1", plan.RequestHash, snapshot.ItemId, snapshot.Unit,
            snapshot.ItemName, snapshot.BatchNumber, snapshot.ExpiryDate, snapshot.ZoneId, snapshot.OccurredAt });

    private static bool ValidQuantity(decimal quantity) => quantity >= 0 && quantity <= MaximumQuantity
        && decimal.Round(quantity, 2) == quantity;
    private static string Number(decimal quantity) => quantity.ToString("0.00", CultureInfo.InvariantCulture);
    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)))).ToLowerInvariant();
}

/// <summary>W4-A core only. Existing stock API/worker adapters are not routed here yet.</summary>
public sealed class InventoryAdjustmentPostingService(IWorkAccessContext access, IInventoryPostingGate gate,
    IInventoryAdjustmentStore store) : IInventoryAdjustmentPosting
{
    public async Task<InventoryAdjustmentReceipt> AdjustAsync(InventoryAdjustmentRequest request, CancellationToken ct = default)
    {
        var scope = await access.GetAsync(ct) ?? throw new WorkAccessDeniedException();
        if (!InventoryAdjustmentPlanner.CanAdjust(scope.Role) || !scope.CanLocation(request.LocationId))
            throw new WorkAccessDeniedException();
        if (!gate.Allows(scope.TenantId, request.LocationId)) throw new InventoryPostingDisabledException();
        var plan = InventoryAdjustmentPlanner.Prepare(scope.TenantId, scope.UserId, request);
        return await store.PostAsync(plan, ct);
    }
}
