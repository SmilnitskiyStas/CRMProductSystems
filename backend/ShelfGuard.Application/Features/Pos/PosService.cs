using Microsoft.Extensions.Logging;
using ShelfGuard.Application.Features.Pos.Dtos;
using ShelfGuard.Application.Features.Pos.Fiscal;
using ShelfGuard.Application.Features.Stock;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Exceptions;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Pos;

/// <summary>
/// POS business logic: shift open/close, sale creation with FEFO write-down.
/// ADR-011: sale committed locally first; fiscalization is async, never blocks.
/// ADR-013: IFiscalService resolved per-tenant via IFiscalServiceFactory.
/// </summary>
public sealed class PosService : IPosService
{
    private const int ShiftOpenPollIntervalMs = 2_000;
    private const int ShiftOpenTimeoutMs = 60_000;

    // Bounded synchronous attempt at online fiscalization (TASK-356 fix — see CreateSaleAsync).
    // Short enough to not meaningfully stall a checkout queue if Checkbox is slow/unreachable;
    // long enough to cover the typical happy-path round trip so the printed receipt already
    // carries a real fiscal number in the common case.
    private const int OnlineFiscalizationTimeoutMs = 8_000;

    private readonly IPosRepository _pos;
    private readonly IStockRepository _stock;
    private readonly IItemRepository _catalog;
    private readonly IDiscountRepository _discounts;
    private readonly IFiscalServiceFactory _fiscalFactory;
    private readonly ILoyaltyRepository _loyalty;
    private readonly ICustomerRepository _customers;
    private readonly ILogger<PosService> _log;

    public PosService(
        IPosRepository pos,
        IStockRepository stock,
        IItemRepository catalog,
        IDiscountRepository discounts,
        IFiscalServiceFactory fiscalFactory,
        ILoyaltyRepository loyalty,
        ICustomerRepository customers,
        ILogger<PosService> log)
    {
        _pos = pos;
        _stock = stock;
        _catalog = catalog;
        _discounts = discounts;
        _fiscalFactory = fiscalFactory;
        _loyalty = loyalty;
        _customers = customers;
        _log = log;
    }

    // ── Open shift ─────────────────────────────────────────────────────────

