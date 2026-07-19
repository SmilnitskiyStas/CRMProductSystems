using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixUserLocationColumnMapping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "StoreId",
                table: "users",
                newName: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_users_LocationId",
                table: "users",
                column: "LocationId");

            // NOT VALID zero-downtime FK (not the EF-generated migrationBuilder.AddForeignKey,
            // which validates every existing row synchronously as part of adding the
            // constraint). Two reasons this matters here specifically, not just in general:
            //   1. Production applies migrations via db.Database.MigrateAsync() on the app's
            //      own regular (non-superuser, NOBYPASSRLS) connection at container startup
            //      (Program.cs:159-163) — a validating ADD CONSTRAINT would run its row check
            //      through that same connection, and FORCE ROW LEVEL SECURITY on `locations`
            //      hides every row from a non-superuser/non-bypassrls role that has no
            //      app.tenant_id/app.role session var set (migrations run outside any request
            //      context) — every existing non-null users.LocationId would look orphaned to
            //      the validator even when none are, so the constraint-add would throw 23503
            //      and MigrateAsync() would throw, crashing the container on startup.
            //   2. NOT VALID still enforces the FK for every new/updated row immediately — it
            //      only skips checking rows that already exist at ADD CONSTRAINT time, so this
            //      closes the "any GUID silently accepted" gap right away with zero risk to the
            //      deploy path.
            // TODO(follow-up, run manually via the crm/superuser connection once convenient —
            // does not block deploy): `ALTER TABLE users VALIDATE CONSTRAINT
            // "FK_users_locations_LocationId";` to confirm the (expected-empty-in-prod) existing
            // rows are in fact all valid and flip the constraint to convalidated=true. Needs a
            // superuser/bypassrls connection for the same RLS-visibility reason as above.
            migrationBuilder.Sql(
                "ALTER TABLE users ADD CONSTRAINT \"FK_users_locations_LocationId\" " +
                "FOREIGN KEY (\"LocationId\") REFERENCES locations (\"Id\") " +
                "ON DELETE SET NULL NOT VALID;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_users_locations_LocationId",
                table: "users");

            migrationBuilder.DropIndex(
                name: "IX_users_LocationId",
                table: "users");

            migrationBuilder.RenameColumn(
                name: "LocationId",
                table: "users",
                newName: "StoreId");
        }
    }
}
