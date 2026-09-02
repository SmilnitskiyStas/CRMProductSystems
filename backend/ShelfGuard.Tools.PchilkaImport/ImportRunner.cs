using Microsoft.EntityFrameworkCore;
using ShelfGuard.Application.Features.Catalog;
using ShelfGuard.Application.Features.Catalog.Dtos;
using ShelfGuard.Application.Features.Customers;
using ShelfGuard.Application.Features.Stock;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Tools.PchilkaImport.Source;

namespace ShelfGuard.Tools.PchilkaImport;

/// <summary>
/// TASK-513: populates the local dev "Свіжий Кут" tenant with a real slice of Pchilka POS
/// data (top-selling products, real receipts, real customers) plus synthetic-but-realistic
/// FEFO expiry batches, so FEFO/write-offs/notifications/POS analytics can be clicked through
/// manually against non-trivial data.
///
/// Every Postgres write phase runs inside <see cref="ITenantSessionOverride"/> — the same
/// SET LOCAL app.tenant_id mechanism TASK-417's LoyaltyService.JoinAsync uses for a
/// non-HTTP-context write — because this console app has no HttpContext at all, so
/// TenantConnectionInterceptor always RESETs the RLS session variables on connection open
/// (see its own remarks) and every tenant-scoped table would otherwise return/accept zero
/// rows. product_stock and pos_transactions additionally carry a RESTRICTIVE store_scope
/// policy (ADR from AddLocationStoreScopeRlsPolicies) that ALSO requires app.role to be one
/// of a fixed bypass set — this tool has no logged-in user, so each phase additionally sets
/// app.role='enterprise_admin' for the duration of its own transaction (see
/// SetEnterpriseAdminRoleAsync). Each phase is its own transaction/ExecuteAsync call (not one
/// giant transaction) specifically so a re-run with adjusted scope numbers can pick up from
/// whatever already committed instead of redoing everything.
/// </summary>
public sealed class ImportRunner(
    AppDbContext db,
    ITenantSessionOverride tenantOverride,
    IItemService itemService,
    ICustomerService customerService,
    PchilkaSourceReader source,
    ImportOptions options)
{
    private static readonly string[] FreshKeywords =
        ["молоч", "мясо", "м'ясо", "риба", "рыба", "птиц", "сир", "сыр", "фрукт", "овоч", "овощ", "яйц", "ковбас", "колбас", "зелен"];

    private static readonly string[] ChilledKeywords =
        ["заморож", "морожен", "напівфабрикат", "полуфабрикат", "торт", "кондитер", "десерт"];

    public async Task RunAsync(CancellationToken ct)
    {
        Console.WriteLine("=== TASK-513 Pchilka -> ShelfGuard dev import ===");

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Slug == options.TenantSlug, ct)
            ?? throw new InvalidOperationException(
                $"Tenant with slug '{options.TenantSlug}' not found. Boot the API once against an empty dev DB first so DbSeeder creates it.");
        var tenantId = tenant.Id;
        Console.WriteLine($"Tenant: {tenant.Name} ({tenantId})");

        await EnsureModulesAsync(tenant, ct);

        // Tenants has no RLS (confirmed empirically: relrowsecurity=false in pg_class) — safe to
        // read/write directly. Locations DOES carry FORCE RLS (relrowsecurity AND
        // relforcerowsecurity both true — a grep for a literal "ON locations" migration string
        // missed it because the policy is applied programmatically, not as a one-off SQL
        // literal), so it needs the same tenant override as every other tenant-scoped table.
        //
        // The brief assumed this tenant still has exactly one store (true when it was written);
        // by the time this ran, TASK-501..512's cross-store migration testing had added 4
        // stores total: the original seeded store, a second genuine store ("Подільський"), and
        // two disposable same-timestamp QA fixtures explicitly labelled "QA TASK-504" with zero
        // zones. Pick the oldest non-"QA"-labelled store — the original real one — rather than
        // whatever Postgres happens to return first.
        var (location, zones) = await tenantOverride.ExecuteAsync(tenantId, async () =>
        {
            await SetEnterpriseAdminRoleAsync(ct); // store_scope RESTRICTIVE policy — see helper's remarks
            var loc = await db.Locations
                    .Where(l => l.TenantId == tenantId && !l.Name.Contains("QA"))
                    .OrderBy(l => l.CreatedAt)
                    .FirstOrDefaultAsync(ct)
                ?? await db.Locations.Where(l => l.TenantId == tenantId).OrderBy(l => l.CreatedAt).FirstOrDefaultAsync(ct)
                ?? throw new InvalidOperationException("Tenant has no Location (store) — expected DbSeeder's seeded store.");
            var z = await db.LocationZones.Where(zz => zz.LocationId == loc.Id).ToListAsync(ct);
            return (loc, z);
        }, ct);
        var storeId = location.Id;
        Console.WriteLine($"Store: {location.Name} ({storeId})");

        // ── 1. Extract from Pchilka (read-only MySQL) ────────────────────────
        Console.WriteLine($"Reading top {options.TopProductCount} products for shop {options.ShopCode}, {options.SalesWindowFrom}..{options.SalesWindowTo} ...");
        var topProducts = await source.GetTopProductCodesAsync(
            options.ShopCode, options.SalesWindowFrom, options.SalesWindowTo, options.TopProductCount, ct);
        var products = await source.GetProductCatalogAsync(topProducts, ct);
        var productsByCode = products.ToDictionary(p => p.ProductCode);
        Console.WriteLine($"  {products.Count} products resolved.");

        Console.WriteLine($"Reading orders for shop {options.ShopCode}, {options.ImportWindowFrom}..{options.ImportWindowTo} ...");
        var orders = await source.GetOrdersAsync(
            options.ShopCode, options.ImportWindowFrom, options.ImportWindowTo, productsByCode.Keys.ToList(), ct);
        Console.WriteLine($"  {orders.Count} orders / {orders.Sum(o => o.Lines.Count)} lines resolved.");

        var clientCodes = orders
            .Where(o => o.ClientCode.HasValue)
            .Select(o => o.ClientCode!.Value)
            .Distinct()
            .ToList();
        Console.WriteLine($"  {clientCodes.Count} distinct customers ({orders.Count(o => o.ClientCode.HasValue)} orders carry a real customer link).");

        // ── 2. Postgres: catalog (categories + items) ─────────────────────────
        Console.WriteLine("Importing catalog (categories + items) ...");
        var (categoriesCreated, itemsCreated, itemsReused, productToItemId) = await ImportCatalogAsync(tenantId, products, ct);
        Console.WriteLine($"  categories +{categoriesCreated}, items +{itemsCreated} (reused {itemsReused})");

        // ── 3. Postgres: customers ─────────────────────────────────────────────
        Console.WriteLine("Importing customers ...");
        var (customersCreated, customersReused, clientToCustomerId) = await ImportCustomersAsync(tenantId, clientCodes, ct);
        Console.WriteLine($"  customers +{customersCreated} (reused {customersReused})");

        // ── 4. Postgres: FEFO stock batches ───────────────────────────────────
        Console.WriteLine("Importing FEFO stock batches ...");
        var stock = await ImportStockAsync(tenantId, storeId, zones, productsByCode, productToItemId, ct);
        Console.WriteLine($"  batches +{stock.Created} (near-expiry {stock.Near}, mid {stock.Mid}, far {stock.Far}, expired {stock.Expired})");

        // ── 5. Postgres: POS transactions ─────────────────────────────────────
        Console.WriteLine("Importing POS transactions ...");
        var tx = await ImportTransactionsAsync(
            tenantId, storeId, orders, productToItemId, clientToCustomerId, stock.PrimaryBatchByItemId, ct);
        Console.WriteLine($"  transactions +{tx.Created} (skipped existing {tx.SkippedExisting}, skipped no-lines {tx.OrdersSkippedNoLines}), line items +{tx.ItemsCreated}");

        Console.WriteLine();
        Console.WriteLine("=== SUMMARY ===");
        Console.WriteLine($"Items:         +{itemsCreated} created, {itemsReused} reused ({products.Count} selected)");
        Console.WriteLine($"Categories:    +{categoriesCreated}");
        Console.WriteLine($"Customers:     +{customersCreated} created, {customersReused} reused");
        Console.WriteLine($"Stock batches: +{stock.Created} (near {stock.Near} / mid {stock.Mid} / far {stock.Far} / expired {stock.Expired})");
        Console.WriteLine($"Transactions:  +{tx.Created} (skipped existing {tx.SkippedExisting}, skipped empty {tx.OrdersSkippedNoLines})");
        Console.WriteLine($"Line items:    +{tx.ItemsCreated}");
        Console.WriteLine("Status: OK");
    }

    // ── Phase: tenant modules ───────────────────────────────────────────────

    private async Task EnsureModulesAsync(Tenant tenant, CancellationToken ct)
    {
        var required = new[] { "inventory", "procurement", "pos", "marketing_analytics", "loyalty" };
        var current = tenant.GetModules();
        var merged = current.Union(required, StringComparer.OrdinalIgnoreCase).ToArray();

        var alreadyComplete = required.All(m => current.Contains(m, StringComparer.OrdinalIgnoreCase));
        if (alreadyComplete)
        {
            Console.WriteLine("Modules already enabled, no change.");
            return;
        }

        var error = tenant.UpdateModules(merged);
        if (error is not null)
            throw new InvalidOperationException($"UpdateModules failed: {error}");

        await db.SaveChangesAsync(ct);
        Console.WriteLine($"Modules enabled: {string.Join(", ", merged)}");
    }

    // ── Phase: categories + items ────────────────────────────────────────────

    private async Task<(int CategoriesCreated, int ItemsCreated, int ItemsReused, Dictionary<long, Guid> ProductToItemId)>
        ImportCatalogAsync(Guid tenantId, List<PchilkaProduct> products, CancellationToken ct)
    {
        return await tenantOverride.ExecuteAsync(tenantId, async () =>
        {
            await SetEnterpriseAdminRoleAsync(ct);

            // Categories are global now (B1): match each distinct non-null product-group name
            // to an existing platform_categories row by trimmed, case-insensitive name; create
            // a global row (no TenantId) for any unmatched name.
            var existingCategoryRows = await db.PlatformCategories.ToListAsync(ct);
            var existingCategories = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in existingCategoryRows)
                existingCategories.TryAdd(c.Name.Trim(), c.Id);

            var groupNameToCategoryId = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            var categoriesCreated = 0;
            foreach (var groupName in products
                         .Where(p => !string.IsNullOrWhiteSpace(p.GroupName))
                         .Select(p => p.GroupName!.Trim())
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (existingCategories.TryGetValue(groupName, out var existingId))
                {
                    groupNameToCategoryId[groupName] = existingId;
                    continue;
                }

                var category = new PlatformCategory { Name = groupName, BusinessTypes = [] };
                db.PlatformCategories.Add(category);
                groupNameToCategoryId[groupName] = category.Id;
                categoriesCreated++;
            }
            if (categoriesCreated > 0)
                await db.SaveChangesAsync(ct);

            // Existing items: index by barcode and by normalized name for dedupe on rerun.
            var existingItems = await db.Items.Where(i => i.TenantId == tenantId).ToListAsync(ct);
            var byBarcode = new Dictionary<string, Guid>();
            var byName = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            foreach (var it in existingItems)
            {
                foreach (var bc in it.Barcodes)
                    byBarcode.TryAdd(bc, it.Id);
                byName.TryAdd(it.Name.Trim(), it.Id);
            }

            var productToItemId = new Dictionary<long, Guid>();
            int itemsCreated = 0, itemsReused = 0;

            foreach (var p in products)
            {
                Guid? existingId = null;
                foreach (var bc in p.Barcodes)
                    if (byBarcode.TryGetValue(bc, out var foundId)) { existingId = foundId; break; }
                if (existingId is null && byName.TryGetValue(p.Name.Trim(), out var byNameId))
                    existingId = byNameId;

                if (existingId.HasValue)
                {
                    productToItemId[p.ProductCode] = existingId.Value;
                    itemsReused++;
                    continue;
                }

                var perishability = InferPerishability(p.GroupName);
                var categoryId = p.GroupName is not null && groupNameToCategoryId.TryGetValue(p.GroupName.Trim(), out var catId)
                    ? catId
                    : (Guid?)null;
                var unit = string.IsNullOrWhiteSpace(p.UnitAbbr) ? "шт" : p.UnitAbbr!;
                decimal? priceRetail = p.AvgUnitPrice > 0 ? Math.Round(p.AvgUnitPrice, 2) : null;

                var request = new CreateProductRequest(
                    Name: p.Name,
                    Barcodes: p.Barcodes.Count > 0 ? p.Barcodes : null,
                    CategoryId: categoryId,
                    SegmentId: null,
                    Unit: unit,
                    ManagementType: "MTS",
                    ItemType: "product",
                    MinStock: 0,
                    MaxStock: 0,
                    SafetyBuffer: 0,
                    StorageTempMin: null,
                    StorageTempMax: null,
                    ShelfLifeDays: DefaultShelfLifeDays(perishability),
                    DefaultSupplierId: null,
                    VatRate: p.Vat,
                    // No cost/purchase-price table is in TASK-513's scope — 75% of the
                    // observed average selling price is a cosmetic placeholder, not a real cost.
                    PricePurchase: priceRetail.HasValue ? Math.Round(priceRetail.Value * 0.75m, 2) : null,
                    PriceRetail: priceRetail,
                    ImageUrl: null,
                    Manufacturer: null,
                    CountryOrigin: null,
                    PerishabilityClass: perishability);

                var (dto, error) = await itemService.CreateAsync(tenantId, request, ct);
                if (dto is null)
                {
                    Console.WriteLine($"  [warn] item create failed for product {p.ProductCode} '{p.Name}': {error}");
                    continue;
                }

                productToItemId[p.ProductCode] = dto.Id;
                byName[p.Name.Trim()] = dto.Id;
                foreach (var bc in p.Barcodes)
                    byBarcode.TryAdd(bc, dto.Id);
                itemsCreated++;
            }

            return (categoriesCreated, itemsCreated, itemsReused, productToItemId);
        }, ct);
    }

    // ── Phase: customers ──────────────────────────────────────────────────────

    private async Task<(int Created, int Reused, Dictionary<long, Guid> ClientToCustomerId)>
        ImportCustomersAsync(Guid tenantId, IReadOnlyCollection<long> clientCodes, CancellationToken ct)
    {
        return await tenantOverride.ExecuteAsync(tenantId, async () =>
        {
            await SetEnterpriseAdminRoleAsync(ct);

            var existing = await db.Customers.Where(c => c.TenantId == tenantId).ToListAsync(ct);
            var byTag = new Dictionary<long, Guid>();
            foreach (var c in existing)
            {
                foreach (var tag in c.Tags)
                {
                    if (tag.StartsWith("pchilka:", StringComparison.OrdinalIgnoreCase) &&
                        long.TryParse(tag.AsSpan("pchilka:".Length), out var code))
                    {
                        byTag.TryAdd(code, c.Id);
                    }
                }
            }

            var map = new Dictionary<long, Guid>();
            int created = 0, reused = 0;

            foreach (var code in clientCodes)
            {
                if (byTag.TryGetValue(code, out var existingId))
                {
                    map[code] = existingId;
                    reused++;
                    continue;
                }

                var dto = new CreateCustomerDto(
                    Name: $"Клієнт #{code} (Pchilka)",
                    Phone: null,
                    Email: null,
                    Notes: "Імпортовано з Pchilka POS (TASK-513)",
                    Tags: [$"pchilka:{code}"]);

                var (customer, error) = await customerService.CreateAsync(tenantId, dto, ct);
                if (customer is null)
                {
                    Console.WriteLine($"  [warn] customer create failed for client {code}: {error}");
                    continue;
                }

                map[code] = customer.Id;
                byTag[code] = customer.Id;
                created++;
            }

            return (created, reused, map);
        }, ct);
    }

    // ── Phase: FEFO stock batches ─────────────────────────────────────────────

    private async Task<StockPhaseResult> ImportStockAsync(
        Guid tenantId, Guid storeId, List<LocationZone> zones,
        Dictionary<long, PchilkaProduct> productsByCode, Dictionary<long, Guid> productToItemId,
        CancellationToken ct)
    {
        return await tenantOverride.ExecuteAsync(tenantId, async () =>
        {
            await SetEnterpriseAdminRoleAsync(ct);

            var itemIds = productToItemId.Values.Distinct().ToList();

            var itemsWithStock = (await db.ProductStocks
                    .Where(s => s.TenantId == tenantId && s.StoreId == storeId && itemIds.Contains(s.ProductId))
                    .Select(s => s.ProductId)
                    .Distinct()
                    .ToListAsync(ct))
                .ToHashSet();

            // Zone Type taxonomy in the live data is a superset of what LocationService
            // validates ("shelf, fridge, freezer, display, production, warehouse") — the
            // TASK-501..512 store data actually uses "refrigerated"/"fresh" too. Match both.
            var fridgeZone = zones.FirstOrDefault(z => z.Type is "fridge" or "freezer" or "refrigerated" or "fresh");
            var shelfZone = zones.FirstOrDefault(z => z.Type == "shelf");
            var anyZone = zones.FirstOrDefault();

            int created = 0, near = 0, mid = 0, far = 0, expired = 0;
            var index = 0;
            var pending = new List<ProductStock>();

            foreach (var (productCode, itemId) in productToItemId)
            {
                index++;
                if (itemsWithStock.Contains(itemId)) continue; // already stocked (rerun-safe)
                if (!productsByCode.TryGetValue(productCode, out var product)) continue;

                itemsWithStock.Add(itemId); // guard: two Pchilka codes can map to the same reused Item

                var perishability = InferPerishability(product.GroupName);
                var zone = perishability is PerishabilityClass.Fresh or PerishabilityClass.Chilled
                    ? fridgeZone ?? shelfZone ?? anyZone
                    : shelfZone ?? anyZone;
                var unit = string.IsNullOrWhiteSpace(product.UnitAbbr) ? "шт" : product.UnitAbbr!;

                foreach (var (label, daysLeft) in BuildExpiryPlan(perishability, index))
                {
                    var expiry = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(daysLeft);
                    var qty = RandomQuantity(unit);
                    var lastChecked = DateTime.UtcNow;

                    var batch = new ProductStock
                    {
                        TenantId = tenantId,
                        ProductId = itemId,
                        StoreId = storeId,
                        ZoneId = zone?.Id,
                        BatchNumber = $"PCH-{productCode}-{label}",
                        Quantity = qty,
                        QuantityInitial = qty,
                        ExpiryDate = expiry,
                        SourceType = "import",
                        LastCheckedAt = lastChecked,
                    };
                    batch.Status = StockStatus.Compute(batch.Quantity, batch.ExpiryDate, batch.LastCheckedAt, perishability);
                    pending.Add(batch);

                    created++;
                    switch (label)
                    {
                        case "near": near++; break;
                        case "mid": mid++; break;
                        case "far": far++; break;
                        case "expired": expired++; break;
                    }
                }

                if (pending.Count >= 300)
                {
                    db.ProductStocks.AddRange(pending);
                    await db.SaveChangesAsync(ct);
                    db.ChangeTracker.Clear();
                    pending.Clear();
                }
            }

            if (pending.Count > 0)
            {
                db.ProductStocks.AddRange(pending);
                await db.SaveChangesAsync(ct);
                db.ChangeTracker.Clear();
            }

            // Primary (FEFO-nearest, preferring still-sellable) batch per item, covering both
            // freshly-created and already-existing stock — used to (optionally) link sale lines.
            var allBatches = await db.ProductStocks
                .Where(s => s.TenantId == tenantId && s.StoreId == storeId && itemIds.Contains(s.ProductId))
                .Select(s => new { s.Id, s.ProductId, s.ExpiryDate })
                .ToListAsync(ct);

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var primaryBatch = allBatches
                .GroupBy(b => b.ProductId)
                .ToDictionary(
                    g => g.Key,
                    g => (g.Where(b => b.ExpiryDate > today).OrderBy(b => b.ExpiryDate).FirstOrDefault()
                          ?? g.OrderBy(b => b.ExpiryDate).First()).Id);

            return new StockPhaseResult(created, near, mid, far, expired, primaryBatch);
        }, ct);
    }

    // ── Phase: POS transactions ───────────────────────────────────────────────

    private async Task<TransactionsPhaseResult> ImportTransactionsAsync(
        Guid tenantId, Guid storeId, List<PchilkaOrder> orders,
        Dictionary<long, Guid> productToItemId, Dictionary<long, Guid> clientToCustomerId,
        Dictionary<Guid, Guid> primaryBatchByItemId, CancellationToken ct)
    {
        return await tenantOverride.ExecuteAsync(tenantId, async () =>
        {
            await SetEnterpriseAdminRoleAsync(ct);

            var existingReceiptNumbers = (await db.PosTransactions
                    .Where(t => t.TenantId == tenantId)
                    .Select(t => t.ReceiptNumber)
                    .ToListAsync(ct))
                .ToHashSet(StringComparer.Ordinal);

            int txCreated = 0, txSkippedExisting = 0, lineItemsCreated = 0, ordersSkippedNoLines = 0;
            var pending = new List<PosTransaction>();
            var rnd = Random.Shared;

            foreach (var order in orders)
            {
                var receiptNumber = order.SynthesizedReceiptNumber;
                if (existingReceiptNumbers.Contains(receiptNumber)) { txSkippedExisting++; continue; }

                var lineEntities = new List<PosTransactionItem>();
                foreach (var line in order.Lines)
                {
                    if (line.Quantity <= 0) continue;
                    if (!productToItemId.TryGetValue(line.ProductCode, out var itemId)) continue;

                    var priceRetail = Math.Round(line.UnitPrice, 2);
                    // line_total is already net-of-discount for the WHOLE line (quantity units) —
                    // confirmed against source data (unit_price*quantity - discount_total ==
                    // line_total). PriceFinal is a PER-UNIT price throughout this codebase
                    // (AnalyticsRepository/AudienceBuilderRepository always compute revenue as
                    // PriceFinal * Quantity) — so PriceFinal must be line_total / quantity, NOT
                    // line_total itself, or every revenue figure downstream would be inflated by
                    // an extra factor of Quantity.
                    var priceFinal = line.Quantity != 0 ? Math.Round(line.LineTotal / line.Quantity, 2) : priceRetail;
                    var discountAmount = Math.Max(0, Math.Round(priceRetail - priceFinal, 2));

                    primaryBatchByItemId.TryGetValue(itemId, out var batchId);

                    lineEntities.Add(new PosTransactionItem
                    {
                        ProductId = itemId,
                        ProductStockId = batchId == Guid.Empty ? null : batchId,
                        Quantity = Math.Round(line.Quantity, 2),
                        PriceRetail = priceRetail,
                        DiscountAmount = discountAmount,
                        PriceFinal = priceFinal,
                    });
                }

                if (lineEntities.Count == 0) { ordersSkippedNoLines++; continue; }

                Guid? customerId = order.ClientCode.HasValue && clientToCustomerId.TryGetValue(order.ClientCode.Value, out var cid)
                    ? cid
                    : null;

                var totalAmount = order.OrderTotal.HasValue
                    ? Math.Round(order.OrderTotal.Value, 2)
                    : Math.Round(lineEntities.Sum(i => i.PriceFinal * i.Quantity), 2);

                var tx = new PosTransaction
                {
                    TenantId = tenantId,
                    StoreId = storeId,
                    ReceiptNumber = receiptNumber,
                    // pos_order_payments.payment_type is entirely NULL in this export (no
                    // reference table decodes it either) — synthesized weighted split per brief.
                    PaymentType = rnd.NextDouble() < 0.6 ? "cash" : "card",
                    TotalAmount = totalAmount,
                    Status = "fiscalized",
                    CreatedAt = DateTime.SpecifyKind(order.OrderedAt, DateTimeKind.Utc),
                    CustomerId = customerId,
                };
                foreach (var li in lineEntities)
                    tx.Items.Add(li);

                pending.Add(tx);
                existingReceiptNumbers.Add(receiptNumber); // guard within-run duplicates
                txCreated++;
                lineItemsCreated += lineEntities.Count;

                if (pending.Count >= 250)
                {
                    db.PosTransactions.AddRange(pending);
                    await db.SaveChangesAsync(ct);
                    db.ChangeTracker.Clear();
                    pending.Clear();
                    Console.WriteLine($"  ... {txCreated} transactions written so far");
                }
            }

            if (pending.Count > 0)
            {
                db.PosTransactions.AddRange(pending);
                await db.SaveChangesAsync(ct);
                db.ChangeTracker.Clear();
            }

            return new TransactionsPhaseResult(txCreated, txSkippedExisting, lineItemsCreated, ordersSkippedNoLines);
        }, ct);
    }

    // ── Shared RLS helper ──────────────────────────────────────────────────────

    private async Task SetEnterpriseAdminRoleAsync(CancellationToken ct)
    {
        // store_scope's RESTRICTIVE policy on product_stock/pos_transactions requires app.role
        // to be one of provider/provider_admin/worker/enterprise_admin, OR a matching
        // user_locations row for app.user_id. This tool has no logged-in user/session, so it
        // asserts the same tenant-wide bypass DbSeeder's own enterprise_admin user
        // ("Василь Мороз") would carry — scoped to this transaction only via SET LOCAL, same
        // revert-on-commit-or-rollback discipline as ITenantSessionOverride's own
        // app.tenant_id. Harmless on tables without a role-based policy (items/customers/
        // categories/location_zones) — no policy anywhere checks for this exact value there.
#pragma warning disable EF1002
        await db.Database.ExecuteSqlRawAsync("SET LOCAL app.role = 'enterprise_admin'", ct);
#pragma warning restore EF1002
    }

    // ── Pure helpers ─────────────────────────────────────────────────────────

    private static string InferPerishability(string? groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName)) return PerishabilityClass.Standard;
        var lower = groupName.ToLowerInvariant();
        if (Array.Exists(FreshKeywords, k => lower.Contains(k))) return PerishabilityClass.Fresh;
        if (Array.Exists(ChilledKeywords, k => lower.Contains(k))) return PerishabilityClass.Chilled;
        return PerishabilityClass.Standard;
    }

    private static int DefaultShelfLifeDays(string perishability) => perishability switch
    {
        PerishabilityClass.Fresh => 5,
        PerishabilityClass.Chilled => 14,
        PerishabilityClass.Durable => 365,
        _ => 180,
    };

    /// <summary>
    /// Deliberate expiry mix per item (TASK-513 explicit requirement): a near-expiry batch
    /// (triggers critical/warning notifications, gives write-off testing something to act on),
    /// a mid-range batch, a comfortably-far-future batch, and — for roughly 1 in 8 items — an
    /// already-expired batch too.
    /// </summary>
    private static IEnumerable<(string Label, int DaysLeft)> BuildExpiryPlan(string perishability, int index)
    {
        var (critical, warning) = PerishabilityClass.GetThresholds(perishability);
        var rnd = Random.Shared;

        yield return ("near", rnd.Next(1, critical + 1));

        var midLow = critical + 1;
        var midHigh = Math.Max(midLow, warning);
        yield return ("mid", rnd.Next(midLow, midHigh + 1));

        yield return ("far", warning + rnd.Next(20, 151));

        if (index % 8 == 0)
            yield return ("expired", -rnd.Next(1, 6));
    }

    private static decimal RandomQuantity(string unit) => unit switch
    {
        "кг" or "л" or "г" or "мл" => Math.Round((decimal)(Random.Shared.NextDouble() * 25 + 3), 1),
        _ => Random.Shared.Next(8, 61),
    };
}
