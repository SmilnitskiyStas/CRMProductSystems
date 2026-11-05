using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ShelfGuard.Infrastructure.Data;

namespace ShelfGuard.Tests.Erp;

internal static class InventoryFenceTestHelper
{
    internal static string BeforeTrigger(Guid tenant, Guid location) => "erp_w4_b_" +
        Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes($"{tenant:D}:{location:D}"))).ToLowerInvariant();

    internal static Task<string> Fingerprint(AppDbContext db, Guid tenant, Guid location) =>
        db.Database.SqlQuery<string>($"SELECT public.erp_w4_scope_fingerprint({tenant},{location}) AS \"Value\"").SingleAsync();

    internal static async Task<Guid> Activate(AppDbContext db, Guid tenant, Guid location, Guid actor,
        Guid? activation = null, string? fingerprint = null, string reason = "Reviewed disposable inventory pilot")
    {
        var id = activation ?? Guid.NewGuid(); fingerprint ??= await Fingerprint(db, tenant, location);
        return await db.Database.SqlQuery<Guid>($"SELECT public.erp_w4_activate_scope({tenant},{location},{id},{actor},{fingerprint},{reason}) AS \"Value\"").SingleAsync();
    }
}
