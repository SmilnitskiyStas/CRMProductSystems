using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddShortLoyaltyNumbers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "consumer_account_number_seq",
                startValue: 1000000000L,
                maxValue: 9999999999L);

            migrationBuilder.CreateSequence(
                name: "loyalty_card_number_seq",
                startValue: 1000000000L,
                maxValue: 9999999999L);

            migrationBuilder.AddColumn<long>(
                name: "CardNumber",
                table: "loyalty_memberships",
                type: "bigint",
                nullable: false,
                defaultValueSql: "nextval('loyalty_card_number_seq')");

            migrationBuilder.AddColumn<long>(
                name: "AccountNumber",
                table: "consumer_accounts",
                type: "bigint",
                nullable: false,
                defaultValueSql: "nextval('consumer_account_number_seq')");

            migrationBuilder.CreateIndex(
                name: "uq_loyalty_memberships_tenant_card_number",
                table: "loyalty_memberships",
                columns: new[] { "TenantId", "CardNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_consumer_accounts_account_number",
                table: "consumer_accounts",
                column: "AccountNumber",
                unique: true);

            // Same root cause/fix shape as FixLoyaltyTableGrants (20260726154747): a sequence
            // created by whatever role actually executes the migration (typically a superuser,
            // e.g. `crm` in dev) is owned by that role, not by the restricted app role the
            // running application actually connects as (`shelfguard_app_dev`/staging/prod
            // equivalents — see docs/staging.md "Migrations and seed data"). CREATE SEQUENCE has
            // no per-role default-privilege grant in this codebase (no ALTER DEFAULT PRIVILEGES
            // anywhere — same note as FixLoyaltyTableGrants), so without this, every
            // nextval('...') call from the app hits Postgres 42501 "permission denied for
            // sequence" the first time a row is inserted. Resolve the app role dynamically from
            // whichever role already owns `tenants` (same technique as FixLoyaltyTableGrants)
            // instead of hardcoding one environment's role name.
            migrationBuilder.Sql(@"
                DO $$
                DECLARE
                  app_owner text;
                BEGIN
                  SELECT tableowner INTO app_owner
                  FROM pg_tables
                  WHERE schemaname = 'public' AND tablename = 'tenants';

                  IF app_owner IS NULL THEN
                    RAISE EXCEPTION
                      'AddShortLoyaltyNumbers: could not resolve owner of table ""tenants"" — aborting rather than guessing a role name';
                  END IF;

                  EXECUTE format('ALTER SEQUENCE consumer_account_number_seq OWNER TO %I', app_owner);
                  EXECUTE format('ALTER SEQUENCE loyalty_card_number_seq OWNER TO %I', app_owner);
                END $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_loyalty_memberships_tenant_card_number",
                table: "loyalty_memberships");

            migrationBuilder.DropIndex(
                name: "uq_consumer_accounts_account_number",
                table: "consumer_accounts");

            migrationBuilder.DropColumn(
                name: "CardNumber",
                table: "loyalty_memberships");

            migrationBuilder.DropColumn(
                name: "AccountNumber",
                table: "consumer_accounts");

            migrationBuilder.DropSequence(
                name: "consumer_account_number_seq");

            migrationBuilder.DropSequence(
                name: "loyalty_card_number_seq");
        }
    }
}
