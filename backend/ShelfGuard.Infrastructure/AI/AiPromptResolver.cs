using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ShelfGuard.Application.Services;
using ShelfGuard.Infrastructure.Data;

namespace ShelfGuard.Infrastructure.AI;

/// <summary>
/// Default <see cref="IAiPromptResolver"/>. Looks up the current tenant's name and the
/// provider-set <c>extra_instructions</c>, then delegates the string composition to the pure
/// <see cref="AiGuardrail"/>.
///
/// Both reads are RLS-scoped to the caller's tenant exactly like the advisors' own
/// <c>ResolveAsync</c> — the <c>integration_configs</c> read is the same <c>Service == "claude"
/// &amp;&amp; IsEnabled</c> row, and <c>tenants</c> has no RLS so the name lookup is a plain filtered
/// query. No RLS bypass primitive is taken here (enforced by
/// <c>AiPromptResolverRlsContainmentTests</c>).
/// </summary>
public sealed class AiPromptResolver : IAiPromptResolver
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenant;

    public AiPromptResolver(AppDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<string> WrapSystemPromptAsync(string basePrompt, CancellationToken ct = default)
    {
        var tenantId = _tenant.TenantId;
        if (tenantId is null)
            return basePrompt; // provider / worker / unauthenticated — no tenant to scope to

        var name = await _db.Tenants
            .Where(t => t.Id == tenantId.Value)
            .Select(t => t.Name)
            .FirstOrDefaultAsync(ct);

        var extra = await ResolveExtraInstructionsAsync(ct);

        return AiGuardrail.Compose(name, extra, basePrompt);
    }

    private async Task<string?> ResolveExtraInstructionsAsync(CancellationToken ct)
    {
        var configJson = await _db.IntegrationConfigs
            .Where(i => (i.Service == "claude" || i.Service == "openai") && i.IsEnabled)
            .OrderBy(i => i.Service)
            .Select(i => i.Config)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(configJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(configJson);
            return doc.RootElement.TryGetProperty("extra_instructions", out var e)
                ? e.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
