using System.Reflection;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace ShelfGuard.Api.Infrastructure;

public enum ControllerGroup { Core, Consumer, Provider, Shared }

/// <summary>
/// Stage 3b: one API image, several role instances selected by env API_ROLE
/// (core | consumer | provider | all; default/unknown = all). Controllers outside the active role's
/// allowlist are removed from MVC discovery, so their routes simply 404 on that instance.
/// Single source of truth: <see cref="Classify"/>. Anything not listed is CORE.
/// </summary>
public sealed class RoleControllerFeatureProvider : IApplicationFeatureProvider<ControllerFeature>
{
    public const string All = "all";

    private static readonly HashSet<string> Consumer = new(StringComparer.Ordinal)
    {
        "ConsumerAssistant", "ConsumerAuth", "ConsumerCatalogEvents", "ConsumerContent",
        "ConsumerLoyalty", "ConsumerProfile", "ConsumerPushNotifications", "ConsumerReviews",
        "ConsumerSupport", "MobileAuth", "Retailers",
        "MobileConfig", // only GET api/v1/mobile/config (anonymous public config)
    };

    private static readonly HashSet<string> Provider = new(StringComparer.Ordinal)
    {
        "Admin", "AdminChat", "AdminServiceDesk", "MarketplaceAdmin", "Provider", "ProviderCategories",
        "ProviderLeads", "ProviderRoles", "ProviderSchedule", "ProviderTeam", "PublicLeads",
        "Marketplace", "MarketplaceChat", "MarketplaceCooperation", "SupplierProfileSettings",
    };

    private static readonly HashSet<string> Shared = new(StringComparer.Ordinal) { "Auth", "Geo" };

    private readonly string _role;

    public RoleControllerFeatureProvider(string? role) => _role = Normalize(role);

    public static string Normalize(string? role)
    {
        var r = role?.Trim().ToLowerInvariant();
        return r is "core" or "consumer" or "provider" ? r : All;
    }

    public static ControllerGroup Classify(string controllerTypeName)
    {
        var name = controllerTypeName.EndsWith("Controller", StringComparison.Ordinal)
            ? controllerTypeName[..^"Controller".Length]
            : controllerTypeName;
        if (Shared.Contains(name)) return ControllerGroup.Shared;
        if (Consumer.Contains(name)) return ControllerGroup.Consumer;
        if (Provider.Contains(name) || name.StartsWith("SupplierCabinet", StringComparison.Ordinal))
            return ControllerGroup.Provider;
        return ControllerGroup.Core;
    }

    public static bool IsAllowed(string? role, string controllerTypeName)
    {
        var r = Normalize(role);
        if (r == All) return true;
        var group = Classify(controllerTypeName);
        return group == ControllerGroup.Shared || group.ToString().Equals(r, StringComparison.OrdinalIgnoreCase);
    }

    public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
    {
        if (_role == All) return;
        foreach (var t in feature.Controllers.Where(t => !IsAllowed(_role, t.Name)).ToList())
            feature.Controllers.Remove(t);
    }
}
