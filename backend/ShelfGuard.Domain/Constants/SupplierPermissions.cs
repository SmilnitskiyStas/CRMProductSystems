namespace ShelfGuard.Domain.Constants;

public static class SupplierPermissions
{
    public const string CatalogManagement = "catalog_management";
    public const string ClientReviews      = "client_reviews";
    public const string TaskBoard          = "task_board";
    public const string StaffManagement    = "staff_management";
    public const string ProfileManagement  = "profile_management";
    public const string ClientManagement   = "client_management";

    // ── Supplier-portal expansion ─────────────────────────────────────────────
    /// <summary>Manage the supplier's own warehouses, batch stock and batch-consuming shipment (gated by the "supplier_inventory" module).</summary>
    public const string WarehouseManagement = "warehouse_management";
    /// <summary>Manage employee work schedules for the supplier's warehouses (gated by the "supplier_workforce" module).</summary>
    public const string WorkforceManagement = "workforce_management";
    /// <summary>View the supplier's own demand/sales analytics.</summary>
    public const string AnalyticsView = "analytics_view";

    public static readonly string[] All =
    [
        CatalogManagement, ClientReviews, TaskBoard, StaffManagement, ProfileManagement,
        ClientManagement, WarehouseManagement, WorkforceManagement, AnalyticsView,
    ];
}
