using System.Data.Common;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ShelfGuard.Infrastructure.Interceptors;

/// <summary>
/// Fires on every connection open (including pool checkout) and sets PostgreSQL session
/// variables app.tenant_id, app.role, and app.user_id so that RLS policies activate
/// automatically. app.user_id (TASK-392b) is prep for Stage 3's user_locations
/// EXISTS-subquery RESTRICTIVE policies — no such policy reads it yet.
/// </summary>
public sealed class TenantConnectionInterceptor : DbConnectionInterceptor
{
    // Whitelist prevents injection via a crafted JWT role claim.
    private static readonly HashSet<string> ValidRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "provider", "provider_admin", "provider_agent",
        "enterprise_admin", "network_manager",
        "store_manager", "merchandiser", "storekeeper", "cashier",
        // ADR-020 (TASK-345): minimal base tier, tenant-scoped like the roles above —
        // grants no RLS bypass, just lets app.role be set to a real value for a
        // capability-template-only user.
        "staff",
    };

    private readonly IHttpContextAccessor _http;

    public TenantConnectionInterceptor(IHttpContextAccessor http) => _http = http;

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        var sql = GetSetSql();
        if (sql is not null)
            await ExecuteSqlAsync(connection, sql, cancellationToken);
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        var sql = GetSetSql();
        if (sql is not null)
            ExecuteSqlAsync(connection, sql, CancellationToken.None).GetAwaiter().GetResult();
    }

    private string? GetSetSql()
    {
        var user = _http.HttpContext?.User;

        // Unauthenticated requests (login, health) must RESET session variables so that
        // a pooled connection's stale app.tenant_id cannot leak into the query.
        // The users-table RLS policy allows full visibility when app.tenant_id IS NULL,
        // which is the correct behaviour for the login lookup.
        if (user?.Identity?.IsAuthenticated != true)
            return "RESET app.tenant_id; RESET app.role; RESET app.user_id;";

        var tenantId = user.FindFirstValue("tenant_id");
        var role     = user.FindFirstValue(ClaimTypes.Role);
        var userId   = user.FindFirstValue(ClaimTypes.NameIdentifier);
        return BuildSetSql(tenantId, role, userId);
    }

    private static async Task ExecuteSqlAsync(DbConnection connection, string sql, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Builds the SET SQL string from raw claim values.
    /// Internal and static so unit tests can exercise it without a live connection.
    /// Returns null when there is nothing to set.
    /// </summary>
    internal static string? BuildSetSql(string? tenantId, string? role, string? userId = null)
    {
        var sb = new StringBuilder();

        // Always set app.tenant_id. Use the null UUID when the claim is absent (provider users)
        // so that connection-pool reuse cannot leak a previous tenant's session variable.
        // RLS: TenantId = '00000000-...'::uuid → never matches → 0 rows returned.
        if (!string.IsNullOrEmpty(tenantId) && Guid.TryParse(tenantId, out var tenantGuid))
            sb.Append($"SET app.tenant_id = '{tenantGuid:D}';");
        else
            sb.Append("SET app.tenant_id = '00000000-0000-0000-0000-000000000000';");

        // Only set role when it is on the known-roles whitelist.
        if (!string.IsNullOrEmpty(role) && ValidRoles.Contains(role))
            sb.Append($"SET app.role = '{role}';");

        // app.user_id (TASK-392b): same always-set/null-uuid-fallback discipline as
        // app.tenant_id above — prep for Stage 3's user_locations EXISTS-subquery RLS
        // policies (which will do "... WHERE user_id = current_setting('app.user_id')::uuid").
        // Never leave it unset: a pooled connection must not carry a stale real user_id across
        // requests the way an unset app.role harmlessly would (role has no such subquery
        // today). A null-uuid can never match a real UserId in user_locations, so it fails
        // safe (0 rows) exactly like tenant_id's fallback.
        if (!string.IsNullOrEmpty(userId) && Guid.TryParse(userId, out var userGuid))
            sb.Append($"SET app.user_id = '{userGuid:D}';");
        else
            sb.Append("SET app.user_id = '00000000-0000-0000-0000-000000000000';");

        return sb.Length > 0 ? sb.ToString() : null;
    }
}
