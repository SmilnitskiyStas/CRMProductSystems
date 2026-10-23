using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShelfGuard.Infrastructure.Data;

namespace ShelfGuard.Infrastructure.Migrations;

/// <summary>
/// Allow a consumer's own purchases through the restrictive SELECT store scope, without
/// granting that consumer INSERT/UPDATE/DELETE rights or changing staff store assignments.
/// The permissive tenant policy still applies; the service validates membership first.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261022001000_AddConsumerPurchaseReadScope")]
public sealed class AddConsumerPurchaseReadScope : Migration
{
    public const string UpSql = """
        DO $policy$
        DECLARE
          prior_using text;
          prior_check text;
          own_purchase text := $own$
            EXISTS (
              SELECT 1 FROM loyalty_memberships m
              WHERE m."TenantId" = pos_transactions."TenantId"
                AND m."ConsumerAccountId" = NULLIF(current_setting('app.consumer_account_id', true), '')::uuid
                AND (m."Id" = pos_transactions."LoyaltyMembershipId" OR EXISTS (
                  SELECT 1 FROM loyalty_ledger_entries le
                  WHERE le."TenantId" = pos_transactions."TenantId"
                    AND le."MembershipId" = m."Id" AND le."PosTransactionId" = pos_transactions."Id"
                ))
            )
          $own$;
        BEGIN
          SELECT pg_get_expr(polqual,polrelid),
                 coalesce(pg_get_expr(polwithcheck,polrelid),pg_get_expr(polqual,polrelid))
          INTO STRICT prior_using,prior_check FROM pg_policy
          WHERE polrelid = 'pos_transactions'::regclass AND polname = 'store_scope'
            AND polcmd = '*' AND NOT polpermissive;
          DROP POLICY store_scope ON pos_transactions;
          EXECUTE format('CREATE POLICY store_scope ON pos_transactions AS RESTRICTIVE FOR SELECT USING ((%s) OR (%s))',prior_using,own_purchase);
          EXECUTE format('CREATE POLICY store_scope_insert ON pos_transactions AS RESTRICTIVE FOR INSERT WITH CHECK (%s)',prior_check);
          EXECUTE format('CREATE POLICY store_scope_update ON pos_transactions AS RESTRICTIVE FOR UPDATE USING (%s) WITH CHECK (%s)',prior_using,prior_check);
          EXECUTE format('CREATE POLICY store_scope_delete ON pos_transactions AS RESTRICTIVE FOR DELETE USING (%s)',prior_using);
        END
        $policy$;
        """;

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(UpSql);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DO $policy$
        DECLARE prior_using text; prior_check text;
        BEGIN
          SELECT pg_get_expr(polqual,polrelid) INTO STRICT prior_using FROM pg_policy
          WHERE polrelid = 'pos_transactions'::regclass AND polname = 'store_scope_delete';
          SELECT pg_get_expr(polwithcheck,polrelid) INTO STRICT prior_check FROM pg_policy
          WHERE polrelid = 'pos_transactions'::regclass AND polname = 'store_scope_insert';
          DROP POLICY store_scope ON pos_transactions;
          DROP POLICY store_scope_insert ON pos_transactions;
          DROP POLICY store_scope_update ON pos_transactions;
          DROP POLICY store_scope_delete ON pos_transactions;
          EXECUTE format('CREATE POLICY store_scope ON pos_transactions AS RESTRICTIVE USING (%s) WITH CHECK (%s)',prior_using,prior_check);
        END
        $policy$;
        """);
}
