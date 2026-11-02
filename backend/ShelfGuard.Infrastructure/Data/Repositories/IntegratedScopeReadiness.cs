using Microsoft.EntityFrameworkCore;
namespace ShelfGuard.Infrastructure.Data.Repositories;

internal static class IntegratedScopeReadiness
{
    internal static async Task<bool> Ready(AppDbContext db,Guid tenant,Guid location,CancellationToken ct)=>
        await db.Database.SqlQuery<bool>($"SELECT to_regprocedure('public.erp_w4_integrated_ready(uuid,uuid)') IS NOT NULL AS \"Value\"").SingleAsync(ct)
        &&await db.Database.SqlQuery<bool>($"SELECT public.erp_w4_integrated_ready({tenant},{location}) AS \"Value\"").SingleAsync(ct);
}
