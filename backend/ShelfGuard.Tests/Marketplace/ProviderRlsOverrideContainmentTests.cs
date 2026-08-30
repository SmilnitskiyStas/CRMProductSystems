using System.Reflection;
using ShelfGuard.Application.Services;
using ShelfGuard.Infrastructure.Data.Repositories;
using Xunit;

namespace ShelfGuard.Tests.Marketplace;

/// <summary>
/// TASK-643/KI-036, required change R4 of the TASK-641 threat model.
///
/// <see cref="IProviderRlsOverride"/> sets Postgres <c>app.role = 'provider'</c>, and 107 tables
/// carry a <c>provider_bypass</c> policy that is PERMISSIVE <c>FOR ALL</c> with
/// <c>WITH CHECK</c> defaulting to its <c>USING</c> — so inside such a block the connection has a
/// full cross-tenant READ AND WRITE bypass over essentially the whole schema. The only thing
/// keeping that acceptable is containment: exactly one type may take this dependency, and each of
/// its blocks wraps one self-contained repository operation.
///
/// Containment is a convention, not a language feature, so this test pins it. If it fails,
/// something outside MarketplaceRepository has acquired a cross-tenant RLS bypass — do not "fix"
/// it by extending the allow-list without re-deriving IProviderRlsOverride's security contract
/// from scratch and updating ADR-035.
/// </summary>
public sealed class ProviderRlsOverrideContainmentTests
{
    /// <summary>
    /// The single type permitted to depend on the provider bypass. Deliberately a one-element
    /// set: adding to it is the decision this test exists to make visible in review.
    /// </summary>
    private static readonly Type[] AllowedConsumers = [typeof(MarketplaceRepository)];

    /// <summary>
    /// Every assembly that could plausibly acquire the dependency. ShelfGuard.Api is scanned too
    /// (TASK-645 C2): controllers in this codebase already inject repositories directly —
    /// <c>MarketplaceChatController</c> takes <c>IMarketplaceRepository</c> — so "a controller
    /// takes IProviderRlsOverride" is precisely the mistake R4 exists to catch, and it would have
    /// slipped through an Application+Infrastructure-only scan. ShelfGuard.Domain is deliberately
    /// absent: it cannot reference the Application assembly at all, so the type is unreachable
    /// there by construction.
    /// </summary>
    private static readonly Assembly[] ScannedAssemblies =
    [
        typeof(IProviderRlsOverride).Assembly,                 // ShelfGuard.Application
        typeof(MarketplaceRepository).Assembly,                // ShelfGuard.Infrastructure
        typeof(ShelfGuard.Api.Controllers.MarketplaceChatController).Assembly, // ShelfGuard.Api
    ];

    [Fact]
    public void OnlyMarketplaceRepository_TakesProviderRlsOverride_AsConstructorDependency()
    {
        var consumers = ScannedAssemblies
            .SelectMany(SafeGetTypes)
            .Where(t => t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                         .Any(c => c.GetParameters()
                                    .Any(p => p.ParameterType == typeof(IProviderRlsOverride))))
            .Distinct()
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            AllowedConsumers.Select(t => t.FullName).OrderBy(n => n, StringComparer.Ordinal),
            consumers.Select(t => t.FullName));
    }

    /// <summary>
    /// Second half of the same containment rule: no service/controller may reach the bypass by
    /// holding the interface in a field either (e.g. via property injection or a service locator).
    /// </summary>
    [Fact]
    public void NoTypeOutsideMarketplaceRepository_HoldsProviderRlsOverride_InAField()
    {
        var holders = ScannedAssemblies
            .SelectMany(SafeGetTypes)
            .Where(t => t.GetFields(BindingFlags.Public | BindingFlags.NonPublic |
                                    BindingFlags.Instance | BindingFlags.Static)
                         .Any(f => f.FieldType == typeof(IProviderRlsOverride)))
            .Distinct()
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            AllowedConsumers.Select(t => t.FullName).OrderBy(n => n, StringComparer.Ordinal),
            holders.Select(t => t.FullName));
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }
}
