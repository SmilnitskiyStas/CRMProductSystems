namespace ShelfGuard.Domain.Entities;

public sealed class Tenant
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public string Plan { get; private set; } = "basic";
    public string Modules { get; private set; } = "[]";
    // v4: business domain (retail / auto_service / warehouse / restaurant / production / distribution)
    public string BusinessType { get; private set; } = "retail";
    public bool IsActive { get; private set; } = true;

    /// <summary>Consumer/admin-app branding logo (spec's minimal Tenant shape, TASK-527). Null = no logo uploaded yet.</summary>
    public string? LogoUrl { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public ICollection<User> Users { get; private set; } = new List<User>();

    private Tenant() { }

    public static Tenant Create(string name, string slug)
    {
        var now = DateTime.UtcNow;
        return new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            Slug = slug.ToLowerInvariant(),
            Plan = "basic",
            Modules = "[]",
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>Changes the billing plan (provider-only).</summary>
    public string? UpdatePlan(string plan)
    {
        var valid = new[] { "basic", "standard", "enterprise", "trial" };
        if (!valid.Contains(plan, StringComparer.OrdinalIgnoreCase))
            return $"Unknown plan '{plan}'. Valid: {string.Join(", ", valid)}.";
        Plan = plan.ToLowerInvariant();
        UpdatedAt = DateTime.UtcNow;
        return null;
    }

    /// <summary>Replaces the enabled modules list (provider-only).</summary>
    public string? UpdateModules(IReadOnlyList<string> modules)
    {
        var valid = new[]
        {
            // legacy
            "shelf_manager", "crm", "notifications", "auto_order", "iot", "cv_camera",
            // v4 module-based activation
            "inventory", "procurement", "pos", "auto_service", "production", "marketplace",
            // v4.1 supplier self-service (ADR-016)
            "marketplace_supplier",
            // TASK-405 (loyalty/RFM plan Фаза 0/1): "loyalty" gates consumer accounts, QR
            // bonus accrual/redemption, POS loyalty UI; "marketing_analytics" gates the
            // Фаза 1 RFM dashboard (Features/MarketingAnalytics/, a later task) — key
            // registered now so Tenant.UpdateModules doesn't reject it once that feature
            // lands. Independent of each other: a tenant can run the bonus program without
            // the analytics dashboard.
            "loyalty", "marketing_analytics",
            // TASK-674: "mobile_app" gates the whole "Застосунок" admin section (bonus
            // program settings, loyalty tiers, banners, promo campaigns/discounts, mobile
            // catalog, App Builder) — distinct from "loyalty" which stays scoped to the POS
            // QR accrual/redemption API only. "analytics" gates the "Аналітика" reports/
            // dashboards section (AnalyticsController, per-action — the dashboard-home and
            // Events-calendar endpoints on that controller stay ungated). Neither is in any
            // DefaultModulesForBusinessType set — provider-granted only, no backfill.
            "mobile_app", "analytics",
            // Supplier-portal expansion: "supplier_inventory" gates the supplier's own
            // warehouses + batch stock + batch-consuming shipment (SupplierCabinetWarehouses
            // controller, per-action); "supplier_workforce" gates the supplier's employee
            // work schedules (SupplierCabinetSchedules controller). TASK-693 (Phase 7): both are
            // in DefaultModulesForBusinessType("supplier") and were backfilled onto existing
            // supplier tenants — the legacy confirmed→shipped flow still works with
            // "supplier_inventory" off, but a provider would now have to explicitly remove it.
            "supplier_inventory", "supplier_workforce"
        };
        var unknown = modules.Where(m => !valid.Contains(m, StringComparer.OrdinalIgnoreCase)).ToList();
        if (unknown.Count > 0)
            return $"Unknown modules: {string.Join(", ", unknown)}.";
        Modules = System.Text.Json.JsonSerializer.Serialize(modules);
        UpdatedAt = DateTime.UtcNow;
        return null;
    }

    /// <summary>Sets the business type (provider-only, determines default module set).</summary>
    public string? UpdateBusinessType(string businessType)
    {
        var valid = new[] { "retail", "auto_service", "warehouse", "restaurant", "production", "distribution", "pharmacy", "floristry", "supplier" };
        if (!valid.Contains(businessType, StringComparer.OrdinalIgnoreCase))
            return $"Unknown business type '{businessType}'. Valid: {string.Join(", ", valid)}.";
        BusinessType = businessType.ToLowerInvariant();
        UpdatedAt = DateTime.UtcNow;
        return null;
    }

    /// <summary>Parses the Modules JSON array. Returns an empty list on null/invalid JSON.</summary>
    public IReadOnlyList<string> GetModules()
    {
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<string[]>(Modules) ?? [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>True when the given module key is in the enabled-modules list (case-insensitive).</summary>
    public bool HasModule(string key) =>
        GetModules().Contains(key, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Default module set applied when a tenant is created (ADR-015), keyed by business_type.
    /// Unknown/unmapped business types fall back to a minimal {inventory} set.
    /// </summary>
    public static IReadOnlyList<string> DefaultModulesForBusinessType(string businessType) =>
        businessType.ToLowerInvariant() switch
        {
            "retail"       => ["inventory", "procurement", "pos"],
            "auto_service" => ["auto_service", "procurement"],
            "restaurant"   => ["inventory", "pos", "production"],
            "warehouse"    => ["inventory", "procurement"],
            "production"   => ["inventory", "procurement", "production"],
            "distribution" => ["inventory", "procurement", "marketplace"],
            "pharmacy"     => ["inventory", "procurement", "pos"],
            "floristry"    => ["inventory", "procurement", "pos"],
            // TASK-693 (Phase 7): supplier_inventory + supplier_workforce now ship ON for every
            // supplier tenant (reverses the 2026-09-02 default-off). BackfillSupplierModules
            // merges the two keys into existing supplier tenants.
            "supplier"     => ["marketplace_supplier", "supplier_inventory", "supplier_workforce"],
            _              => ["inventory"],
        };

    /// <summary>Soft-deactivates the tenant (provider-only).</summary>
    public void Deactivate()
    {
        IsActive = false;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Re-activates a previously deactivated tenant (provider-only).</summary>
    public void Activate()
    {
        IsActive = true;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Sets the consumer/admin-app branding logo. Pass null to clear.</summary>
    public void UpdateLogoUrl(string? logoUrl)
    {
        LogoUrl = logoUrl;
        UpdatedAt = DateTime.UtcNow;
    }
}
