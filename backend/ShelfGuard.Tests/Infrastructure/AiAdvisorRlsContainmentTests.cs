using System.Reflection;
using ShelfGuard.Application.Services;
using Xunit;

namespace ShelfGuard.Tests.Infrastructure;

/// <summary>
/// Managed-AI Phase 1 — tenant isolation, data layer.
///
/// The AI advisors in <c>ShelfGuard.Infrastructure.AI</c> read <c>integration_configs</c> (the
/// tenant's Claude key) and, via <c>AiPromptResolver</c>, <c>tenants</c> — all under normal
/// RLS <c>tenant_isolation</c>, with no <c>.Where(TenantId == ...)</c> of their own. That is only
/// safe as long as no advisor holds an RLS-bypass primitive: <see cref="IProviderRlsOverride"/>
/// (<c>app.role='provider'</c>), <see cref="IAnalyticsRlsOverride"/>
/// (<c>app.role='marketing_analytics_bypass'</c>) or <see cref="ITenantSessionOverride"/>
/// (<c>SET LOCAL app.tenant_id</c>). Historically KI-036 "F5" was exactly this — a leaked
/// <c>provider</c> role let <c>SupplierAdvisor</c> read another tenant's Claude key.
///
/// This pins the containment: nothing under the <c>ShelfGuard.Infrastructure.AI</c> namespace
/// may take any of the three overrides as a constructor parameter or field. If it fails, an
/// advisor (or its prompt resolver / connectivity tester) has acquired a cross-tenant bypass —
/// do not extend an allow-list; re-derive why the advisor needs it and whether the guardrail
/// still holds.
/// </summary>
public sealed class AiAdvisorRlsContainmentTests
{
    private static readonly Type[] ForbiddenOverrides =
    [
        typeof(IProviderRlsOverride),
        typeof(IAnalyticsRlsOverride),
        typeof(ITenantSessionOverride),
    ];

    private static IEnumerable<Type> AiNamespaceTypes() =>
        SafeGetTypes(typeof(ShelfGuard.Infrastructure.AI.AiGuardrail).Assembly)
            .Where(t => t.Namespace is { } ns &&
                        (ns == "ShelfGuard.Infrastructure.AI" ||
                         ns.StartsWith("ShelfGuard.Infrastructure.AI.", StringComparison.Ordinal)));

    [Fact]
    public void NoAiType_TakesAnRlsOverride_AsConstructorParameter()
    {
        var offenders = AiNamespaceTypes()
            .Where(t => t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                         .SelectMany(c => c.GetParameters())
                         .Any(p => ForbiddenOverrides.Contains(p.ParameterType)))
            .Select(t => t.FullName)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void NoAiType_HoldsAnRlsOverride_InAField()
    {
        var offenders = AiNamespaceTypes()
            .Where(t => t.GetFields(BindingFlags.Public | BindingFlags.NonPublic |
                                    BindingFlags.Instance | BindingFlags.Static)
                         .Any(f => ForbiddenOverrides.Contains(f.FieldType)))
            .Select(t => t.FullName)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.Empty(offenders);
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
