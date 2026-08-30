using ShelfGuard.Application.Common;
using ShelfGuard.Application.Features.Stock;
using ShelfGuard.Application.Features.Transfers.Dtos;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Transfers;

public sealed class TransferService : ITransferService
{
    /// <summary>
    /// Roles that must have a <c>user_locations</c> grant for the transfer's <c>ToStoreId</c>
    /// before they may confirm it (TASK-580). Mirrors ADR-022's restricted ranks, narrowed to
    /// the subset that can even reach this action — Infrastructure's <c>AppPolicies
    /// .CanReceiveStockRoles</c> already excludes merchandiser/cashier/staff from
    /// this controller entirely, and provider/enterprise_admin bypass store-scoping
    /// unconditionally per ADR-022, so only these three remain. Defined here from Domain's
    /// AppRoles (not Infrastructure's AppPolicies) to keep the Application → Infrastructure
    /// dependency direction clean — same rationale as <c>LocationService.StoreScopedRoles</c>.
    /// </summary>
    private static readonly IReadOnlySet<string> StoreScopedConfirmRoles = new HashSet<string>
    {
        AppRoles.NetworkManager,
        AppRoles.StoreManager,
        AppRoles.Storekeeper,
    };

    private readonly ITransferRepository _repo;
    private readonly IUserLocationRepository _userLocations;

    public TransferService(ITransferRepository repo, IUserLocationRepository userLocations)
    {
        _repo = repo;
        _userLocations = userLocations;
    }

    public async Task<List<TransferDto>> GetAllAsync(Guid? storeId, string? status, CancellationToken ct = default)
    {
        var transfers = await _repo.GetAllAsync(storeId, status, ct);
        return transfers.Select(ToDto).ToList();
    }

    public async Task<PagedResult<TransferDto>> GetPagedAsync(
        Guid? storeId, string? status, string? search, string? sortBy, bool? sortDescending,
        int page, int pageSize,
        Guid? categoryId = null, int? minItems = null, int? maxItems = null,
        CancellationToken ct = default)
    {
        var (transfers, total) = await _repo.GetPagedAsync(
            storeId, status, search, sortBy, sortDescending, page, pageSize,
            categoryId, minItems, maxItems, ct);
        return new PagedResult<TransferDto>
        {
            Items = transfers.Select(ToDto).ToList(),
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
        };
    }

