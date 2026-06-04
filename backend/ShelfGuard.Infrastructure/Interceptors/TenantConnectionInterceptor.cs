using System.Data.Common;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ShelfGuard.Infrastructure.Interceptors;

/// <summary>
/// Fires on every connection open (including pool checkout) and sets PostgreSQL session
/// variables app.tenant_id and app.role so that RLS policies activate automatically.
/// </summary>
public sealed class TenantConnectionInterceptor : DbConnectionInterceptor
{
    // Whitelist prevents injection via a crafted JWT role claim.
    private static readonly HashSet<string> ValidRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "provider", "enterprise_admin", "network_manager",
        "store_manager", "merchandiser", "storekeeper", "cashier",
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
        if (user?.Identity?.IsAuthenticated != true) return null;

        var tenantId = user.FindFirstValue("tenant_id");
        var role     = user.FindFirstValue(ClaimTypes.Role);
        return BuildSetSql(tenantId, role);
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
    internal static string? BuildSetSql(string? tenantId, string? role)
    {
        var sb = new StringBuilder();

        // Only set tenant_id when it is a valid UUID (provider users have no tenant claim).
        if (!string.IsNullOrEmpty(tenantId) && Guid.TryParse(tenantId, out var guid))
            sb.Append($"SET app.tenant_id = '{guid:D}';");

        // Only set role when it is on the known-roles whitelist.
        if (!string.IsNullOrEmpty(role) && ValidRoles.Contains(role))
            sb.Append($"SET app.role = '{role}';");

        return sb.Length > 0 ? sb.ToString() : null;
    }
}
