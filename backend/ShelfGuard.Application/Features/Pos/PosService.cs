using Microsoft.Extensions.Logging;
using System.Text.Json;
using ShelfGuard.Application.Features.Pos.Dtos;
using ShelfGuard.Application.Features.Pos.Fiscal;
using ShelfGuard.Application.Features.Promotions;
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
    private readonly IUserRepository? _users;
    private readonly ICashRegisterRepository? _registers;
    private readonly IPromotionRuleRepository? _promotions;
    private readonly ILocationRepository? _locations;

    // Offline-sale attribution window (batch sync).
    public static readonly TimeSpan MaxFutureSkew = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan MaxSaleAge = TimeSpan.FromDays(14);
    public static readonly TimeSpan SessionEndTolerance = TimeSpan.FromMinutes(5);

    public PosService(
        IPosRepository pos,
        IStockRepository stock,
        IItemRepository catalog,
        IDiscountRepository discounts,
        IFiscalServiceFactory fiscalFactory,
        ILoyaltyRepository loyalty,
        ICustomerRepository customers,
        ILogger<PosService> log,
        IUserRepository? users = null,
        ICashRegisterRepository? registers = null,
        IPromotionRuleRepository? promotions = null,
        ILocationRepository? locations = null)
    {
        _locations = locations;
        _users = users;
        _registers = registers;
        _promotions = promotions;
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
        Guid tenantId, Guid cashierId, OpenShiftRequest request, CancellationToken ct = default,
        Guid? registerId = null)
    {
        // 409: legacy/mobile flow = one open shift per tenant; register flow = one per register.
        var existing = registerId.HasValue
            ? await _pos.GetOpenShiftForRegisterAsync(registerId.Value, ct)
            : await _pos.GetOpenShiftAsync(tenantId, ct);
        if (existing is not null)
            return (null, registerId.HasValue
                ? "A shift is already open on this register."
                : "A shift is already open for this tenant.", 409);

        var shift = new PosShift
        {
            TenantId = tenantId,
            StoreId = request.StoreId,
            CashierId = cashierId,
            RegisterId = registerId,
            // Local sequential number (per register, or per tenant for legacy shifts) so it is
            // meaningful even without a fiscal provider; the ДПС number stays in FiscalShiftNumber.
            ShiftNumber = await _pos.GetNextShiftNumberAsync(tenantId, registerId, ct),
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

    public async Task<ShiftDto?> GetCurrentShiftAsync(
        Guid tenantId, CancellationToken ct = default, Guid? registerId = null)
    {
        var shift = registerId.HasValue
            ? await _pos.GetOpenShiftForRegisterAsync(registerId.Value, ct)
            : await _pos.GetOpenShiftAsync(tenantId, ct);
        return shift is null ? null : ToShiftDto(shift, "open", shift.FiscalShiftNumber);
    }

    // ── Close shift ────────────────────────────────────────────────────────

    public async Task<(ShiftDto? Shift, string? Error, int? StatusCode)> CloseShiftAsync(
        Guid tenantId, CloseShiftRequest? request = null, CancellationToken ct = default,
        Guid? registerId = null)
    {
        var shift = registerId.HasValue
            ? await _pos.GetOpenShiftForRegisterAsync(registerId.Value, ct)
            : await _pos.GetOpenShiftAsync(tenantId, ct);
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

    public Task<(SaleDto? Sale, string? Error, int? StatusCode)> CreateSaleAsync(
        Guid tenantId, Guid cashierId, CreateSaleRequest request,
        CancellationToken ct = default, Guid? registerId = null)
    {
        // Single sale: always the signed-in operator; OccurredAt is ignored. Attributing a sale to
        // someone else is only possible through the batch (offline sync) path, with proof.
        if (request.CashierId.HasValue && request.CashierId.Value != cashierId)
            return Task.FromResult<(SaleDto?, string?, int?)>(
                (null, "CashierId must match the signed-in cashier.", 400));

        return CreateSaleCoreAsync(tenantId, cashierId, request, registerId, inlineFiscalization: true, ct);
    }

    // ── Batch sale sync (offline register apps) ────────────────────────────

    public const int MaxBatchSales = 100;

    public async Task<(BatchSalesResultDto? Result, string? Error, int? StatusCode)> CreateSalesBatchAsync(
        Guid tenantId, Guid cashierId, BatchSalesRequest request,
        CancellationToken ct = default, Guid? registerId = null)
    {
        if (request.Sales is null || request.Sales.Count == 0)
            return (null, "Batch must contain at least one sale.", 400);
        if (request.Sales.Count > MaxBatchSales)
            return (null, $"Batch is limited to {MaxBatchSales} sales.", 400);

        var results = new List<BatchSaleItemResultDto>(request.Sales.Count);

        foreach (var sale in request.Sales)
        {
            if (sale.ClientTransactionId is not Guid clientId || clientId == Guid.Empty)
            {
                results.Add(new BatchSaleItemResultDto(null, "failed", null, "ClientTransactionId is required.", 400));
                continue;
            }

            try
            {
                var existing = await _pos.GetTransactionByClientIdAsync(tenantId, clientId, ct);
                if (existing is not null)
                {
                    var (dup, dupError, dupStatus) = await ResolveReplayAsync(tenantId, existing, sale, registerId, ct);
                    results.Add(dupError is null
                        ? new BatchSaleItemResultDto(clientId, "duplicate", dup, null, null)
                        : new BatchSaleItemResultDto(clientId, "failed", null, dupError, dupStatus));
                    continue;
                }

                // Fiscalization stays pending here on purpose: a batch of N queued sales must not
                // block on N x (up to 8 s) Checkbox round trips; the retry worker picks them up.
                var (effectiveCashier, attrError, attrStatus) =
                    await ResolveAttributedCashierAsync(tenantId, cashierId, sale, registerId, ct);
                if (attrError is not null)
                {
                    results.Add(new BatchSaleItemResultDto(clientId, "failed", null, attrError, attrStatus));
                    continue;
                }

                var (dto, error, status) = await CreateSaleCoreAsync(
                    tenantId, effectiveCashier, sale, registerId, inlineFiscalization: false, ct);

                if (error is null)
                {
                    results.Add(new BatchSaleItemResultDto(clientId, "created", dto, null, null));
                }
                else
                {
                    _pos.DiscardPendingChanges();
                    results.Add(new BatchSaleItemResultDto(clientId, "failed", null, error, status));
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // One bad item must never take down the rest of the batch.
                _log.LogError(ex, "Batch sale {ClientId} failed unexpectedly for tenant {TenantId}", clientId, tenantId);
                _pos.DiscardPendingChanges();
                results.Add(new BatchSaleItemResultDto(clientId, "failed", null, "Unexpected error.", 500));
            }
        }

        return (new BatchSalesResultDto(results), null, null);
    }

    /// <summary>
    /// Batch only. Decides whose sale this is: the token's cashier (CashierId absent/equal), or a
    /// colleague the token holder is flushing for - accepted only with proof the colleague worked
    /// this register at OccurredAt, so a token cannot attribute sales to arbitrary people.
    /// </summary>
    private async Task<(Guid Cashier, string? Error, int? StatusCode)> ResolveAttributedCashierAsync(
        Guid tenantId, Guid tokenCashierId, CreateSaleRequest sale, Guid? registerId, CancellationToken ct)
    {
        if (sale.CashierId is not Guid claimed || claimed == tokenCashierId)
            return (tokenCashierId, null, null);

        const string denied = "Sale cannot be attributed to the requested cashier.";

        if (registerId is not Guid regId || _users is null || _registers is null)
            return (default, denied + " Only a register operator may sync another cashier's sales.", 403);

        // (c) plausible time: not in the future (5 min skew), not older than 14 days.
        var now = DateTime.UtcNow;
        var at = (sale.OccurredAt ?? now).ToUniversalTime();
        if (at > now + MaxFutureSkew)
            return (default, "OccurredAt is in the future.", 403);
        if (at < now - MaxSaleAge)
            return (default, "OccurredAt is older than 14 days.", 403);

        // (a) same checks as PIN sign-in: exists in tenant, active, POS role, this register's store.
        var register = await _registers.GetByIdAsync(regId, ct);
        var user = await _users.GetByIdAsync(claimed, ct);
        if (register is null || user is null || user.TenantId != tenantId || !user.IsActive
            || !Registers.RegisterService.IsPosRole(user.Role) || user.StoreId != register.StoreId)
            return (default, denied + " Cashier is unknown, inactive, not a POS user or not of this store.", 403);

        // (b) the cashier really worked THIS register at that moment.
        if (!await _registers.HadSessionAtAsync(regId, claimed, at, SessionEndTolerance, ct))
            return (default, denied + " Cashier had no session on this register at that time.", 403);

        return (claimed, null, null);
    }

    /// <summary>
    /// A stored sale found for a replayed ClientTransactionId. Returns it only when it belongs to
    /// the same register (if both are known) and was made of the same shift/payment/items;
    /// otherwise 409 - a reused key must never silently "succeed" with someone else's sale.
    /// </summary>
    private async Task<(SaleDto? Sale, string? Error, int? StatusCode)> ResolveReplayAsync(
        Guid tenantId, PosTransaction stored, CreateSaleRequest request, Guid? registerId, CancellationToken ct)
    {
        const string conflict = "ClientTransactionId was already used for a different sale.";

        if (registerId.HasValue && stored.CashRegisterId.HasValue && stored.CashRegisterId != registerId)
            return (null, conflict, 409);

        // An explicit CashierId must be the one the sale was stored under.
        if (request.CashierId.HasValue && stored.CashierId != request.CashierId)
            return (null, conflict, 409);

        // A promotion can split one requested line into several stored sub-lines (same product),
        // so the line-count check only holds for sales without promo sub-lines; the per-product
        // quantity comparison below is the authoritative one either way.
        var storedHasPromoSubLines = stored.Items.Any(i => i.PromotionRuleId.HasValue);
        if (stored.ShiftId != request.ShiftId
            || !string.Equals(stored.PaymentType, request.PaymentType, StringComparison.OrdinalIgnoreCase)
            || request.Items is null
            || (!storedHasPromoSubLines && request.Items.Count != stored.Items.Count))
            return (null, conflict, 409);

        var wanted = new Dictionary<Guid, decimal>();
        foreach (var line in request.Items)
        {
            var product = await _catalog.GetByBarcodeAsync(line.Barcode, ct);
            if (product is null) return (null, conflict, 409);
            wanted[product.Id] = wanted.GetValueOrDefault(product.Id) + line.Quantity;
        }

        var actual = stored.Items
            .GroupBy(i => i.ProductId)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));
        if (wanted.Count != actual.Count || wanted.Any(kv => actual.GetValueOrDefault(kv.Key) != kv.Value))
            return (null, conflict, 409);

        return (await MapExistingSaleAsync(tenantId, stored, ct), null, null);
    }

    private async Task<SaleDto> MapExistingSaleAsync(Guid tenantId, PosTransaction tx, CancellationToken ct)
    {
        var entries = (await _loyalty.GetLedgerEntriesForTransactionsAsync(tenantId, [tx.Id], ct))
            .OrderBy(e => e.CreatedAt).ToList();
        return ToSaleDto(tx, tx.ShiftId ?? Guid.Empty, entries);
    }

    private async Task<(SaleDto? Sale, string? Error, int? StatusCode)> CreateSaleCoreAsync(
        Guid tenantId, Guid cashierId, CreateSaleRequest request, Guid? registerId,
        bool inlineFiscalization, CancellationToken ct)
    {
        // 0. Idempotency: a replayed offline sale returns the stored one, never a duplicate.
        if (request.ClientTransactionId is Guid clientTxId && clientTxId != Guid.Empty)
        {
            var already = await _pos.GetTransactionByClientIdAsync(tenantId, clientTxId, ct);
            if (already is not null)
                return await ResolveReplayAsync(tenantId, already, request, registerId, ct);
        }

        // 1. Validate shift
        var shift = await _pos.GetShiftByIdAsync(request.ShiftId, ct);
        if (shift is null || shift.TenantId != tenantId || shift.ClosedAt.HasValue)
            return (null, "Shift not found or not open.", 409);

        if (registerId.HasValue && shift.RegisterId != registerId)
            return (null, "Shift belongs to a different register.", 409);

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

        // TASK-615: tier discount percent applied per item in the loop below, gated the same
        // way as accrual/redemption (membership present + program enabled) so a disabled
        // program never grants tier benefits even when the membership carries a CurrentTier.
        // A membership with no CurrentTier yet (nightly recompute job hasn't run, or the
        // feature was just enabled) behaves exactly as before this change (0% discount).
        var tierDiscountPercent = membership is not null && loyaltySettings is { IsEnabled: true }
            ? membership.CurrentTier?.DiscountPercent ?? 0m
            : 0m;

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

            var stockDiscountAmount = discountAmount;

            // TASK-615: tier discount applied per item (not as a lump-sum reduction against
            // tx.TotalAmount) so each line's DiscountAmount/PriceFinal already reflects it and
            // the transaction total stays internally consistent with the sum of item totals —
            // same principle the critical-batch auto-discount above already follows. Computed
            // off priceRetail independently of that auto-discount, then combined into one
            // DiscountAmount per item (capped so a line's discount can never exceed its price).
            if (tierDiscountPercent > 0m)
            {
                var tierDiscountAmount = Math.Round(priceRetail * tierDiscountPercent / 100m, 2);
                discountAmount = Math.Min(priceRetail, discountAmount + tierDiscountAmount);
            }
            // Like redemption below, this discount reduces tx.TotalAmount, the monetary base the (not-yet-built) RFM/tier composite-score job will read — a pre-existing, accepted pattern.

            resolvedItems.Add(new ResolvedSaleItem(
                Product: product,
                Batches: batches.Where(b => b.ExpiryDate > today).ToList(),
                Quantity: itemReq.Quantity,
                PriceRetail: priceRetail,
                DiscountAmount: discountAmount,
                StockDiscount: stockDiscountAmount));
        }

        // 2b. Promotion rules (promotion-rules.md). Only lines where a rule actually priced at
        // least one unit are re-priced (and possibly split into sub-lines); every other line keeps
        // the exact legacy stock/tier discount computed above.
        var pricingAt = ResolvePricingInstant(request, inlineFiscalization);
        var promo = await EvaluatePromotionsAsync(
            tenantId, storeId, pricingAt, resolvedItems,
            membership is not null && loyaltySettings is { IsEnabled: true }
                ? new PromotionMembership(membership.Id, membership.CurrentTierId, tierDiscountPercent)
                : null,
            ct);

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
            LoyaltyMembershipId = request.LoyaltyMembershipId,
            CashRegisterId = registerId ?? shift.RegisterId,
            TenderedAmount = request.PaymentAmount,
            ClientTransactionId = request.ClientTransactionId is { } cid && cid != Guid.Empty ? cid : null,
        };

        var txItemDtos = new List<SaleItemDto>();
        var subLines = new List<SaleSubLine>();
        var stockEvents = new List<StockEvent>();

        for (var lineIndex = 0; lineIndex < resolvedItems.Count; lineIndex++)
        {
            var resolved = resolvedItems[lineIndex];
            var priceFinal = Math.Max(0, resolved.PriceRetail - resolved.DiscountAmount);
            decimal remaining = resolved.Quantity;

            // FEFO write-down: consume batches in order (nearest expiry first) for the WHOLE line
            // quantity - promo sub-lines below only split the money, never the stock movement.
            Guid? primaryBatchId = null;
            var consumption = new List<(Guid BatchId, decimal Quantity)>();
            foreach (var batch in resolved.Batches)
            {
                if (remaining <= 0) break;

                var consume = Math.Min(remaining, batch.Quantity);
                primaryBatchId ??= batch.Id;
                consumption.Add((batch.Id, consume));

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

            var segments = promo?.Segments[lineIndex];
            if (segments is null)
            {
                AddSubLine(resolved, primaryBatchId, resolved.Quantity,
                    resolved.DiscountAmount, priceFinal, null, null);
                continue;
            }

            // Promo-priced line: one sub-line per segment (e.g. 2 @ regular + 1 @ 0.01), each
            // pointing at the FEFO batch its first unit was taken from.
            var offset = 0m;
            foreach (var seg in segments)
            {
                AddSubLine(resolved, BatchAt(consumption, offset) ?? primaryBatchId, seg.Quantity,
                    resolved.PriceRetail - seg.UnitPrice, seg.UnitPrice, seg.RuleId, seg.Label);
                offset += seg.Quantity;
            }
        }

        void AddSubLine(ResolvedSaleItem resolved, Guid? batchId, decimal quantity,
            decimal discountAmount, decimal priceFinal, Guid? ruleId, string? label)
        {
            tx.Items.Add(new PosTransactionItem
            {
                TransactionId = tx.Id,
                ProductId = resolved.Product.Id,
                ProductStockId = batchId,
                Quantity = quantity,
                PriceRetail = resolved.PriceRetail,
                DiscountAmount = discountAmount,
                PriceFinal = priceFinal,
                PromotionRuleId = ruleId,
                PromotionLabel = label,
            });

            var dto = new SaleItemDto(
                ProductId: resolved.Product.Id,
                ProductName: resolved.Product.Name,
                Barcode: resolved.Product.Barcodes.Count > 0 ? resolved.Product.Barcodes[0] : string.Empty,
                Quantity: quantity,
                UnitPrice: resolved.PriceRetail,
                DiscountAmount: discountAmount,
                Total: Math.Round(priceFinal * quantity, 2),
                PromotionRuleId: ruleId,
                PromotionLabel: label);
            txItemDtos.Add(dto);
            subLines.Add(new SaleSubLine(resolved.Product, quantity, priceFinal, discountAmount, dto.Total));
        }

        // Set totals
        tx.TotalAmount = txItemDtos.Sum(i => i.Total);

        // Execution log: one row per rule that priced at least one unit (committed with the sale).
        var applications = promo is null
            ? []
            : BuildApplications(promo, tenantId, tx.Id, storeId, tx.CashRegisterId, pricingAt);

        var excludedProductIds = loyaltySettings?.BonusExclusionsEnabled == true
            ? JsonSerializer.Deserialize<HashSet<Guid>>(loyaltySettings.ExcludedProductIdsJson) ?? [] : [];
        var excludedCategoryIds = loyaltySettings?.BonusExclusionsEnabled == true
            ? JsonSerializer.Deserialize<HashSet<Guid>>(loyaltySettings.ExcludedCategoryIdsJson) ?? [] : [];
        bool IsBonusExcluded(SaleSubLine line) => loyaltySettings?.BonusExclusionsEnabled == true &&
            (excludedProductIds.Contains(line.Product.Id) ||
             (line.Product.CategoryId is Guid categoryId && excludedCategoryIds.Contains(categoryId)) ||
             (loyaltySettings.ExcludeDiscountedItems && line.DiscountAmount > 0));
        var eligibleBonusTotal = subLines.Where(x => !IsBonusExcluded(x)).Sum(x => x.Total);

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
            var qualifiesForFirstPurchaseReward = loyaltySettings.FirstPurchaseRewardEnabled
                && loyaltySettings.FirstPurchaseRewardAmount > 0m
                && !await _loyalty.HasPurchaseAccrualAsync(tenantId, membership.Id, ct);
            if (request.RedeemAmount is > 0m)
            {
                var redeem = request.RedeemAmount.Value;
                var bonusUnitsPerCurrencyUnit = loyaltySettings.BonusUnitsPerCurrencyUnit;
                var redeemedCurrencyAmount = Math.Round(
                    redeem / bonusUnitsPerCurrencyUnit, 2, MidpointRounding.AwayFromZero);
                var redemptionBase = loyaltySettings.BonusExclusionsEnabled && loyaltySettings.ExclusionsApplyToRedemption
                    ? eligibleBonusTotal : tx.TotalAmount;
                var capInCurrency = Math.Round(redemptionBase * loyaltySettings.RedemptionCapPercent / 100m, 2);
                var capInBonusUnits = Math.Floor(capInCurrency * bonusUnitsPerCurrencyUnit * 100m) / 100m;
                if (redeem > capInBonusUnits)
                    return (null, $"Redeem amount exceeds the redemption cap ({loyaltySettings.RedemptionCapPercent:0.##}% of the sale).", 400);
                if (redeem > membership.Balance)
                    return (null, "Insufficient loyalty balance.", 400);
                if (membership.Balance - redeem < loyaltySettings.MinRedemptionBalance)
                    return (null, "Redemption would drop the balance below the minimum allowed.", 400);

                tx.TotalAmount -= redeemedCurrencyAmount;
                membership.Balance -= redeem;
                loyaltyRedeemed = redeem;
                var remainingToAllocate = redeem;
                foreach (var lot in await _loyalty.GetAvailableBonusLotsAsync(tenantId, membership.Id, ct))
                {
                    if (remainingToAllocate <= 0) break;
                    var consumed = Math.Min(lot.RemainingAmount, remainingToAllocate);
                    lot.RemainingAmount -= consumed;
                    remainingToAllocate -= consumed;
                }
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

            // Each configured tier owns its cashback percentage directly. The legacy global
            // rate remains only as a safe fallback until a membership receives its first tier.
            var cashbackPercent = membership.CurrentTier?.AccrualMultiplier
                ?? loyaltySettings.AccrualRatePercent;
            var accrualBase = loyaltySettings.BonusExclusionsEnabled && loyaltySettings.ExclusionsApplyToAccrual
                ? Math.Max(0m, eligibleBonusTotal - ((loyaltyRedeemed ?? 0m) / loyaltySettings.BonusUnitsPerCurrencyUnit))
                : tx.TotalAmount;
            var accrual = Math.Round(
                accrualBase * cashbackPercent / 100m * loyaltySettings.BonusUnitsPerCurrencyUnit,
                2,
                MidpointRounding.AwayFromZero);
            if (accrual > 0m)
            {
                membership.Balance += accrual;
                loyaltyAccrued = accrual;
                var accrualEntry = new LoyaltyLedgerEntry
                {
                    TenantId = tenantId,
                    MembershipId = membership.Id,
                    EntryType = LoyaltyEntryType.Accrual,
                    Amount = accrual,
                    BalanceAfter = membership.Balance,
                    PosTransactionId = tx.Id,
                };
                await _loyalty.AddLedgerEntryAsync(accrualEntry, ct);
                await _loyalty.AddBonusLotAsync(new LoyaltyBonusLot
                {
                    TenantId = tenantId,
                    MembershipId = membership.Id,
                    SourceLedgerEntryId = accrualEntry.Id,
                    OriginalAmount = accrual,
                    RemainingAmount = accrual,
                    ExpiresAt = loyaltySettings.BonusLifetimeEnabled
                        ? DateTimeOffset.UtcNow.AddDays(loyaltySettings.BonusLifetimeDays)
                        : null,
                }, ct);
            }

            if (qualifiesForFirstPurchaseReward)
            {
                var reward = loyaltySettings.FirstPurchaseRewardAmount;
                membership.Balance += reward;
                var rewardEntry = new LoyaltyLedgerEntry { TenantId = tenantId, MembershipId = membership.Id,
                    EntryType = LoyaltyEntryType.Reward, Amount = reward, BalanceAfter = membership.Balance,
                    PosTransactionId = tx.Id, Note = "Винагорода за першу покупку" };
                await _loyalty.AddLedgerEntryAsync(rewardEntry, ct);
                await _loyalty.AddBonusLotAsync(new LoyaltyBonusLot { TenantId = tenantId,
                    MembershipId = membership.Id, SourceLedgerEntryId = rewardEntry.Id,
                    OriginalAmount = reward, RemainingAmount = reward,
                    ExpiresAt = loyaltySettings.BonusLifetimeEnabled
                        ? DateTimeOffset.UtcNow.AddDays(loyaltySettings.BonusLifetimeDays) : null }, ct);
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
        if (applications.Count > 0)
            await _promotions!.AddApplicationsAsync(applications, ct);

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
        catch (DuplicateClientTransactionException)
        {
            // Lost the race against an identical concurrent submission: nothing of ours was
            // persisted, so hand back the winner's stored sale.
            _pos.DiscardPendingChanges();
            var winner = await _pos.GetTransactionByClientIdAsync(tenantId, request.ClientTransactionId!.Value, ct);
            if (winner is null)
                return (null, "Duplicate client transaction could not be resolved. Please retry.", 409);
            return await ResolveReplayAsync(tenantId, winner, request, registerId, ct);
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

        if (inlineFiscalization)
        try
        {
            var fiscal = await _fiscalFactory.GetForTenantAsync(tenantId, fiscalCts.Token);
            var fiscalRequest = new FiscalReceiptRequest(
                Items: subLines.Select(r => new FiscalReceiptItem(
                    Code: r.Product.Id.ToString(),
                    Name: r.Product.Name,
                    UnitPrice: r.PriceFinal,
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
            .Select(t => ToSaleDto(t, shiftId, ledgerByTx.GetValueOrDefault(t.Id)))
            .ToList();

        return new SalesListDto(dtos, dtos.Sum(d => d.Subtotal));
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// Maps a stored transaction to a SaleDto. <paramref name="entries"/> = that sale's loyalty
    /// ledger entries ordered oldest-first (null/empty when the sale had no loyalty activity).
    /// </summary>
    private static SaleDto ToSaleDto(PosTransaction t, Guid shiftId, List<LoyaltyLedgerEntry>? entries)
    {
        decimal? loyaltyAccrued = null;
        decimal? loyaltyRedeemed = null;
        decimal? loyaltyBalance = null;

        if (entries is { Count: > 0 })
        {
            var accrualSum = entries
                .Where(e => e.EntryType == LoyaltyEntryType.Accrual)
                .Sum(e => e.Amount);
            if (accrualSum > 0m)
                loyaltyAccrued = accrualSum;

            // Stored as a negative delta (see CreateSaleAsync) - surfaced as a positive figure.
            var redemptionSum = entries
                .Where(e => e.EntryType == LoyaltyEntryType.Redemption)
                .Sum(e => e.Amount);
            if (redemptionSum < 0m)
                loyaltyRedeemed = -redemptionSum;

            // The LAST entry is whichever ledger effect completed this transaction, so its
            // BalanceAfter is the membership balance at the moment THIS sale finished.
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
                Total: Math.Round(i.PriceFinal * i.Quantity, 2),
                PromotionRuleId: i.PromotionRuleId,
                PromotionLabel: i.PromotionLabel)).ToList(),
            Subtotal: t.TotalAmount,
            PaymentType: t.PaymentType,
            PaymentAmount: t.TenderedAmount ?? t.TotalAmount,
            Change: t.TenderedAmount is { } tendered ? Math.Max(0, tendered - t.TotalAmount) : 0,
            FiscalStatus: t.Status,
            FiscalNumber: t.FiscalNumber,
            ReceiptNumber: t.ReceiptNumber,
            CreatedAt: t.CreatedAt,
            LoyaltyAccrued: loyaltyAccrued,
            LoyaltyRedeemed: loyaltyRedeemed,
            LoyaltyBalance: loyaltyBalance,
            CustomerId: t.CustomerId,
            CustomerName: t.Customer?.Name);
    }

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
        decimal DiscountAmount,
        decimal StockDiscount);

    /// <summary>One persisted pos_transaction_items row (a whole line, or a promo sub-line).</summary>
    private sealed record SaleSubLine(Item Product, decimal Quantity, decimal PriceFinal, decimal DiscountAmount, decimal Total);

    private sealed record PromotionOutcome(
        IReadOnlyList<IReadOnlyList<PromotionSegment>?> Segments,
        IReadOnlyList<PromotionLineInput> Lines,
        IReadOnlyDictionary<Guid, PromotionRuleInput> Rules);

    // ── Promotions ─────────────────────────────────────────────────────────

    /// <summary>
    /// Single sales are priced "now". Batch (offline) sales are priced at their OccurredAt so the
    /// rules that were in scope when the customer paid apply - but only inside the same plausible
    /// window the attribution check uses (5 min future skew, 14 days back); otherwise "now".
    /// </summary>
    private static DateTime ResolvePricingInstant(CreateSaleRequest request, bool inlineFiscalization)
    {
        var now = DateTime.UtcNow;
        if (inlineFiscalization || request.OccurredAt is not DateTime occurred) return now;
        var at = occurred.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(occurred, DateTimeKind.Utc)
            : occurred.ToUniversalTime();
        return at <= now + MaxFutureSkew && at >= now - MaxSaleAge ? at : now;
    }

    /// <summary>The store's IANA time zone (locations.TimeZone); null when unavailable (engine then uses UTC).</summary>
    private async Task<string?> GetStoreTimeZoneAsync(Guid storeId, CancellationToken ct) =>
        _locations is null ? null : (await _locations.GetByIdAsync(storeId, ct))?.TimeZone;

    private async Task<PromotionOutcome?> EvaluatePromotionsAsync(
        Guid tenantId, Guid storeId, DateTime pricingAt, List<ResolvedSaleItem> items,
        PromotionMembership? membership, CancellationToken ct)
    {
        if (_promotions is null || items.Count == 0) return null;

        var rules = PromotionRuleLoader.ToInputs(await _promotions.GetPublishedAsync(tenantId, ct));
        if (rules.Count == 0) return null;

        var lines = items.Select(r => new PromotionLineInput(
            ItemId: r.Product.Id,
            CategoryId: r.Product.CategoryId,
            UnitPrice: Math.Max(0m, r.PriceRetail - r.StockDiscount),
            Quantity: r.Quantity,
            HasStockDiscount: r.StockDiscount > 0m,
            RegularPrice: r.PriceRetail)).ToList();

        var result = PromotionEngine.Evaluate(lines, rules,
            new PromotionContext(storeId, new DateTimeOffset(pricingAt, TimeSpan.Zero),
                0, membership, await GetStoreTimeZoneAsync(storeId, ct)));

        var segments = result.Lines
            .Select(l => l.Segments.Any(s => s.RuleId.HasValue) ? l.Segments : null)
            .ToList();
        if (segments.All(s => s is null)) return null;

        return new PromotionOutcome(segments, lines, rules.ToDictionary(r => r.Id));
    }

    private static List<PromotionRuleApplication> BuildApplications(
        PromotionOutcome promo, Guid tenantId, Guid transactionId, Guid storeId, Guid? registerId, DateTime appliedAt)
    {
        var totals = new Dictionary<Guid, (decimal Units, decimal Discount)>();
        for (var i = 0; i < promo.Segments.Count; i++)
        {
            if (promo.Segments[i] is not { } segs) continue;
            foreach (var seg in segs)
            {
                if (seg.RuleId is not Guid ruleId) continue;
                var (units, discount) = totals.GetValueOrDefault(ruleId);
                totals[ruleId] = (units + seg.Quantity, discount + (promo.Lines[i].UnitPrice - seg.UnitPrice) * seg.Quantity);
            }
        }

        return totals.Select(kv => new PromotionRuleApplication
        {
            TenantId = tenantId,
            RuleId = kv.Key,
            VersionId = promo.Rules[kv.Key].VersionId,
            TransactionId = transactionId,
            StoreId = storeId,
            RegisterId = registerId,
            AppliedAt = appliedAt,
            Units = kv.Value.Units,
            DiscountAmount = Math.Round(kv.Value.Discount, 2, MidpointRounding.AwayFromZero),
        }).ToList();
    }

    /// <summary>The FEFO batch the unit at <paramref name="offset"/> (0-based, in line quantity) came from.</summary>
    private static Guid? BatchAt(List<(Guid BatchId, decimal Quantity)> consumption, decimal offset)
    {
        var acc = 0m;
        foreach (var (batchId, qty) in consumption)
        {
            acc += qty;
            if (offset < acc) return batchId;
        }
        return null;
    }
}