    public async Task<TransferDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var transfer = await _repo.GetByIdAsync(id, ct);
        return transfer is null ? null : ToDto(transfer);
    }

    public async Task<(TransferDto? Transfer, string? Error)> CreateAsync(
        Guid tenantId, Guid initiatedBy, CreateTransferRequest request, CancellationToken ct = default)
    {
        if (request.Items is null || request.Items.Count == 0)
            return (null, "Transfer must contain at least one item.");

        if (request.FromStoreId == request.ToStoreId)
            return (null, "Source and destination stores must be different.");

        if (!IsValidTransferType(request.TransferType))
            return (null, $"Invalid transfer type '{request.TransferType}'. Valid: store_to_store, cs_to_store, store_to_production.");

        var transfer = new StockTransfer
        {
            TenantId = tenantId,
            FromStoreId = request.FromStoreId,
            ToStoreId = request.ToStoreId,
            TransferType = request.TransferType,
            Notes = request.Notes,
            Status = "in_transit",
            InitiatedBy = initiatedBy,
        };

        // Validate and deduct source stock for each item
        foreach (var itemReq in request.Items)
        {
            if (itemReq.Quantity <= 0)
                return (null, "All item quantities must be greater than 0.");

            var sourceStock = await _repo.GetStockByIdAsync(itemReq.ProductStockId, ct);
            if (sourceStock is null)
                return (null, $"Stock batch {itemReq.ProductStockId} not found.");

            if (sourceStock.StoreId != request.FromStoreId)
                return (null, $"Stock batch {itemReq.ProductStockId} does not belong to the source store.");

            if (sourceStock.Quantity < itemReq.Quantity)
                return (null, $"Insufficient quantity in batch {itemReq.ProductStockId}. Available: {sourceStock.Quantity}, requested: {itemReq.Quantity}.");

            // Deduct from source immediately (stock is in transit)
            sourceStock.Quantity -= itemReq.Quantity;
            sourceStock.Status = StockStatus.Compute(sourceStock.Quantity, sourceStock.ExpiryDate, sourceStock.LastCheckedAt);
            _repo.UpdateStock(sourceStock);

            // expiry_date and batch_number copied as-is (spec rule: never change on transfer)
            transfer.Items.Add(new StockTransferItem
            {
                TransferId = transfer.Id,
                ProductStockId = itemReq.ProductStockId,
                ProductId = sourceStock.ProductId,
                Quantity = itemReq.Quantity,
                ExpiryDate = sourceStock.ExpiryDate,
                BatchNumber = sourceStock.BatchNumber,
            });

            // Log outbound movement
            await _repo.AddMovementAsync(new StockMovement
            {
                TenantId = tenantId,
                MovementType = "transfer",
                ProductStockId = sourceStock.Id,
                ProductId = sourceStock.ProductId,
                FromStoreId = request.FromStoreId,
                ToStoreId = request.ToStoreId,
                Quantity = itemReq.Quantity,
                QuantityBefore = sourceStock.Quantity + itemReq.Quantity,
                QuantityAfter = sourceStock.Quantity,
                ReferenceId = transfer.Id,
                ReferenceType = "stock_transfer",
                PerformedBy = initiatedBy,
            }, ct);
        }

        await _repo.AddAsync(transfer, ct);
        await _repo.SaveChangesAsync(ct);

        var saved = await _repo.GetByIdAsync(transfer.Id, ct);
        return (saved is null ? null : ToDto(saved), null);
    }

    public async Task<(TransferDto? Transfer, string? Error)> ConfirmAsync(
        Guid id, Guid confirmedBy, Guid tenantId, string? role, CancellationToken ct = default)
    {
        var transfer = await _repo.GetByIdAsync(id, ct);
        if (transfer is null)
            return (null, "Transfer not found.");

        if (transfer.Status == "received")
            return (null, "Transfer is already received.");

        if (transfer.Status == "cancelled")
            return (null, "Cannot confirm a cancelled transfer.");

        // TASK-580: fail-closed store-membership check. Unlike LocationService.GetAllAsync's
        // transitional fail-open list filter, this guards an actual stock-mutating action, so a
        // scoped-role user with zero (or non-matching) user_locations rows for the destination
        // store is rejected outright — the coverage-gap report against prod confirmed zero
        // active scoped-role users are currently unassigned, so there is no legacy population
        // this newly breaks. Only provider/enterprise_admin bypass unconditionally (matching
        // every other ADR-022 bypass site); a null/missing role claim is deliberately NOT
        // treated as a bypass — it falls through into the check (and is rejected, since it has
        // no matching user_locations row) rather than skipping it. In practice the JWT always
        // carries a role claim for anything that passed [Authorize], so this null path is a
        // defensive fallback, not a normal route.
        if (role is null || StoreScopedConfirmRoles.Contains(role))
        {
            var assignedIds = await _userLocations.GetLocationIdsForUserAsync(tenantId, confirmedBy, ct);
            if (!assignedIds.Contains(transfer.ToStoreId))
                return (null, "You do not have access to confirm transfers for this store.");
        }

        // Create new ProductStock batches at destination store
        foreach (var item in transfer.Items)
        {
            var destStock = new ProductStock
            {
                TenantId = transfer.TenantId,
                ProductId = item.ProductId,
                StoreId = transfer.ToStoreId,
                BatchNumber = item.BatchNumber,    // copied as-is
                Quantity = item.Quantity,
                QuantityInitial = item.Quantity,
                ExpiryDate = item.ExpiryDate,      // copied as-is
                Status = StockStatus.Compute(item.Quantity, item.ExpiryDate, DateTime.UtcNow),
                SourceType = "transfer",
                SourceId = transfer.Id,
                AddedBy = confirmedBy,
            };

            await _repo.AddStockAsync(destStock, ct);

            await _repo.AddMovementAsync(new StockMovement
            {
                TenantId = transfer.TenantId,
                MovementType = "transfer",
                ProductStockId = destStock.Id,
                ProductId = item.ProductId,
                FromStoreId = transfer.FromStoreId,
                ToStoreId = transfer.ToStoreId,
                Quantity = item.Quantity,
                QuantityBefore = 0,
                QuantityAfter = item.Quantity,
                ReferenceId = transfer.Id,
                ReferenceType = "stock_transfer",
                PerformedBy = confirmedBy,
            }, ct);
        }

        transfer.Status = "received";
        transfer.ConfirmedBy = confirmedBy;
        _repo.Update(transfer);
        await _repo.SaveChangesAsync(ct);

        var saved = await _repo.GetByIdAsync(id, ct);
        return (saved is null ? null : ToDto(saved), null);
    }

    public async Task<(TransferDto? Transfer, string? Error)> CancelAsync(
        Guid id, CancellationToken ct = default)
    {
        var transfer = await _repo.GetByIdAsync(id, ct);
        if (transfer is null)
            return (null, "Transfer not found.");

        if (transfer.Status == "received")
            return (null, "Cannot cancel a received transfer.");

        if (transfer.Status == "cancelled")
            return (null, "Transfer is already cancelled.");

        // Restore deducted source stock
        foreach (var item in transfer.Items)
        {
            var sourceStock = await _repo.GetStockByIdAsync(item.ProductStockId, ct);
            if (sourceStock is not null)
            {
                sourceStock.Quantity += item.Quantity;
                sourceStock.Status = StockStatus.Compute(sourceStock.Quantity, sourceStock.ExpiryDate, sourceStock.LastCheckedAt);
                _repo.UpdateStock(sourceStock);
            }
        }

        transfer.Status = "cancelled";
        _repo.Update(transfer);
        await _repo.SaveChangesAsync(ct);

        var saved = await _repo.GetByIdAsync(id, ct);
        return (saved is null ? null : ToDto(saved), null);
    }

    // ── mapping ────────────────────────────────────────────────────────────

    private static TransferDto ToDto(StockTransfer t) => new(
        t.Id,
        t.FromStoreId,
        t.FromStore?.Name ?? "—",
        t.ToStoreId,
        t.ToStore?.Name ?? "—",
        t.TransferType,
        t.Status,
        t.Notes,
        t.CreatedAt,
        t.Items.Select(i => new TransferItemDto(
            i.Id, i.ProductStockId, i.ProductId,
            i.Product?.Name ?? "—", i.Quantity, i.ExpiryDate, i.BatchNumber
        )).ToList()
    );

    private static bool IsValidTransferType(string? type) =>
        type is null or "store_to_store" or "cs_to_store" or "store_to_production";
}
