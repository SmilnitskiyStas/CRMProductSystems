using ShelfGuard.Application.Common;
using ShelfGuard.Application.Features.Stock;
using ShelfGuard.Application.Features.WriteOffs.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.WriteOffs;

public sealed class WriteOffService : IWriteOffService
{
    private static readonly HashSet<string> ValidReasons =
        ["expired", "damaged", "theft", "production_loss", "other"];

    private readonly IWriteOffRepository _repo;

    public WriteOffService(IWriteOffRepository repo) => _repo = repo;

    public async Task<List<WriteOffDto>> GetAllAsync(Guid? storeId, string? status, CancellationToken ct = default)
    {
        var writeOffs = await _repo.GetAllAsync(storeId, status, ct);
        return writeOffs.Select(ToDto).ToList();
    }

    public async Task<PagedResult<WriteOffDto>> GetPagedAsync(
        Guid? storeId, string? status, int page, int pageSize,
        CancellationToken ct = default)
    {
        var (writeOffs, total) = await _repo.GetPagedAsync(storeId, status, page, pageSize, ct);
        return new PagedResult<WriteOffDto>
        {
            Items = writeOffs.Select(ToDto).ToList(),
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
        };
    }

    public async Task<WriteOffDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var writeOff = await _repo.GetByIdAsync(id, ct);
        return writeOff is null ? null : ToDto(writeOff);
    }

    public async Task<(WriteOffDto? WriteOff, string? Error)> CreateAsync(
        Guid tenantId, Guid createdBy, CreateWriteOffRequest request, CancellationToken ct = default)
    {
        if (request.Items is null || request.Items.Count == 0)
            return (null, "Write-off must contain at least one item.");

        if (request.Reason is not null && !ValidReasons.Contains(request.Reason))
            return (null, $"Invalid reason '{request.Reason}'. Valid: {string.Join(", ", ValidReasons)}.");

        foreach (var item in request.Items)
        {
            if (item.Quantity <= 0)
                return (null, "All item quantities must be greater than 0.");
        }

        var writeOff = new WriteOff
        {
            TenantId = tenantId,
            StoreId = request.StoreId,
            Reason = request.Reason,
            Status = "pending_approval",
            CreatedBy = createdBy,
        };

        foreach (var itemReq in request.Items)
        {
            var lossAmount = itemReq.UnitPrice.HasValue
                ? itemReq.UnitPrice.Value * itemReq.Quantity
                : (decimal?)null;

            writeOff.Items.Add(new WriteOffItem
            {
                WriteOffId = writeOff.Id,
                ProductStockId = itemReq.ProductStockId,
                ProductId = itemReq.ProductId,
                Quantity = itemReq.Quantity,
                UnitPrice = itemReq.UnitPrice,
                LossAmount = lossAmount,
            });
        }

        writeOff.TotalLossAmount = writeOff.Items
            .Where(i => i.LossAmount.HasValue)
            .Sum(i => i.LossAmount!.Value);

        await _repo.AddAsync(writeOff, ct);
        await _repo.SaveChangesAsync(ct);

        var saved = await _repo.GetByIdAsync(writeOff.Id, ct);
        return (saved is null ? null : ToDto(saved), null);
    }

    public async Task<(WriteOffDto? WriteOff, string? Error)> ApproveAsync(
        Guid id, Guid approvedBy, CancellationToken ct = default)
    {
        var writeOff = await _repo.GetByIdAsync(id, ct);
        if (writeOff is null)
            return (null, "Write-off not found.");

        if (writeOff.Status == "approved")
            return (null, "Write-off is already approved.");

        if (writeOff.Status == "rejected")
            return (null, "Cannot approve a rejected write-off.");

        // Deduct stock and log movements for items with a stock reference
        foreach (var item in writeOff.Items)
        {
            if (item.ProductStockId is null) continue;

            var stock = await _repo.GetStockByIdAsync(item.ProductStockId.Value, ct);
            if (stock is null) continue;

            var before = stock.Quantity;
            var deduct = Math.Min(item.Quantity, stock.Quantity);

            stock.Quantity -= deduct;
            stock.Status = StockStatus.Compute(stock.Quantity, stock.ExpiryDate, stock.LastCheckedAt);
            _repo.UpdateStock(stock);

            await _repo.AddMovementAsync(new StockMovement
            {
                TenantId = writeOff.TenantId,
                MovementType = "write_off",
                ProductStockId = stock.Id,
                ProductId = item.ProductId,
                FromStoreId = writeOff.StoreId,
                Quantity = deduct,
                QuantityBefore = before,
                QuantityAfter = stock.Quantity,
                UnitPrice = item.UnitPrice,
                TotalAmount = item.LossAmount,
                ReferenceId = writeOff.Id,
                ReferenceType = "write_off",
                PerformedBy = approvedBy,
            }, ct);
        }

        writeOff.Status = "approved";
        writeOff.ApprovedBy = approvedBy;
        writeOff.ApprovedAt = DateTime.UtcNow;

        _repo.Update(writeOff);
        await _repo.SaveChangesAsync(ct);

        var saved = await _repo.GetByIdAsync(id, ct);
        return (saved is null ? null : ToDto(saved), null);
    }

    public async Task<(WriteOffDto? WriteOff, string? Error)> RejectAsync(
        Guid id, CancellationToken ct = default)
    {
        var writeOff = await _repo.GetByIdAsync(id, ct);
        if (writeOff is null)
            return (null, "Write-off not found.");

        if (writeOff.Status == "approved")
            return (null, "Cannot reject an approved write-off.");

        if (writeOff.Status == "rejected")
            return (null, "Write-off is already rejected.");

        writeOff.Status = "rejected";
        _repo.Update(writeOff);
        await _repo.SaveChangesAsync(ct);

        var saved = await _repo.GetByIdAsync(id, ct);
        return (saved is null ? null : ToDto(saved), null);
    }

    // ── mapping ────────────────────────────────────────────────────────────

    private static WriteOffDto ToDto(WriteOff w) => new(
        w.Id,
        w.StoreId,
        w.Store?.Name ?? "—",
        w.Status,
        w.Reason,
        w.TotalLossAmount,
        w.PdfUrl,
        w.CreatedAt,
        w.ApprovedAt,
        w.Items.Select(i => new WriteOffItemDto(
            i.Id,
            i.ProductStockId,
            i.ProductId,
            i.Product?.Name ?? "—",
            i.ProductStock?.BatchNumber,
            i.ProductStock?.ExpiryDate,
            i.Quantity,
            i.UnitPrice,
            i.LossAmount
        )).ToList()
    );
}
