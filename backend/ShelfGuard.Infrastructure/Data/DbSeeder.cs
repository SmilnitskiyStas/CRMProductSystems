using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Infrastructure.Data;

/// <summary>
/// Inserts demo data on first startup. Skips entirely if any tenant already exists.
/// </summary>
public static class DbSeeder
{
    /// <summary>
    /// KI-005 fix: the demo password is no longer a hardcoded bcrypt hash committed to
    /// source control (previously readable by anyone with repo access, incl. git history).
    /// It is now hashed at runtime via <see cref="IPasswordHasher"/>, from
    /// <c>Seed:DefaultPassword</c> config (env var <c>Seed__DefaultPassword</c>). The
    /// "password" fallback below only ever applies when that key is unset — dev-only,
    /// never relied upon in staging/production (SeedAsync itself is gated to
    /// Development/SEED_ON_START=true by KI-006, so this path never runs unattended in prod).
    /// </summary>
    private const string DefaultSeedPassword = "password";

    public static async Task SeedAsync(
        AppDbContext db, IPasswordHasher hasher, IConfiguration? config = null, CancellationToken ct = default)
    {
        if (await db.Tenants.AnyAsync(ct)) return;

        // ── Tenant ────────────────────────────────────────────────────────────
        var tenant = Tenant.Create("Свіжий Кут", "svizhy-kut");
        db.Tenants.Add(tenant);

        // ── Users ─────────────────────────────────────────────────────────────
        var seedPassword = config?["Seed:DefaultPassword"];
        if (string.IsNullOrWhiteSpace(seedPassword))
            seedPassword = DefaultSeedPassword;
        var hash = hasher.Hash(seedPassword);

        var provider  = User.Create(null,        "admin@shelfguard.local", "Admin ShelfGuard",  hash, "provider");
        var entAdmin  = User.Create(tenant.Id,   "ea@demo.local",          "Василь Мороз",      hash, "enterprise_admin");
        var netMgr    = User.Create(tenant.Id,   "netmgr@demo.local",      "Ірина Бондаренко",  hash, "network_manager");
        var manager   = User.Create(tenant.Id,   "manager@demo.local",     "Олена Ткаченко",    hash, "store_manager");
        var keeper    = User.Create(tenant.Id,   "keeper@demo.local",      "Михайло Гринченко", hash, "storekeeper");
        var merch1    = User.Create(tenant.Id,   "merch1@demo.local",      "Дмитро Коваль",     hash, "merchandiser");
        var merch2    = User.Create(tenant.Id,   "merch2@demo.local",      "Аліна Шевченко",    hash, "merchandiser");

        db.Users.AddRange(provider, entAdmin, netMgr, manager, keeper, merch1, merch2);

        // ── Categories ────────────────────────────────────────────────────────
        var catDairy   = new Category { TenantId = tenant.Id, Name = "Молочні продукти" };
        var catVeg     = new Category { TenantId = tenant.Id, Name = "Овочі та фрукти" };
        var catMeat    = new Category { TenantId = tenant.Id, Name = "М'ясо та ковбаса" };
        var catDry     = new Category { TenantId = tenant.Id, Name = "Бакалія" };
        var catDrinks  = new Category { TenantId = tenant.Id, Name = "Напої" };
        var catBread   = new Category { TenantId = tenant.Id, Name = "Хлібобулочні" };

        db.Categories.AddRange(catDairy, catVeg, catMeat, catDry, catDrinks, catBread);

        // ── Supplier ──────────────────────────────────────────────────────────
        var supplier = new Supplier
        {
            TenantId      = tenant.Id,
            Name          = "АТБ Постачання",
            Edrpou        = "12345678",
            ContactPerson = "Петро Іваненко",
            Phone         = "+380671234567",
            Email         = "supply@atb.ua",
            DeliveryDays  = 2,
            PaymentTerms  = "NET30",
        };
        db.Suppliers.Add(supplier);

        // ── Location ──────────────────────────────────────────────────────────
        var store = new Location
        {
            TenantId = tenant.Id,
            Name     = "Магазин №1 — Центральний",
            Address  = "вул. Хрещатик, 1, Київ",
            Type     = "shop",
        };
        db.Locations.Add(store);

        // ── Location Zones ────────────────────────────────────────────────────
        var zoneShelf = new LocationZone
        {
            LocationId   = store.Id,
            Name         = "Стелаж A — Молочні",
            Type         = "shelf",
            ShelvesCount = 4,
            TempMin      = 2,
            TempMax      = 6,
        };
        var zoneFridge = new LocationZone
        {
            LocationId   = store.Id,
            Name         = "Холодильник — М'ясо",
            Type         = "fridge",
            ShelvesCount = 3,
            TempMin      = 0,
            TempMax      = 4,
        };
        var zoneFreezer = new LocationZone
        {
            LocationId   = store.Id,
            Name         = "Морозильна камера",
            Type         = "freezer",
            ShelvesCount = 2,
            TempMin      = -20,
            TempMax      = -15,
        };
        var zoneDry = new LocationZone
        {
            LocationId   = store.Id,
            Name         = "Стелаж B — Бакалія",
            Type         = "shelf",
            ShelvesCount = 6,
        };

        db.LocationZones.AddRange(zoneShelf, zoneFridge, zoneFreezer, zoneDry);

        // ── Catalog Products ──────────────────────────────────────────────────
        var cpMilk    = MakeCatalog(tenant.Id, "4820001234501", "Молоко 2,5% Галичина",         catDairy.Id,  supplier.Id, "л",   18.5m,  24.9m,  30, 120, shelfLife: 7);
        var cpKefir   = MakeCatalog(tenant.Id, "4820001234502", "Кефір 1% Простоквашино",       catDairy.Id,  supplier.Id, "л",   16.0m,  21.5m,  20,  80, shelfLife: 7);
        var cpSour    = MakeCatalog(tenant.Id, "4820001234503", "Сметана 20% Президент",        catDairy.Id,  supplier.Id, "кг",  42.0m,  55.0m,  15,  60, shelfLife: 14);
        var cpButter  = MakeCatalog(tenant.Id, "4820001234504", "Масло вершкове 82,5%",         catDairy.Id,  supplier.Id, "кг", 168.0m, 215.0m,  10,  40, shelfLife: 30);
        var cpCheese  = MakeCatalog(tenant.Id, "4820001234505", "Сир кисломолочний 9% Злагода", catDairy.Id,  supplier.Id, "кг",  85.0m, 110.0m,  12,  50, shelfLife: 10);

        var cpTomato  = MakeCatalog(tenant.Id, "4820001234511", "Помідори cherry",              catVeg.Id,    supplier.Id, "кг",  45.0m,  65.0m,  20,  80, shelfLife: 5);
        var cpCucumb  = MakeCatalog(tenant.Id, "4820001234512", "Огірки свіжі",                 catVeg.Id,    supplier.Id, "кг",  28.0m,  40.0m,  25,  60, shelfLife: 7);
        var cpPepper  = MakeCatalog(tenant.Id, "4820001234513", "Перець болгарський",           catVeg.Id,    supplier.Id, "кг",  55.0m,  79.0m,  15,  40, shelfLife: 7);
        var cpBanana  = MakeCatalog(tenant.Id, "4820001234514", "Банани Dole",                  catVeg.Id,    supplier.Id, "кг",  38.0m,  54.0m,  30,  80, shelfLife: 5);

        var cpChicken = MakeCatalog(tenant.Id, "4820001234521", "Куряче філе охолоджене",       catMeat.Id,   supplier.Id, "кг",  95.0m, 129.0m,  20,  60, shelfLife: 4, tempMin: 0, tempMax: 4);
        var cpPork    = MakeCatalog(tenant.Id, "4820001234522", "Свинина шия охолоджена",       catMeat.Id,   supplier.Id, "кг", 145.0m, 195.0m,  15,  50, shelfLife: 4, tempMin: 0, tempMax: 4);
        var cpSausage = MakeCatalog(tenant.Id, "4820001234523", "Ковбаса Докторська Ятрань",    catMeat.Id,   supplier.Id, "кг", 115.0m, 149.0m,  10,  40, shelfLife: 14, tempMin: 0, tempMax: 6);

        var cpRice    = MakeCatalog(tenant.Id, "4820001234531", "Рис круглозернистий Чумак",    catDry.Id,    supplier.Id, "кг",  22.0m,  32.0m,  50, 200, shelfLife: 730);
        var cpBuckwh  = MakeCatalog(tenant.Id, "4820001234532", "Гречка ядриця Жменька",        catDry.Id,    supplier.Id, "кг",  35.0m,  49.0m,  40, 185, shelfLife: 730);
        var cpPasta   = MakeCatalog(tenant.Id, "4820001234533", "Макарони рожки Barilla",       catDry.Id,    supplier.Id, "кг",  38.0m,  52.0m,  30, 100, shelfLife: 730);
        var cpOil     = MakeCatalog(tenant.Id, "4820001234534", "Олія соняшникова Чумак 1л",    catDry.Id,    supplier.Id, "л",   48.0m,  65.0m,  25,  60, shelfLife: 365);
        var cpSugar   = MakeCatalog(tenant.Id, "4820001234535", "Цукор білий УКРЦУКОР",         catDry.Id,    supplier.Id, "кг",  28.0m,  38.0m,  60, 300, shelfLife: 730);

        var cpWater   = MakeCatalog(tenant.Id, "4820001234541", "Вода Моршинська 1,5л",         catDrinks.Id, supplier.Id, "шт",   8.5m,  14.0m,  48, 150, shelfLife: 365);
        var cpJuice   = MakeCatalog(tenant.Id, "4820001234542", "Сік Садочок яблуко 1л",        catDrinks.Id, supplier.Id, "шт",  22.0m,  31.0m,  24,  40, shelfLife: 365);
        var cpPepsi   = MakeCatalog(tenant.Id, "4820001234543", "Pepsi Cola 2л",                catDrinks.Id, supplier.Id, "шт",  38.0m,  52.0m,  12,   6, shelfLife: 180);

        var cpBread   = MakeCatalog(tenant.Id, "4820001234551", "Хліб Дарницький Київхліб",     catBread.Id,  supplier.Id, "шт",  14.0m,  22.0m,  20,  20, shelfLife: 3);
        var cpBaton   = MakeCatalog(tenant.Id, "4820001234552", "Батон нарізний Кульчицький",   catBread.Id,  supplier.Id, "шт",  11.0m,  17.0m,  15,  15, shelfLife: 3);

        db.Items.AddRange(
            cpMilk, cpKefir, cpSour, cpButter, cpCheese,
            cpTomato, cpCucumb, cpPepper, cpBanana,
            cpChicken, cpPork, cpSausage,
            cpRice, cpBuckwh, cpPasta, cpOil, cpSugar,
            cpWater, cpJuice, cpPepsi,
            cpBread, cpBaton);

        // ── Legacy Products (POC catalog — kept for backward compat) ──────────
        var legacyProducts = new[]
        {
            MakeProduct("MLK-001", "Молоко 2,5% Галичина",         "Молочні",      "л",    18.5m,  24.9m,  30,  120),
            MakeProduct("MLK-002", "Кефір 1% Простоквашино",       "Молочні",      "л",    16.0m,  21.5m,  20,    8),
            MakeProduct("VEG-001", "Помідори cherry",               "Овочі",        "кг",   45.0m,  65.0m,  20,   72),
            MakeProduct("MET-001", "Куряче філе охолоджене",        "М'ясо",        "кг",   95.0m, 129.0m,  20,   14),
            MakeProduct("DRY-001", "Рис круглозернистий Чумак",     "Бакалія",      "кг",   22.0m,  32.0m,  50,  200),
            MakeProduct("DRK-001", "Вода Моршинська 1,5л",          "Напої",        "шт",    8.5m,  14.0m,  48,  144),
            MakeProduct("BRD-001", "Хліб Дарницький Київхліб",      "Хлібобулочні", "шт",   14.0m,  22.0m,  20,    0),
        };
        db.Products.AddRange(legacyProducts);

        // Save everything before creating stock (FK dependencies)
        await db.SaveChangesAsync(ct);

        // ── ProductStock Batches ───────────────────────────────────────────────
        // Reference date: today
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var batches = new List<ProductStock>
        {
            // ── SAFE batches (expiry > 7 days) ─────────────────────────────
            MakeBatch(tenant.Id, cpMilk.Id,    store.Id, zoneShelf.Id,   "BATCH-MLK-001", 48m,  today.AddDays(5),  "safe"),
            MakeBatch(tenant.Id, cpMilk.Id,    store.Id, zoneShelf.Id,   "BATCH-MLK-002", 24m,  today.AddDays(6),  "safe"),
            MakeBatch(tenant.Id, cpKefir.Id,   store.Id, zoneShelf.Id,   "BATCH-KFR-001", 36m,  today.AddDays(5),  "safe"),
            MakeBatch(tenant.Id, cpSour.Id,    store.Id, zoneShelf.Id,   "BATCH-SRC-001", 20m,  today.AddDays(10), "safe"),
            MakeBatch(tenant.Id, cpButter.Id,  store.Id, zoneShelf.Id,   "BATCH-BTR-001", 15m,  today.AddDays(25), "safe"),
            MakeBatch(tenant.Id, cpRice.Id,    store.Id, zoneDry.Id,     "BATCH-RIC-001", 80m,  today.AddDays(700),"safe"),
            MakeBatch(tenant.Id, cpBuckwh.Id,  store.Id, zoneDry.Id,     "BATCH-BCK-001", 60m,  today.AddDays(680),"safe"),
            MakeBatch(tenant.Id, cpPasta.Id,   store.Id, zoneDry.Id,     "BATCH-PST-001", 40m,  today.AddDays(720),"safe"),
            MakeBatch(tenant.Id, cpOil.Id,     store.Id, zoneDry.Id,     "BATCH-OIL-001", 24m,  today.AddDays(350),"safe"),
            MakeBatch(tenant.Id, cpSugar.Id,   store.Id, zoneDry.Id,     "BATCH-SGR-001", 100m, today.AddDays(600),"safe"),
            MakeBatch(tenant.Id, cpWater.Id,   store.Id, zoneDry.Id,     "BATCH-WTR-001", 72m,  today.AddDays(340),"safe"),
            MakeBatch(tenant.Id, cpJuice.Id,   store.Id, zoneDry.Id,     "BATCH-JCE-001", 24m,  today.AddDays(300),"safe"),
            MakeBatch(tenant.Id, cpPepsi.Id,   store.Id, zoneDry.Id,     "BATCH-PSI-001", 6m,   today.AddDays(150),"safe"),
            MakeBatch(tenant.Id, cpBanana.Id,  store.Id, zoneShelf.Id,   "BATCH-BAN-001", 25m,  today.AddDays(4),  "safe"),
            MakeBatch(tenant.Id, cpSausage.Id, store.Id, zoneFridge.Id,  "BATCH-SSG-001", 12m,  today.AddDays(10), "safe"),

            // ── WARNING batches (expiry 3–7 days) ──────────────────────────
            MakeBatch(tenant.Id, cpMilk.Id,    store.Id, zoneShelf.Id,   "BATCH-MLK-003", 12m,  today.AddDays(3),  "warning"),
            MakeBatch(tenant.Id, cpKefir.Id,   store.Id, zoneShelf.Id,   "BATCH-KFR-002", 8m,   today.AddDays(4),  "warning"),
            MakeBatch(tenant.Id, cpCheese.Id,  store.Id, zoneShelf.Id,   "BATCH-CHS-001", 10m,  today.AddDays(3),  "warning"),
            MakeBatch(tenant.Id, cpTomato.Id,  store.Id, zoneShelf.Id,   "BATCH-TMT-001", 15m,  today.AddDays(2),  "warning"),
            MakeBatch(tenant.Id, cpCucumb.Id,  store.Id, zoneShelf.Id,   "BATCH-CUC-001", 20m,  today.AddDays(3),  "warning"),
            MakeBatch(tenant.Id, cpChicken.Id, store.Id, zoneFridge.Id,  "BATCH-CHK-001", 8m,   today.AddDays(2),  "warning"),
            MakeBatch(tenant.Id, cpPork.Id,    store.Id, zoneFridge.Id,  "BATCH-PRK-001", 5m,   today.AddDays(3),  "warning"),
            MakeBatch(tenant.Id, cpBread.Id,   store.Id, zoneShelf.Id,   "BATCH-BRD-001", 10m,  today.AddDays(1),  "warning"),

            // ── CRITICAL batches (expiry today or 1 day) ───────────────────
            MakeBatch(tenant.Id, cpMilk.Id,    store.Id, zoneShelf.Id,   "BATCH-MLK-004", 6m,   today.AddDays(1),  "critical"),
            MakeBatch(tenant.Id, cpSour.Id,    store.Id, zoneShelf.Id,   "BATCH-SRC-002", 4m,   today,             "critical"),
            MakeBatch(tenant.Id, cpTomato.Id,  store.Id, zoneShelf.Id,   "BATCH-TMT-002", 8m,   today.AddDays(1),  "critical"),
            MakeBatch(tenant.Id, cpChicken.Id, store.Id, zoneFridge.Id,  "BATCH-CHK-002", 3m,   today,             "critical"),
            MakeBatch(tenant.Id, cpBaton.Id,   store.Id, zoneShelf.Id,   "BATCH-BTN-001", 5m,   today,             "critical"),
            MakeBatch(tenant.Id, cpPepper.Id,  store.Id, zoneShelf.Id,   "BATCH-PPR-001", 6m,   today.AddDays(1),  "critical"),

            // ── EXPIRED batches (expiry < today) ───────────────────────────
            MakeBatch(tenant.Id, cpMilk.Id,    store.Id, zoneShelf.Id,   "BATCH-MLK-005", 3m,   today.AddDays(-1), "expired"),
            MakeBatch(tenant.Id, cpKefir.Id,   store.Id, zoneShelf.Id,   "BATCH-KFR-003", 2m,   today.AddDays(-2), "expired"),
            MakeBatch(tenant.Id, cpChicken.Id, store.Id, zoneFridge.Id,  "BATCH-CHK-003", 2m,   today.AddDays(-1), "expired"),
            MakeBatch(tenant.Id, cpBread.Id,   store.Id, zoneShelf.Id,   "BATCH-BRD-002", 4m,   today.AddDays(-1), "expired"),
            MakeBatch(tenant.Id, cpTomato.Id,  store.Id, zoneShelf.Id,   "BATCH-TMT-003", 5m,   today.AddDays(-3), "expired"),
            MakeBatch(tenant.Id, cpCucumb.Id,  store.Id, zoneShelf.Id,   "BATCH-CUC-002", 3m,   today.AddDays(-2), "expired"),
        };

        db.ProductStocks.AddRange(batches);
        await db.SaveChangesAsync(ct);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Item MakeCatalog(
        Guid tenantId, string barcode, string name,
        Guid categoryId, Guid supplierId, string unit,
        decimal pricePurchase, decimal priceRetail,
        decimal minStock, decimal maxStock,
        int shelfLife,
        decimal? tempMin = null, decimal? tempMax = null)
    {
        return new Item
        {
            TenantId          = tenantId,
            Barcodes          = string.IsNullOrEmpty(barcode) ? [] : [barcode],
            Name              = name,
            CategoryId        = categoryId,
            DefaultSupplierId = supplierId,
            Unit              = unit,
            PricePurchase     = pricePurchase,
            PriceRetail       = priceRetail,
            MinStock          = minStock,
            MaxStock          = maxStock,
            ShelfLifeDays     = shelfLife,
            StorageTempMin    = tempMin,
            StorageTempMax    = tempMax,
            ManagementType    = "MTS",
            VatRate           = 20,
        };
    }

    private static ProductStock MakeBatch(
        Guid tenantId, Guid productId, Guid storeId, Guid zoneId,
        string batchNumber, decimal qty, DateOnly expiryDate, string status)
    {
        return new ProductStock
        {
            TenantId        = tenantId,
            ProductId       = productId,
            StoreId         = storeId,
            ZoneId          = zoneId,
            BatchNumber     = batchNumber,
            Quantity        = qty,
            QuantityInitial = qty,
            ExpiryDate      = expiryDate,
            Status          = status,
            SourceType      = "seed",
            LastCheckedAt   = DateTime.UtcNow,
        };
    }

    private static Product MakeProduct(
        string sku, string name, string category, string unit,
        decimal cost, decimal sale, decimal reorder, decimal stock)
    {
        var p = Product.Create(sku, name, null, category, unit, cost, sale, reorder);
        p.AdjustStock(stock);
        return p;
    }
}
