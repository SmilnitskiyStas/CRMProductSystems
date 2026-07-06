namespace ShelfGuard.Domain.Constants;

public static class SupplierPermissions
{
    public const string CatalogManagement = "catalog_management";
    public const string ClientReviews      = "client_reviews";
    public const string TaskBoard          = "task_board";
    public const string StaffManagement    = "staff_management";
    public const string ProfileManagement  = "profile_management";
    public const string ClientManagement   = "client_management";

    public static readonly string[] All =
    [
        CatalogManagement, ClientReviews, TaskBoard, StaffManagement, ProfileManagement,
        ClientManagement,
    ];
}
