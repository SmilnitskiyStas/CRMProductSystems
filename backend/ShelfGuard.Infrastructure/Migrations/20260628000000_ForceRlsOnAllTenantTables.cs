using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShelfGuard.Infrastructure.Data;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260628000000_ForceRlsOnAllTenantTables")]
    public partial class ForceRlsOnAllTenantTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // PostgreSQL bypasses RLS for the table owner by default.
            // When the app DB user owns the tables (which it does here), all RLS
            // policies are silently skipped unless FORCE ROW LEVEL SECURITY is set.
            // This applies FORCE RLS to every table that already has RLS enabled.
            migrationBuilder.Sql(@"
DO $force$
DECLARE t text;
BEGIN
    FOR t IN
        SELECT tablename FROM pg_tables
        WHERE schemaname = 'public' AND rowsecurity = true
    LOOP
        EXECUTE format('ALTER TABLE %I FORCE ROW LEVEL SECURITY', t);
    END LOOP;
END $force$;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DO $unforce$
DECLARE t text;
BEGIN
    FOR t IN
        SELECT tablename FROM pg_tables
        WHERE schemaname = 'public' AND rowsecurity = true
    LOOP
        EXECUTE format('ALTER TABLE %I NO FORCE ROW LEVEL SECURITY', t);
    END LOOP;
END $unforce$;
");
        }
    }
}
