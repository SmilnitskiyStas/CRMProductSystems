using ShelfGuard.Application.Common;
using ShelfGuard.Application.Features.Catalog.Dtos;
using ShelfGuard.Application.Features.Stock;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Catalog;

public sealed class ItemService : IItemService
{
    private readonly IItemRepository _repo;
    private readonly ICategoryRepository _categoryRepo;

    public ItemService(IItemRepository repo, ICategoryRepository categoryRepo)
    {
        _repo = repo;
        _categoryRepo = categoryRepo;
    }

    public async Task<List<ItemDto>> GetAllAsync(
        Guid tenantId,
        Guid? categoryId,
        Guid? segmentId,
        string? managementType,
        bool? uncategorized = null,
        CancellationToken ct = default)
    {
        var products = await _repo.GetAllAsync(categoryId, segmentId, managementType, uncategorized, ct);
        var promo = await LoadPromoStatesAsync(products, ct);
        return products.Select(p => ToDto(p, promo)).ToList();
    }

    // Slice 3: one extra query per catalog page for the promo highlight.
    private const int PromoUpcomingWithinDays = 14;

    private async Task<IReadOnlyDictionary<Guid, ItemPromoInfo>> LoadPromoStatesAsync(
        IReadOnlyCollection<Item> products, CancellationToken ct)
    {
        if (products.Count == 0) return new Dictionary<Guid, ItemPromoInfo>();
        var states = await _repo.GetPromoStatesAsync(products.Select(p => p.Id).ToList(), PromoUpcomingWithinDays, ct);
        return states ?? new Dictionary<Guid, ItemPromoInfo>();
    }

    public async Task<PagedResult<ItemDto>> GetPagedAsync(
        Guid tenantId,
        Guid? categoryId,
        Guid? segmentId,
        string? managementType,
        string? search,
        IReadOnlyList<Guid>? ids,
        string? sortBy,
        bool? sortDescending,
        int page,
        int pageSize,
        decimal? minPrice = null,
        decimal? maxPrice = null,
        bool? uncategorized = null,
        CancellationToken ct = default)
    {
        var (products, total) = await _repo.GetPagedAsync(
            categoryId, segmentId, managementType, search, ids, sortBy, sortDescending, page, pageSize,
            minPrice, maxPrice, uncategorized, ct);
        var promo = await LoadPromoStatesAsync(products, ct);
        return new PagedResult<ItemDto>
        {
            Items = products.Select(p => ToDto(p, promo)).ToList(),
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
        };
    }