    public async Task<(ShiftDto? Shift, string? Error, int? StatusCode)> OpenShiftAsync(
        Guid tenantId, Guid cashierId, OpenShiftRequest request, CancellationToken ct = default)
    {
        // 409: only one open shift per tenant
        var existing = await _pos.GetOpenShiftAsync(tenantId, ct);
        if (existing is not null)
            return (null, "A shift is already open for this tenant.", 409);

        var shift = new PosShift
        {
            TenantId = tenantId,
            StoreId = request.StoreId,
            CashierId = cashierId,
            OpeningCash = request.OpeningCash,
            OpenedAt = DateTime.UtcNow,
        };

        await _pos.AddShiftAsync(shift, ct);
        await _pos.SaveChangesAsync(ct);

        // Fiscal open (async, non-blocking on failure — offline-first)
        string fiscalStatus = "pending_fiscalization";
        string? providerShiftId = null;

        try
        {
            var fiscal = await _fiscalFactory.GetForTenantAsync(tenantId, ct);
            var openResult = await fiscal.OpenShiftAsync(ct);
            providerShiftId = openResult.ProviderShiftId;

            // Checkbox opens async: poll until OPENED or timeout
            if (openResult.ProviderShiftId is not null)
            {
                var polled = await PollShiftStatusAsync(
                    fiscal, openResult.ProviderShiftId, FiscalShiftStatus.Opened, ct);

                if (polled.Status == FiscalShiftStatus.Opened)
                {
                    shift.FiscalShiftNumber = polled.Serial?.ToString();
                    fiscalStatus = "opened";
                }
                else
                {
                    fiscalStatus = "open_failed";
                }

                providerShiftId = polled.ProviderShiftId ?? providerShiftId;
            }
            else
            {
                // Noop provider — shift is local-only
                fiscalStatus = "local_only";
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Fiscal open failed for tenant {TenantId} — shift stays pending_fiscalization", tenantId);
            fiscalStatus = "open_failed";
        }

        // Persist fiscal result (if any) — shift entity was already saved; update in place.
        await _pos.SaveChangesAsync(ct);

        return (ToShiftDto(shift, fiscalStatus, providerShiftId), null, null);
    }

    // ── Get current shift ──────────────────────────────────────────────────

    public async Task<ShiftDto?> GetCurrentShiftAsync(Guid tenantId, CancellationToken ct = default)
    {
        var shift = await _pos.GetOpenShiftAsync(tenantId, ct);
        return shift is null ? null : ToShiftDto(shift, "open", shift.FiscalShiftNumber);
    }

    // ── Close shift ────────────────────────────────────────────────────────

    public async Task<(ShiftDto? Shift, string? Error, int? StatusCode)> CloseShiftAsync(
        Guid tenantId, CloseShiftRequest? request = null, CancellationToken ct = default)
    {
        var shift = await _pos.GetOpenShiftAsync(tenantId, ct);
        if (shift is null)
            return (null, "No open shift found.", 404);

        // TASK-356: validate before touching the fiscal provider — a bad cash count
        // shouldn't trigger a real Checkbox Z-report call.
        var actualClosingCash = request?.ActualClosingCash;
        if (actualClosingCash is < 0)
            return (null, "ActualClosingCash cannot be negative.", 400);

        string fiscalStatus = "pending_fiscalization";
        string? providerShiftId = shift.FiscalShiftNumber; // may already be set

        try
        {
            var fiscal = await _fiscalFactory.GetForTenantAsync(tenantId, ct);
            var closeResult = await fiscal.CloseShiftAsync(ct);
            providerShiftId = closeResult.ProviderShiftId ?? providerShiftId;

            if (closeResult.ProviderShiftId is not null)
            {
                var polled = await PollShiftStatusAsync(
                    fiscal, closeResult.ProviderShiftId, FiscalShiftStatus.Closed, ct);
                fiscalStatus = polled.Status == FiscalShiftStatus.Closed ? "closed" : "close_failed";
            }
            else
            {
                fiscalStatus = "local_only";
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Fiscal close failed for tenant {TenantId}", tenantId);
            fiscalStatus = "close_failed";
        }

        // TASK-356: cash-drawer reconciliation — independent of the fiscal Z-report
        // outcome above (cash counting doesn't depend on Checkbox being reachable).
        // ExpectedCashAmount only counts CASH-payment sales: card payments never touch
        // the physical drawer, so including them would make every card-heavy shift look
        // like a "shortage".
        decimal? expectedCashAmount = null;
        decimal? cashDiscrepancy = null;

        if (actualClosingCash.HasValue)
        {
            var cashSalesTotal = await _pos.GetCashSalesTotalForShiftAsync(shift.Id, ct);
            expectedCashAmount = (shift.OpeningCash ?? 0m) + cashSalesTotal;
            cashDiscrepancy = actualClosingCash.Value - expectedCashAmount.Value;
            shift.ClosingCash = actualClosingCash.Value;
        }

        shift.ClosedAt = DateTime.UtcNow;
        _pos.UpdateShift(shift);
        await _pos.SaveChangesAsync(ct);

        return (ToShiftDto(shift, fiscalStatus, providerShiftId, expectedCashAmount, cashDiscrepancy), null, null);
    }

    // ── Create sale ────────────────────────────────────────────────────────

    public async Task<(SaleDto? Sale, string? Error, int? StatusCode)> CreateSaleAsync(
        Guid tenantId, Guid cashierId, CreateSaleRequest request,
        CancellationToken ct = default)
    {
        // 1. Validate shift
        var shift = await _pos.GetShiftByIdAsync(request.ShiftId, ct);
        if (shift is null || shift.TenantId != tenantId || shift.ClosedAt.HasValue)
            return (null, "Shift not found or not open.", 409);

        // StoreId resolved from the shift entity (not JWT) — single source of truth.
        var storeId = shift.StoreId;

        if (request.Items is null || request.Items.Count == 0)
            return (null, "Sale must contain at least one item.", 400);

        if (!string.Equals(request.PaymentType, "Cash", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(request.PaymentType, "Card", StringComparison.OrdinalIgnoreCase))
            return (null, "PaymentType must be 'Cash' or 'Card'.", 400);

        // TASK-405 (Loyalty Фаза 0): resolve + validate the optional customer/membership up
        // front, same "fail fast before touching stock" discipline as the checks above.
        // customer/membership are fetched via the shared scoped AppDbContext (PosService,
        // CustomerRepository and LoyaltyRepository all resolve to the same per-request
        // context — see AuthService.IssueTokensAsync's identical reasoning), so mutating
        // their tracked properties further down needs no extra SaveChanges of its own; the
        // single COMMIT below picks everything up together.
        Customer? customer = null;
        if (request.CustomerId.HasValue)
        {
            customer = await _customers.GetByIdAsync(request.CustomerId.Value, tenantId, ct);
            if (customer is null)
                return (null, "Customer not found.", 400);
        }

        LoyaltyMembership? membership = null;
        LoyaltyProgramSettings? loyaltySettings = null;
        if (request.LoyaltyMembershipId.HasValue)
        {
            membership = await _loyalty.GetMembershipByIdAsync(request.LoyaltyMembershipId.Value, tenantId, ct);
            if (membership is null)
                return (null, "Loyalty membership not found.", 400);
            if (membership.Status != LoyaltyMembershipStatus.Active)
                return (null, "Loyalty membership is blocked.", 400);

            // No settings row yet -> proposed defaults (3%/50%/enabled), same fallback as
            // LoyaltyService.GetSettingsAsync, so loyalty works out of the box once the
            // "loyalty" module is on, without forcing a Settings visit first.
            loyaltySettings = await _loyalty.GetSettingsAsync(tenantId, ct)
                ?? new LoyaltyProgramSettings { TenantId = tenantId };
        }
        else if (request.RedeemAmount is > 0m)
        {
            return (null, "LoyaltyMembershipId is required when RedeemAmount is set.", 400);
        }

        // 2. Resolve products + stock
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var resolvedItems = new List<ResolvedSaleItem>();

        foreach (var itemReq in request.Items)
        {
            if (itemReq.Quantity <= 0)
                return (null, $"Quantity must be > 0 for barcode '{itemReq.Barcode}'.", 400);

            // Resolve product by barcode
            var product = await _catalog.GetByBarcodeAsync(itemReq.Barcode, ct);
            if (product is null)
                return (null, $"Product not found for barcode '{itemReq.Barcode}'.", 400);

            // FEFO batches for this product+store
            var batches = await _stock.GetFefoOrderedAsync(product.Id, storeId, ct);

            // 423: if ALL batches (including 0-qty ones) are expired → block
            var allBatches = await _stock.GetAllAsync(null, null, null, product.Id, ct);
            var storeBatches = allBatches.Where(b => b.StoreId == storeId).ToList();
            if (storeBatches.Count > 0 && storeBatches.All(b => b.ExpiryDate <= today))
                return (null, $"Product '{product.Name}' is fully expired and cannot be sold.", 423);

            // Check sufficient stock (from non-expired batches)
            var availableQty = batches.Where(b => b.ExpiryDate > today).Sum(b => b.Quantity);
            if (availableQty < itemReq.Quantity)
                return (null, $"Insufficient stock for '{product.Name}': available {availableQty}, requested {itemReq.Quantity}.", 400);

            // Resolve active discount for this product+store (critical status → auto-discount)
            decimal discountAmount = 0m;
            decimal priceRetail = product.PriceRetail ?? 0m;

            // Check if any batch is critical (apply auto-discount)
            bool hasCriticalBatch = storeBatches.Any(b => b.Status == "critical" && b.Quantity > 0);
            if (hasCriticalBatch)
            {
                // Find active auto-discount for this product
                var activeDiscounts = await _discounts.GetAllAsync(tenantId, storeId, "active", ct);
                var productDiscount = activeDiscounts
                    .Where(d => d.ProductId == product.Id && d.AutoApplied)
                    .OrderByDescending(d => d.DiscountPercent)
                    .FirstOrDefault();

                if (productDiscount is not null && productDiscount.PriceDiscounted.HasValue)
                {
                    discountAmount = priceRetail - productDiscount.PriceDiscounted.Value;
                    discountAmount = Math.Max(0, discountAmount);
                }
            }

            resolvedItems.Add(new ResolvedSaleItem(
                Product: product,
                Batches: batches.Where(b => b.ExpiryDate > today).ToList(),
                Quantity: itemReq.Quantity,
                PriceRetail: priceRetail,
                DiscountAmount: discountAmount));
        }

        // 3. Build transaction (all in ONE EF transaction)
        var receiptNumber = $"R-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}";

        var tx = new PosTransaction
        {
            TenantId = tenantId,
            StoreId = storeId,
            CashierId = cashierId,
            ShiftId = shift.Id,
            ReceiptNumber = receiptNumber,
            PaymentType = request.PaymentType.ToLower(),
            Status = "pending_fiscalization",
            CreatedAt = DateTime.UtcNow,
            CustomerId = customer?.Id,
        };

        var txItemDtos = new List<SaleItemDto>();
        var stockEvents = new List<StockEvent>();

        foreach (var resolved in resolvedItems)
        {
            var priceFinal = Math.Max(0, resolved.PriceRetail - resolved.DiscountAmount);
            decimal remaining = resolved.Quantity;

            // FEFO write-down: consume batches in order (nearest expiry first)
            Guid? primaryBatchId = null;
            foreach (var batch in resolved.Batches)
            {
                if (remaining <= 0) break;

                var consume = Math.Min(remaining, batch.Quantity);
                primaryBatchId ??= batch.Id;

                var before = batch.Quantity;
                batch.Quantity -= consume;
                batch.Status = StockStatus.Compute(batch.Quantity, batch.ExpiryDate, batch.LastCheckedAt);
                _stock.Update(batch);

                stockEvents.Add(new StockEvent
                {
                    TenantId = tenantId,
                    EventType = "pos_sale",
                    ProductStockId = batch.Id,
                    ProductId = resolved.Product.Id,
                    StoreId = storeId,
                    QuantityDelta = -consume,
                    Confidence = 100,
                    Meta = $"{{\"tx_receipt\":\"{receiptNumber}\"}}",
                    PerformedBy = cashierId,
                });

                remaining -= consume;
            }

            tx.Items.Add(new PosTransactionItem
            {
                TransactionId = tx.Id,
                ProductId = resolved.Product.Id,
                ProductStockId = primaryBatchId,
                Quantity = resolved.Quantity,
                PriceRetail = resolved.PriceRetail,
                DiscountAmount = resolved.DiscountAmount,
                PriceFinal = priceFinal,
            });

            txItemDtos.Add(new SaleItemDto(
                ProductId: resolved.Product.Id,
                ProductName: resolved.Product.Name,
                Barcode: resolved.Product.Barcodes.Count > 0 ? resolved.Product.Barcodes[0] : string.Empty,
                Quantity: resolved.Quantity,
                UnitPrice: resolved.PriceRetail,
                DiscountAmount: resolved.DiscountAmount,
                Total: Math.Round(priceFinal * resolved.Quantity, 2)));
        }

        // Set totals
        tx.TotalAmount = txItemDtos.Sum(i => i.Total);

        // TASK-405 (Loyalty Фаза 0): redemption/accrual computed on the NET (post-redemption)
        // amount — matches the plan's "чиста сума, від якої далі рахується нарахування", and
        // means VAT below is also computed on what the customer actually ends up paying.
        // Ledger entries are staged (AddLedgerEntryAsync doesn't save on its own — mirrors
        // IPosRepository's Add*Async methods) and only actually persist at the single COMMIT
        // further down — a failed sale can never leave a dangling ledger entry or an
        // out-of-sync membership balance.
        decimal? loyaltyAccrued = null;
        decimal? loyaltyRedeemed = null;

        if (membership is not null && loyaltySettings is { IsEnabled: true })
        {
            if (request.RedeemAmount is > 0m)
            {
                var redeem = request.RedeemAmount.Value;
                var cap = Math.Round(tx.TotalAmount * loyaltySettings.RedemptionCapPercent / 100m, 2);
                if (redeem > cap)
                    return (null, $"Redeem amount exceeds the redemption cap ({loyaltySettings.RedemptionCapPercent:0.##}% of the sale).", 400);
                if (redeem > membership.Balance)
                    return (null, "Insufficient loyalty balance.", 400);
                if (membership.Balance - redeem < loyaltySettings.MinRedemptionBalance)
                    return (null, "Redemption would drop the balance below the minimum allowed.", 400);

                tx.TotalAmount -= redeem;
                membership.Balance -= redeem;
                loyaltyRedeemed = redeem;
                await _loyalty.AddLedgerEntryAsync(new LoyaltyLedgerEntry
                {
                    TenantId = tenantId,
                    MembershipId = membership.Id,
                    EntryType = LoyaltyEntryType.Redemption,
                    Amount = -redeem,
                    BalanceAfter = membership.Balance,
                    PosTransactionId = tx.Id,
                }, ct);
            }

            // Accrual reads tx.TotalAmount AFTER the possible reduction above.
            var accrual = Math.Round(tx.TotalAmount * loyaltySettings.AccrualRatePercent / 100m, 2);
            if (accrual > 0m)
            {
                membership.Balance += accrual;
                loyaltyAccrued = accrual;
                await _loyalty.AddLedgerEntryAsync(new LoyaltyLedgerEntry
                {
                    TenantId = tenantId,
                    MembershipId = membership.Id,
                    EntryType = LoyaltyEntryType.Accrual,
                    Amount = accrual,
                    BalanceAfter = membership.Balance,
                    PosTransactionId = tx.Id,
                }, ct);
            }

            _loyalty.UpdateMembership(membership);
        }

        // Dead-until-now aggregate fields (TASK-405) — start reflecting reality for any sale
        // with a customer attached, independent of whether a loyalty membership is involved.
        if (customer is not null)
        {
            customer.TotalOrders++;
            customer.TotalSpent += tx.TotalAmount;
        }

        tx.TaxAmount = Math.Round(tx.TotalAmount * 0.2m / 1.2m, 2); // Standard VAT 20%

        // Persist entire sale (stock updates + events + tx + items) in one SaveChanges
        await _pos.AddTransactionAsync(tx, ct);
        foreach (var ev in stockEvents)
            await _pos.AddStockEventAsync(ev, ct);

        // Update shift total sales
        shift.TotalSales += tx.TotalAmount;
        _pos.UpdateShift(shift);

        try
        {
            await _pos.SaveChangesAsync(ct); // COMMIT (FEFO write-down + tx + items + stock events + loyalty balance)
        }
        catch (ConcurrencyConflictException ex)
        {
            // Two concurrent sales raced on the same batch's Quantity (e.g. last unit sold
            // twice at the same moment) OR on the same loyalty membership's Balance (e.g. two
            // redemptions against the same membership at once) — both ProductStock (TASK-356)
            // and LoyaltyMembership (TASK-414) use xmin optimistic concurrency, and this one
            // SaveChangesAsync commits both kinds of writes together. Nothing was persisted, so
            // it's safe to just ask the cashier to retry with fresh data rather than silently
            // overselling stock or overspending a loyalty balance.
            _log.LogWarning(ex,
                "Concurrent update conflict (stock or loyalty balance) while creating sale for tenant {TenantId}", tenantId);
            return (null, "Stock or loyalty balance was updated concurrently by another operation. Please retry.", 409);
        }

        // 4. Fiscalization — attempted synchronously, bounded by a short timeout, so a healthy
        // Checkbox response reaches the cashier before the receipt is printed (v3-spec: the
        // fiscal code belongs on the printed receipt). Still never blocks the sale beyond that
        // budget — the DB commit above already succeeded regardless of the fiscal outcome
        // (ADR-011 offline-first); on timeout/failure the transaction simply stays
        // pending_fiscalization and the 5-minute retry job (worker fiscalization-retry.job.ts)
        // picks it up later, safely (Checkbox honors LocalReceiptId as an idempotency key).
        //
        // This used to be a detached `Task.Run(...)` that captured this request's *scoped*
        // _pos repository (backed by the request's DbContext) and, transitively via
        // _fiscalFactory, a fresh DbContext whose RLS tenant context is resolved from
        // IHttpContextAccessor.HttpContext (TenantConnectionInterceptor). Because the task
        // was never awaited, it kept running after the HTTP response completed and the
        // request's DI scope was disposed — a near-guaranteed ObjectDisposedException on
        // _pos.SaveChangesAsync (silently swallowed), and, worse, an unreliable read of a
        // pooled/possibly-already-reused HttpContext for RLS purposes. In practice this meant
        // sales were fiscalized only by the retry job, never inline, with no diagnostics
        // pointing at why. Running the attempt inline keeps everything inside the one valid,
        // correctly-scoped request.
        using var fiscalCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        fiscalCts.CancelAfter(OnlineFiscalizationTimeoutMs);

        try
        {
            var fiscal = await _fiscalFactory.GetForTenantAsync(tenantId, fiscalCts.Token);
            var fiscalRequest = new FiscalReceiptRequest(
                Items: resolvedItems.Select(r => new FiscalReceiptItem(
                    Code: r.Product.Id.ToString(),
                    Name: r.Product.Name,
                    UnitPrice: r.PriceRetail - r.DiscountAmount,
                    Quantity: r.Quantity,
                    Barcode: r.Product.Barcodes.Count > 0 ? r.Product.Barcodes[0] : null)).ToList(),
                Payments: [new FiscalPayment(
                    string.Equals(request.PaymentType, "Card", StringComparison.OrdinalIgnoreCase)
                        ? FiscalPaymentKind.Cashless : FiscalPaymentKind.Cash,
                    request.PaymentAmount)],
                LocalReceiptId: tx.Id.ToString());

            var receipt = await fiscal.CreateReceiptAsync(fiscalRequest, fiscalCts.Token);

            tx.FiscalNumber = receipt.FiscalNumber;
            tx.Status = receipt.Status == FiscalReceiptStatus.Done
                ? "fiscalized"
                : "pending_fiscalization";
            _pos.UpdateTransaction(tx);
            await _pos.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Fiscal receipt failed/timed out for tx {TxId} — stays pending_fiscalization", tx.Id);
        }

        var change = Math.Max(0, request.PaymentAmount - tx.TotalAmount);

        var dto = new SaleDto(
            TransactionId: tx.Id,
            ShiftId: shift.Id,
            Items: txItemDtos,
            Subtotal: tx.TotalAmount,
            PaymentType: request.PaymentType,
            PaymentAmount: request.PaymentAmount,
            Change: change,
            FiscalStatus: tx.Status,
            FiscalNumber: tx.FiscalNumber,
            ReceiptNumber: tx.ReceiptNumber,
            CreatedAt: tx.CreatedAt,
            LoyaltyAccrued: loyaltyAccrued,
            LoyaltyRedeemed: loyaltyRedeemed,
            LoyaltyBalance: membership?.Balance,
            CustomerId: customer?.Id,
            CustomerName: customer?.Name);

        return (dto, null, null);
    }

    // ── Pending fiscalization (retry worker) ───────────────────────────────

    public async Task<IReadOnlyList<PendingFiscalizationDto>> GetPendingFiscalizationAsync(
        int maxRetries = 5,
        int olderThanSeconds = 30,
        CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-olderThanSeconds);
        var txs = await _pos.GetPendingFiscalizationAsync(maxRetries, cutoff, ct);

        return txs.Select(t => new PendingFiscalizationDto(
            Id: t.Id,
            ShiftId: t.ShiftId ?? Guid.Empty,
            TenantId: t.TenantId,
            CreatedAt: t.CreatedAt,
            RetryCount: t.RetryCount,
            Items: t.Items.Select(i => new SaleItemDto(
                ProductId: i.ProductId,
                ProductName: i.Product?.Name ?? "—",
                Barcode: i.Product?.Barcodes.Count > 0 ? i.Product.Barcodes[0] : string.Empty,
                Quantity: i.Quantity,
                UnitPrice: i.PriceRetail,
                DiscountAmount: i.DiscountAmount,
                Total: Math.Round(i.PriceFinal * i.Quantity, 2))).ToList(),
            Total: t.TotalAmount)).ToList();
    }

    // ── Fiscalize single transaction (retry worker) ────────────────────────

    private const int MaxRetries = 5;

    public async Task<FiscalizeResultDto> FiscalizeTransactionAsync(
        Guid transactionId,
        CancellationToken ct = default)
    {
        var tx = await _pos.GetTransactionByIdAsync(transactionId, ct);
        if (tx is null)
            return new FiscalizeResultDto(Ok: false, Error: "Transaction not found.");

        if (tx.Status == "fiscalized")
            return new FiscalizeResultDto(Ok: true, FiscalNumber: tx.FiscalNumber);

        // Always increment retry count, regardless of outcome
        tx.RetryCount++;

        try
        {
            var fiscal = await _fiscalFactory.GetForTenantAsync(tx.TenantId, ct);

            var fiscalRequest = new FiscalReceiptRequest(
                Items: tx.Items.Select(i => new FiscalReceiptItem(
                    Code: i.ProductId.ToString(),
                    Name: i.Product?.Name ?? i.ProductId.ToString(),
                    UnitPrice: i.PriceRetail - i.DiscountAmount,
                    Quantity: i.Quantity,
                    Barcode: i.Product?.Barcodes.Count > 0 ? i.Product.Barcodes[0] : null)).ToList(),
                Payments: [new FiscalPayment(
                    string.Equals(tx.PaymentType, "card", StringComparison.OrdinalIgnoreCase)
                        ? FiscalPaymentKind.Cashless : FiscalPaymentKind.Cash,
                    tx.TotalAmount)],
                LocalReceiptId: tx.Id.ToString());

            var receipt = await fiscal.CreateReceiptAsync(fiscalRequest, ct);

            if (receipt.Status == FiscalReceiptStatus.Done)
            {
                tx.FiscalNumber = receipt.FiscalNumber;
                tx.Status = "fiscalized";
                _pos.UpdateTransaction(tx);
                await _pos.SaveChangesAsync(ct);

                _log.LogInformation(
                    "Fiscalization succeeded for tx {TxId}: fiscal_code={FiscalCode}",
                    tx.Id, tx.FiscalNumber);

                return new FiscalizeResultDto(Ok: true, FiscalNumber: tx.FiscalNumber);
            }

            // Provider accepted but not yet DONE (Created/PendingFiscalization)
            // Stay pending_fiscalization, let next cron run pick it up if still under limit.
            var errorMsg = $"Provider returned status {receipt.Status}.";

            if (tx.RetryCount >= MaxRetries)
            {
                tx.Status = "fiscalization_failed";
                _log.LogWarning(
                    "Fiscalization permanently failed for tx {TxId} after {Count} retries: {Reason}",
                    tx.Id, tx.RetryCount, errorMsg);
            }

            _pos.UpdateTransaction(tx);
            await _pos.SaveChangesAsync(ct);

            return new FiscalizeResultDto(Ok: false, Error: errorMsg);
        }
        catch (Exception ex)
        {
            var errorMsg = ex.Message;

            if (tx.RetryCount >= MaxRetries)
            {
                tx.Status = "fiscalization_failed";
                _log.LogWarning(ex,
                    "Fiscalization permanently failed for tx {TxId} after {Count} retries",
                    tx.Id, tx.RetryCount);
            }
            else
            {
                _log.LogWarning(ex,
                    "Fiscalization attempt #{Count} failed for tx {TxId} — will retry",
                    tx.RetryCount, tx.Id);
            }

            _pos.UpdateTransaction(tx);
            await _pos.SaveChangesAsync(ct);

            return new FiscalizeResultDto(Ok: false, Error: errorMsg);
        }
    }

    // ── List sales for shift ───────────────────────────────────────────────

    public async Task<SalesListDto> GetSalesForShiftAsync(
        Guid tenantId, Guid shiftId, CancellationToken ct = default)
    {
        var transactions = (await _pos.GetTransactionsByShiftAsync(shiftId, ct))
            .Where(t => t.TenantId == tenantId)
            .ToList();

        // TASK-410: PosTransaction carries no LoyaltyMembershipId of its own — the ledger
        // (matched via LoyaltyLedgerEntry.PosTransactionId) is the only persisted record of
        // whether a sale had any loyalty activity, and for how much. Batched once for the
        // whole shift rather than queried per row.
        var ledgerByTx = (await _loyalty.GetLedgerEntriesForTransactionsAsync(
                tenantId, transactions.Select(t => t.Id).ToList(), ct))
            .Where(e => e.PosTransactionId.HasValue)
            .GroupBy(e => e.PosTransactionId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(e => e.CreatedAt).ToList());

        var dtos = transactions
            .Select(t =>
            {
                decimal? loyaltyAccrued = null;
                decimal? loyaltyRedeemed = null;
                decimal? loyaltyBalance = null;

                if (ledgerByTx.TryGetValue(t.Id, out var entries))
                {
                    var accrualSum = entries
                        .Where(e => e.EntryType == LoyaltyEntryType.Accrual)
                        .Sum(e => e.Amount);
                    if (accrualSum > 0m)
                        loyaltyAccrued = accrualSum;

                    // Stored as a negative delta (see CreateSaleAsync) — surfaced here as a
                    // positive figure, matching CreateSaleAsync's own LoyaltyRedeemed sign and
                    // what SaleDetailDrawer.tsx already renders ("-{value} ₴").
                    var redemptionSum = entries
                        .Where(e => e.EntryType == LoyaltyEntryType.Redemption)
                        .Sum(e => e.Amount);
                    if (redemptionSum < 0m)
                        loyaltyRedeemed = -redemptionSum;

                    // entries is ordered oldest-first above; the LAST one is whichever ledger
                    // effect completed this transaction (redemption then accrual, per
                    // CreateSaleAsync's write order), so its BalanceAfter is the membership's
                    // balance at the moment THIS sale finished — i.e. a historical, per-sale
                    // snapshot, not the membership's current/live balance (which a later sale
                    // on the same membership may have since moved further).
                    loyaltyBalance = entries[^1].BalanceAfter;
                }

                return new SaleDto(
                    TransactionId: t.Id,
                    ShiftId: t.ShiftId ?? shiftId,
                    Items: t.Items.Select(i => new SaleItemDto(
                        ProductId: i.ProductId,
                        ProductName: i.Product?.Name ?? "—",
                        Barcode: i.Product?.Barcodes.Count > 0 ? i.Product.Barcodes[0] : string.Empty,
                        Quantity: i.Quantity,
                        UnitPrice: i.PriceRetail,
                        DiscountAmount: i.DiscountAmount,
                        Total: Math.Round(i.PriceFinal * i.Quantity, 2))).ToList(),
                    Subtotal: t.TotalAmount,
                    PaymentType: t.PaymentType,
                    PaymentAmount: t.TotalAmount, // payment amount stored in TotalAmount
                    Change: 0,
                    FiscalStatus: t.Status,
                    FiscalNumber: t.FiscalNumber,
                    ReceiptNumber: t.ReceiptNumber,
                    CreatedAt: t.CreatedAt,
                    LoyaltyAccrued: loyaltyAccrued,
                    LoyaltyRedeemed: loyaltyRedeemed,
                    LoyaltyBalance: loyaltyBalance,
                    CustomerId: t.CustomerId,
                    CustomerName: t.Customer?.Name);
            })
            .ToList();

        return new SalesListDto(dtos, dtos.Sum(d => d.Subtotal));
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private async Task<FiscalShiftResult> PollShiftStatusAsync(
        IFiscalService fiscal,
        string providerShiftId,
        FiscalShiftStatus targetStatus,
        CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(ShiftOpenTimeoutMs);
        FiscalShiftResult? last = null;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                last = await fiscal.GetShiftStatusAsync(providerShiftId, ct);
                if (last.Status == targetStatus)
                    return last;
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Polling shift {ShiftId} status failed — retrying", providerShiftId);
            }

            await Task.Delay(ShiftOpenPollIntervalMs, ct);
        }

        _log.LogWarning("Timeout polling shift {ShiftId} for status {Target}", providerShiftId, targetStatus);
        return last ?? new FiscalShiftResult(providerShiftId, FiscalShiftStatus.PendingFiscalization, null, null, null);
    }

    private static ShiftDto ToShiftDto(
        PosShift shift, string fiscalStatus, string? providerShiftId,
        decimal? expectedCashAmount = null, decimal? cashDiscrepancy = null) => new(
        ShiftId: shift.Id,
        StoreId: shift.StoreId,
        Status: shift.ClosedAt.HasValue ? "Closed" : "Open",
        OpenedAt: shift.OpenedAt,
        ClosedAt: shift.ClosedAt,
        ProviderShiftId: providerShiftId,
        FiscalStatus: fiscalStatus,
        TotalSales: shift.TotalSales,
        ShiftNumber: shift.ShiftNumber,
        OpeningCash: shift.OpeningCash,
        ClosingCash: shift.ClosingCash,
        ExpectedCashAmount: expectedCashAmount,
        CashDiscrepancy: cashDiscrepancy);

    // ── Private types ──────────────────────────────────────────────────────

    private sealed record ResolvedSaleItem(
        Item Product,
        List<ProductStock> Batches,
        decimal Quantity,
        decimal PriceRetail,
        decimal DiscountAmount);
}

