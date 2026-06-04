using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Infrastructure.Data;

/// <summary>
/// Inserts demo data on first startup. Skips entirely if any tenant already exists.
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        if (await db.Tenants.AnyAsync(ct)) return;

        // ── Tenant ────────────────────────────────────────────────────────────
        var tenant = Tenant.Create("Свіжий Кут", "svizhy-kut");
        db.Tenants.Add(tenant);

        // ── Users ─────────────────────────────────────────────────────────────
        // password for all demo users: password
        const string hash = "$2a$12$eumpgqTete7WjCGRn2h1vedr0qXfTCjkKvYNowxeogQNGBEadHHUa";

        // Provider super-admin (no tenant)
        var admin   = User.Create(null,       "admin@shelfguard.local", "Admin User",       hash, "provider");
        var manager = User.Create(tenant.Id,  "manager@demo.local",     "Олена Ткаченко",   hash, "store_manager");
        var merch1  = User.Create(tenant.Id,  "merch1@demo.local",      "Дмитро Коваль",    hash, "merchandiser");
        var merch2  = User.Create(tenant.Id,  "merch2@demo.local",      "Аліна Шевченко",   hash, "merchandiser");

        db.Users.AddRange(admin, manager, merch1, merch2);

        // ── Products ──────────────────────────────────────────────────────────
        var products = new[]
        {
            // Молочні продукти
            MakeProduct("MLK-001", "Молоко 2,5% Галичина",          "Молочні",    "л",    18.5m,  24.9m,  30,  120),
            MakeProduct("MLK-002", "Кефір 1% Простоквашино",        "Молочні",    "л",    16.0m,  21.5m,  20,    8),
            MakeProduct("MLK-003", "Сметана 20% Президент",         "Молочні",    "кг",   42.0m,  55.0m,  15,    5),
            MakeProduct("MLK-004", "Масло вершкове 82,5% Клуб",     "Молочні",    "кг",  168.0m, 215.0m,  10,    0),
            MakeProduct("MLK-005", "Сир кисломолочний 9% Злагода",  "Молочні",    "кг",   85.0m, 110.0m,  12,    4),

            // Овочі та фрукти
            MakeProduct("VEG-001", "Помідори cherry",               "Овочі",      "кг",   45.0m,  65.0m,  20,   72),
            MakeProduct("VEG-002", "Огірки свіжі",                  "Овочі",      "кг",   28.0m,  40.0m,  25,   18),
            MakeProduct("VEG-003", "Перець болгарський",            "Овочі",      "кг",   55.0m,  79.0m,  15,    0),
            MakeProduct("VEG-004", "Банани Dole",                   "Фрукти",     "кг",   38.0m,  54.0m,  30,   45),
            MakeProduct("VEG-005", "Яблука Голден",                 "Фрукти",     "кг",   32.0m,  48.0m,  40,    6),

            // М'ясо
            MakeProduct("MET-001", "Куряче філе охолоджене",        "М'ясо",      "кг",   95.0m, 129.0m,  20,   14),
            MakeProduct("MET-002", "Свинина шия охолоджена",        "М'ясо",      "кг",  145.0m, 195.0m,  15,    0),
            MakeProduct("MET-003", "Ковбаса Докторська Ятрань",     "М'ясо",      "кг",  115.0m, 149.0m,  10,    3),

            // Бакалія
            MakeProduct("DRY-001", "Рис круглозернистий Чумак",     "Бакалія",    "кг",   22.0m,  32.0m,  50,  200),
            MakeProduct("DRY-002", "Гречка ядриця Жменька",         "Бакалія",    "кг",   35.0m,  49.0m,  40,  185),
            MakeProduct("DRY-003", "Макарони рожки Barilla",        "Бакалія",    "кг",   38.0m,  52.0m,  30,   96),
            MakeProduct("DRY-004", "Олія соняшникова Чумак 1л",     "Бакалія",    "л",    48.0m,  65.0m,  25,   60),
            MakeProduct("DRY-005", "Цукор білий УКРЦУКОР",          "Бакалія",    "кг",   28.0m,  38.0m,  60,  310),

            // Напої
            MakeProduct("DRK-001", "Вода Моршинська 1,5л",          "Напої",      "шт",    8.5m,  14.0m,  48,  144),
            MakeProduct("DRK-002", "Сік Садочок яблуко 1л",         "Напої",      "шт",   22.0m,  31.0m,  24,   36),
            MakeProduct("DRK-003", "Pepsi Cola 2л",                  "Напої",      "шт",   38.0m,  52.0m,  12,    5),

            // Хліб
            MakeProduct("BRD-001", "Хліб Дарницький Київхліб",      "Хлібобулочні","шт",  14.0m,  22.0m,  20,    0),
            MakeProduct("BRD-002", "Батон нарізний Кульчицький",    "Хлібобулочні","шт",  11.0m,  17.0m,  15,    4),
        };

        db.Products.AddRange(products);
        await db.SaveChangesAsync(ct);
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