    public async Task<ItemDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var product = await _repo.GetByIdAsync(id, ct);
        return product is null ? null : ToDto(product);
    }

    public async Task<ItemDto?> GetByBarcodeAsync(string barcode, CancellationToken ct = default)
    {
        var product = await _repo.GetByBarcodeAsync(barcode, ct);
        return product is null ? null : ToDto(product);
    }

    public async Task<(ItemDto? Product, string? Error)> CreateAsync(
        Guid tenantId,
        CreateProductRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return (null, "Product name is required.");

        if (!IsValidManagementType(request.ManagementType))
            return (null, $"Invalid management type '{request.ManagementType}'. Valid values: MTS, MTO, NA, NM.");

        if (!string.IsNullOrWhiteSpace(request.ItemType) && !IsValidItemType(request.ItemType))
            return (null, $"Invalid item type '{request.ItemType}'. Valid values: product, service, spare_part, consumable, raw_material, kit, packaging.");

        if (!string.IsNullOrWhiteSpace(request.PerishabilityClass) && !PerishabilityClass.IsValid(request.PerishabilityClass))
            return (null, $"Invalid perishability class '{request.PerishabilityClass}'. Valid values: fresh, chilled, standard, durable.");

        // B2: category now points at the global platform_categories catalogue — reject an id
        // that doesn't resolve to an active row (a soft-deleted / unknown category).
        if (request.CategoryId is Guid createCatId && !await _categoryRepo.ActiveExistsAsync(createCatId, ct))
            return (null, "Category not found or inactive.");

        var product = new Item
        {
            TenantId = tenantId,
            Name = request.Name.Trim(),
            Barcodes = NormalizeBarcodes(request.Barcodes),
            CategoryId = request.CategoryId,
            SegmentId = request.SegmentId,
            Unit = string.IsNullOrWhiteSpace(request.Unit) ? "шт" : request.Unit.Trim(),
            ManagementType = request.ManagementType.ToUpperInvariant(),
            ItemType = string.IsNullOrWhiteSpace(request.ItemType) ? "product" : request.ItemType.Trim(),
            MinStock = request.MinStock,
            MaxStock = request.MaxStock,
            SafetyBuffer = request.SafetyBuffer,
            StorageTempMin = request.StorageTempMin,
            StorageTempMax = request.StorageTempMax,
            ShelfLifeDays = request.ShelfLifeDays,
            DefaultSupplierId = request.DefaultSupplierId,
            VatRate = request.VatRate,
            PricePurchase = request.PricePurchase,
            PriceRetail = request.PriceRetail,
            ImageUrl = request.ImageUrl,
            Manufacturer = request.Manufacturer,
            CountryOrigin = request.CountryOrigin,
            PerishabilityClass = request.PerishabilityClass ?? "standard",
            SourceSupplierItemId = request.SourceSupplierItemId,
        };

        await _repo.AddAsync(product, ct);
        await _repo.SaveChangesAsync(ct);

        return (ToDto(product), null);
    }

    public async Task<(ItemDto? Product, string? Error)> UpdateAsync(
        Guid id,
        UpdateProductRequest request,
        CancellationToken ct = default)
    {
        var product = await _repo.GetByIdAsync(id, ct);
        if (product is null)
            return (null, "Product not found.");

        if (string.IsNullOrWhiteSpace(request.Name))
            return (null, "Product name is required.");

        if (!IsValidManagementType(request.ManagementType))
            return (null, $"Invalid management type '{request.ManagementType}'. Valid values: MTS, MTO, NA, NM.");

        if (!string.IsNullOrWhiteSpace(request.ItemType) && !IsValidItemType(request.ItemType))
            return (null, $"Invalid item type '{request.ItemType}'. Valid values: product, service, spare_part, consumable, raw_material, kit, packaging.");

        if (!string.IsNullOrWhiteSpace(request.PerishabilityClass) && !PerishabilityClass.IsValid(request.PerishabilityClass))
            return (null, $"Invalid perishability class '{request.PerishabilityClass}'. Valid values: fresh, chilled, standard, durable.");

        // B2: category now points at the global platform_categories catalogue — reject a *newly
        // assigned* id that doesn't resolve to an active row. An unchanged category is grandfathered:
        // the provider may have retagged or soft-deleted it after the item was linked, and that must
        // not block every future save of an otherwise-valid item (QA BUG-1).
        if (request.CategoryId is Guid updateCatId
            && updateCatId != product.CategoryId
            && !await _categoryRepo.ActiveExistsAsync(updateCatId, ct))
            return (null, "Category not found or inactive.");

        product.Name = request.Name.Trim();
        product.Barcodes = NormalizeBarcodes(request.Barcodes);
        product.CategoryId = request.CategoryId;
        product.SegmentId = request.SegmentId;
        product.Unit = string.IsNullOrWhiteSpace(request.Unit) ? "шт" : request.Unit.Trim();
        product.ManagementType = request.ManagementType.ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(request.ItemType))
            product.ItemType = request.ItemType.Trim();
        product.MinStock = request.MinStock;
        product.MaxStock = request.MaxStock;
        product.SafetyBuffer = request.SafetyBuffer;
        product.StorageTempMin = request.StorageTempMin;
        product.StorageTempMax = request.StorageTempMax;
        product.ShelfLifeDays = request.ShelfLifeDays;
        product.DefaultSupplierId = request.DefaultSupplierId;
        product.VatRate = request.VatRate;
        product.PricePurchase = request.PricePurchase;
        product.PriceRetail = request.PriceRetail;
        product.ImageUrl = request.ImageUrl;
        product.IsActive = request.IsActive;
        product.Manufacturer = request.Manufacturer;
        product.CountryOrigin = request.CountryOrigin;
        product.PerishabilityClass = request.PerishabilityClass ?? "standard";

        _repo.Update(product);
        await _repo.SaveChangesAsync(ct);

        return (ToDto(product), null);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var product = await _repo.GetByIdAsync(id, ct);
        if (product is null) return false;

        product.IsActive = false;
        _repo.Update(product);
        await _repo.SaveChangesAsync(ct);

        return true;
    }

    public async Task<List<ProductSupplierSettingDto>> GetSuppliersAsync(Guid productId, CancellationToken ct = default)
    {
        var settings = await _repo.GetSupplierSettingsAsync(productId, ct);
        return settings.Select(ToSupplierDto).ToList();
    }

    public async Task<(ProductSupplierSettingDto? Setting, string? Error)> AddSupplierAsync(
        Guid productId,
        Guid tenantId,
        AddProductSupplierRequest request,
        CancellationToken ct = default)
    {
        var product = await _repo.GetByIdAsync(productId, ct);
        if (product is null)
            return (null, "Product not found.");

        var exists = await _repo.SupplierSettingExistsAsync(productId, request.SupplierId, ct);
        if (exists)
            return (null, "Supplier setting for this product already exists.");

        if (request.Moq <= 0)
            return (null, "MOQ must be greater than 0.");

        if (request.Usq <= 0)
            return (null, "USQ must be greater than 0.");

        var setting = new ProductSupplierSetting
        {
            TenantId = tenantId,
            ProductId = productId,
            SupplierId = request.SupplierId,
            Moq = request.Moq,
            Usq = request.Usq,
            PricePurchase = request.PricePurchase,
            DeliveryDays = request.DeliveryDays,
            IsPrimary = request.IsPrimary,
        };

        await _repo.AddSupplierSettingAsync(setting, ct);
        await _repo.SaveChangesAsync(ct);

        // Reload with navigation to get supplier name
        var settings = await _repo.GetSupplierSettingsAsync(productId, ct);
        var created = settings.FirstOrDefault(s => s.Id == setting.Id);

        return (created is null ? ToSupplierDtoMinimal(setting) : ToSupplierDto(created), null);
    }

    public async Task<BarcodeProductLookupDto?> LookupByBarcodeExternalAsync(string barcode, CancellationToken ct)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Add("User-Agent", "ShelfGuard/1.0 (contact@shelfguard.app)");

        var url = $"https://world.openfoodfacts.org/api/v0/product/{barcode}.json";
        var resp = await http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode) return null;

        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Check if product was found (status: 1 = found, 0 = not found)
        if (!root.TryGetProperty("status", out var status) || status.GetInt32() != 1)
            return null;

        var p = root.GetProperty("product");

        // Name: prefer Ukrainian, then generic
        var name = GetString(p, "product_name_uk")
                ?? GetString(p, "product_name")
                ?? GetString(p, "product_name_en")
                ?? "Невідомий продукт";

        // Brand
        var brand = GetString(p, "brands");

        // Manufacturer — from "manufacturing_places" or "manufacturers_tags"
        string? manufacturer = null;
        if (p.TryGetProperty("manufacturing_places", out var mfgEl) && mfgEl.ValueKind == System.Text.Json.JsonValueKind.String)
            manufacturer = mfgEl.GetString();

        // Country — from countries_tags, take first "en:ukraine" or first entry
        string? country = null;
        if (p.TryGetProperty("countries_tags", out var ct2) && ct2.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var c in ct2.EnumerateArray())
            {
                var cv = c.GetString() ?? "";
                if (cv.Contains("ukraine", StringComparison.OrdinalIgnoreCase)) { country = "Україна"; break; }
            }
            if (country is null)
            {
                var first = ct2.EnumerateArray().FirstOrDefault();
                if (first.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var raw = first.GetString() ?? "";
                    country = raw.Contains(':') ? raw.Split(':')[1] : raw;
                }
            }
        }

        // Image URL
        var imageUrl = GetString(p, "image_url") ?? GetString(p, "image_front_url");

        // Unit — from quantity field e.g. "100 g" or "1 L"
        var unit = "шт";
        var qty = GetString(p, "quantity");
        if (qty is not null)
        {
            if (qty.Contains('г') || qty.Contains('g', StringComparison.OrdinalIgnoreCase)) unit = "г";
            else if (qty.Contains('л') || qty.Contains('l', StringComparison.OrdinalIgnoreCase)) unit = "л";
            else if (qty.Contains("мл") || qty.Contains("ml", StringComparison.OrdinalIgnoreCase)) unit = "мл";
            else if (qty.Contains("кг") || qty.Contains("kg", StringComparison.OrdinalIgnoreCase)) unit = "кг";
        }

        return new BarcodeProductLookupDto(
            Name:          name,
            Barcodes:      [barcode],
            Brand:         brand,
            Manufacturer:  manufacturer,
            CountryOrigin: country,
            ImageUrl:      imageUrl,
            ShelfLifeDays: null,
            Unit:          unit
        );
    }

    public async Task<(string? Url, string? Error)> UploadImageAsync(Guid itemId, Stream imageStream, string fileName, CancellationToken ct)
    {
        var item = await _repo.GetByIdAsync(itemId, ct);
        if (item is null) return (null, "Product not found.");

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var allowed = new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
        if (!allowed.Contains(ext)) return (null, "Unsupported image format.");

        var uploadDir = Path.Combine("wwwroot", "uploads", "items");
        Directory.CreateDirectory(uploadDir);

        var fileNameOnDisk = $"{itemId}{ext}";
        var filePath = Path.Combine(uploadDir, fileNameOnDisk);

        using var fs = new FileStream(filePath, FileMode.Create);
        await imageStream.CopyToAsync(fs, ct);

        var url = $"/uploads/items/{fileNameOnDisk}";
        item.ImageUrl = url;
        _repo.Update(item);
        await _repo.SaveChangesAsync(ct);

        return (url, null);
    }

    /// <summary>
    /// Trims, drops blanks and de-duplicates barcodes while preserving order. The first
    /// surviving entry is the primary/active barcode — POS, receipts and analytics all read
    /// <c>Barcodes[0]</c>, and the catalog form's "make primary" action reorders the list.
    /// </summary>
    private static List<string> NormalizeBarcodes(IEnumerable<string>? barcodes)
    {
        if (barcodes is null) return [];

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var raw in barcodes)
        {
            var b = raw?.Trim();
            if (string.IsNullOrEmpty(b)) continue;
            if (seen.Add(b)) result.Add(b);
        }
        return result;
    }

    private static string? GetString(System.Text.Json.JsonElement el, string prop)
    {
        if (el.TryGetProperty(prop, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            var s = v.GetString();
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }
        return null;
    }

    // ── mapping ────────────────────────────────────────────────────────────

    private static ItemDto ToDto(Item p) => ToDto(p, promoStates: null);

    private static ItemDto ToDto(Item p, IReadOnlyDictionary<Guid, ItemPromoInfo>? promoStates)
    {
        var promo = promoStates is not null && promoStates.TryGetValue(p.Id, out var pi) ? pi : null;
        return new(
            p.Id,
            p.Barcodes,
            p.Name,
            p.CategoryId,
            p.Category?.Name,
            p.SegmentId,
            p.Segment?.Name,
            p.Unit,
            p.ManagementType,
            p.ItemType,
            p.MinStock,
            p.MaxStock,
            p.SafetyBuffer,
            p.StorageTempMin,
            p.StorageTempMax,
            p.ShelfLifeDays,
            p.DefaultSupplierId,
            p.DefaultSupplier?.Name,
            p.VatRate,
            p.PricePurchase,
            p.PriceRetail,
            p.DefaultReimbursementType,
            p.DefaultReimbursementValue,
            p.ImageUrl,
            p.IsActive,
            p.CreatedAt,
            p.Manufacturer,
            p.CountryOrigin,
            p.PerishabilityClass,
            promo?.State,
            promo?.StartsAt,
            promo?.DiscountPercent);
    }

    private static ProductSupplierSettingDto ToSupplierDto(ProductSupplierSetting s) => new(
        s.Id,
        s.SupplierId,
        s.Supplier?.Name ?? string.Empty,
        s.Moq,
        s.Usq,
        s.PricePurchase,
        s.DeliveryDays,
        s.IsPrimary,
        s.IsActive
    );

    private static ProductSupplierSettingDto ToSupplierDtoMinimal(ProductSupplierSetting s) => new(
        s.Id,
        s.SupplierId,
        string.Empty,
        s.Moq,
        s.Usq,
        s.PricePurchase,
        s.DeliveryDays,
        s.IsPrimary,
        s.IsActive
    );

    private static bool IsValidManagementType(string type) =>
        type is "MTS" or "MTO" or "NA" or "NM";

    private static bool IsValidItemType(string type) =>
        // "packaging" added TASK-406 (marketing analytics): lets a tenant tag pallets/boxes/
        // crates as items so Receipts/Transfers can track them, while
        // MarketingAnalyticsRepository excludes ItemType="packaging" from top-product/
        // affinity/basket aggregation (RFM_ANALYSIS.md §11.2 — "packaging" is never a real
        // purchase-intent product line).
        type is "product" or "service" or "spare_part" or "consumable" or "raw_material" or "kit" or "packaging";
}
