namespace ShelfGuard.Domain.Constants;

public static class ProviderPermissions
{
    public const string TeamManagement     = "team_management";
    public const string ViewClients        = "view_clients";
    public const string ManageClients      = "manage_clients";
    public const string ServiceDesk        = "service_desk";
    public const string LiveChat           = "live_chat";
    public const string AdminPanel         = "admin_panel";
    public const string Marketplace        = "marketplace";
    public const string Analytics          = "analytics";
    public const string ScheduleManagement = "schedule_management";

    public static readonly string[] All =
    [
        TeamManagement, ViewClients, ManageClients,
        ServiceDesk, LiveChat, AdminPanel, Marketplace, Analytics, ScheduleManagement,
    ];

    public static readonly IReadOnlyDictionary<string, string[]> SystemRoleDefaults =
        new Dictionary<string, string[]>
        {
            [AppRoles.Provider]      = All,
            [AppRoles.ProviderAdmin] = All,
            [AppRoles.ProviderAgent] = [ViewClients, ServiceDesk, LiveChat],
        };
}
