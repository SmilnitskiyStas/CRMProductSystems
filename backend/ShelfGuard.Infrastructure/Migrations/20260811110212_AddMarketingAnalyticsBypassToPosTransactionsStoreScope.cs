using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <summary>
    /// TASK-509 (ADR-028, fixes KI-033): store_scope's RESTRICTIVE policy on pos_transactions
    /// (added by 20260719193545_AddLocationStoreScopeRlsPolicies) silently corrupts every
    /// MarketingAnalyticsRepository query for a store_manager/network_manager caller — those
    /// roles only get the EXISTS(user_locations) branch, which narrows visibility to whatever
    /// locations happen to be assigned to the acting user's own account, even though marketing
    /// analytics is a tenant-wide reporting feature that must see every location regardless of
    /// which locations the calling user personally works at.
    ///
    /// Fix: add a fifth bypass value, 'marketing_analytics_bypass', to this ONE policy's
    /// IN-list. Unlike the other four bypass roles (which are real, JWT-assignable roles),
    /// 'marketing_analytics_bypass' is never assignable to any User/TenantRole and is never in
    /// TenantConnectionInterceptor.ValidRoles or UserService.ValidRoles — it can only ever be
    /// set by the new AnalyticsRlsOverride's hardcoded `SET LOCAL app.role =
    /// 'marketing_analytics_bypass'` string, scoped to one repository method's transaction only
    /// (see ShelfGuard.Application/Services/IAnalyticsRlsOverride.cs for the full security
    /// contract). No other store_scope-governed table changes — this is the only RLS policy
    /// MarketingAnalyticsRepository's queries actually touch.
    /// </summary>
    public partial class AddMarketingAnalyticsBypassToPosTransactionsStoreScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS store_scope ON pos_transactions;
                CREATE POLICY store_scope ON pos_transactions AS RESTRICTIVE
                  USING (
                    current_setting('app.role', true) IN (
                      'provider', 'provider_admin', 'worker', 'enterprise_admin', 'marketing_analytics_bypass'
                    )
                    OR EXISTS (
                         SELECT 1 FROM user_locations ul
                         WHERE ul.""UserId"" = NULLIF(current_setting('app.user_id', true), '')::uuid
                           AND ul.""LocationId"" = pos_transactions.""LocationId""
                       )
                  );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restores the original 4-role policy exactly as created by
            // 20260719193545_AddLocationStoreScopeRlsPolicies (pos_transactions block only).
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS store_scope ON pos_transactions;
                CREATE POLICY store_scope ON pos_transactions AS RESTRICTIVE
                  USING (
                    current_setting('app.role', true) IN ('provider', 'provider_admin', 'worker', 'enterprise_admin')
                    OR EXISTS (
                         SELECT 1 FROM user_locations ul
                         WHERE ul.""UserId"" = NULLIF(current_setting('app.user_id', true), '')::uuid
                           AND ul.""LocationId"" = pos_transactions.""LocationId""
                       )
                  );
            ");
        }
    }
}
