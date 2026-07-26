using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <summary>
    /// Loyalty program schema (Фаза 0, `docs/uployal/` plan — approved plan file
    /// `deep-cooking-nygaard.md`, TASK-404). Four new tables:
    ///
    /// <c>consumer_accounts</c> — global consumer identity (phone+password), the "one
    /// JWT reads every tenant's membership" root. Deliberately carries NO RLS at all —
    /// same precedent as <c>tenants</c> (globally readable, protected only by application
    /// code that never hands a generic GetById to a non-owner). This is a conscious
    /// architecture decision from the plan, not an oversight: flagged explicitly here for
    /// the mandatory security-reviewer pass before release. See the plan's own words:
    /// "Це найризикованіше окреме рішення Фази 0 — обов'язково на review до
    /// security-reviewer, бо таблиця без RLS фейлить відкрито при багу застосунку, на
    /// відміну від будь-якої RLS-таблиці в цьому репозиторії, яка фейлить закрито."
    ///
    /// <c>loyalty_memberships</c> / <c>loyalty_ledger_entries</c> /
    /// <c>loyalty_program_settings</c> — tenant-scoped, get the canonical fail-closed
    /// triad (tenant_isolation NULLIF-guarded + provider_bypass + worker_bypass), same
    /// shape as every other FORCE RLS table added since 20260714180000.
    ///
    /// <c>loyalty_memberships</c> and <c>loyalty_ledger_entries</c> additionally get a
    /// new, narrower, first-of-its-kind IDENTITY-based (not role-based) policy,
    /// <c>consumer_self_access</c>: it lets a cross-tenant ConsumerAccount session (which
    /// never sets <c>app.tenant_id</c> at all — see TenantConnectionInterceptor) read only
    /// its own rows, in every tenant, via <c>app.consumer_account_id</c> instead. Because
    /// Postgres ORs together multiple PERMISSIVE policies for the same command, this is
    /// additive on top of tenant_isolation (which such a session never satisfies) rather
    /// than a replacement for it — the two other bypass-only roles (provider/worker) are
    /// completely unaffected by this new policy's presence.
    ///
    /// <c>provider_bypass</c> on the three tenant-scoped tables is written to accept both
    /// 'provider' AND 'provider_admin' from day one (deviating from the literal
    /// single-role template in database-schema.md, which predates
    /// 20260714150000_ExpandProviderBypassToProviderAdmin) — that migration already
    /// established provider_admin has identical effective permissions to provider
    /// (ProviderPermissions.SystemRoleDefaults) on every one of the 71 tables that existed
    /// at the time; writing new tables with only 'provider' here would just recreate the
    /// same gap that migration had to retroactively fix. Same judgment call
    /// AddLocationStoreScopeRlsPolicies (20260719193545) made explicitly for its own
    /// bypass list.
    /// </summary>
    public partial class AddLoyaltyProgram : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // consumer_accounts: NO RLS statements anywhere in this migration for this
            // table — deliberate, see class remarks above.
            migrationBuilder.CreateTable(
                name: "consumer_accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Phone = table.Column<string>(type: "text", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    FullName = table.Column<string>(type: "text", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: true),
                    FailedLoginAttempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    LockoutUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    LastLoginAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consumer_accounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "loyalty_program_settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    AccrualRatePercent = table.Column<decimal>(type: "numeric(5,2)", nullable: false, defaultValue: 3.0m),
                    RedemptionCapPercent = table.Column<decimal>(type: "numeric(5,2)", nullable: false, defaultValue: 50.0m),
                    MinRedemptionBalance = table.Column<decimal>(type: "numeric(18,2)", nullable: false, defaultValue: 0m),
                    CodeTtlSeconds = table.Column<int>(type: "integer", nullable: false, defaultValue: 30),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_loyalty_program_settings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_loyalty_program_settings_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "loyalty_memberships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsumerAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    LinkedUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    TotpSecret = table.Column<string>(type: "text", nullable: false),
                    LastRedeemedTimestep = table.Column<long>(type: "bigint", nullable: true),
                    Balance = table.Column<decimal>(type: "numeric(18,2)", nullable: false, defaultValue: 0m),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "active"),
                    JoinedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_loyalty_memberships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_loyalty_memberships_consumer_accounts_ConsumerAccountId",
                        column: x => x.ConsumerAccountId,
                        principalTable: "consumer_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_loyalty_memberships_customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_loyalty_memberships_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_loyalty_memberships_users_LinkedUserId",
                        column: x => x.LinkedUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "loyalty_ledger_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    MembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntryType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    BalanceAfter = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    PosTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Note = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_loyalty_ledger_entries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_loyalty_ledger_entries_loyalty_memberships_MembershipId",
                        column: x => x.MembershipId,
                        principalTable: "loyalty_memberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_loyalty_ledger_entries_pos_transactions_PosTransactionId",
                        column: x => x.PosTransactionId,
                        principalTable: "pos_transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_loyalty_ledger_entries_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "uq_consumer_accounts_phone",
                table: "consumer_accounts",
                column: "Phone",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_loyalty_ledger_membership_created",
                table: "loyalty_ledger_entries",
                columns: new[] { "TenantId", "MembershipId", "CreatedAt" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "idx_loyalty_ledger_pos_transaction",
                table: "loyalty_ledger_entries",
                column: "PosTransactionId",
                filter: "\"PosTransactionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_loyalty_ledger_entries_CreatedByUserId",
                table: "loyalty_ledger_entries",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_loyalty_ledger_entries_MembershipId",
                table: "loyalty_ledger_entries",
                column: "MembershipId");

            migrationBuilder.CreateIndex(
                name: "idx_loyalty_memberships_tenant",
                table: "loyalty_memberships",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_loyalty_memberships_ConsumerAccountId",
                table: "loyalty_memberships",
                column: "ConsumerAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_loyalty_memberships_CustomerId",
                table: "loyalty_memberships",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_loyalty_memberships_LinkedUserId",
                table: "loyalty_memberships",
                column: "LinkedUserId");

            migrationBuilder.CreateIndex(
                name: "uq_loyalty_memberships_tenant_consumer",
                table: "loyalty_memberships",
                columns: new[] { "TenantId", "ConsumerAccountId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_loyalty_program_settings_tenant",
                table: "loyalty_program_settings",
                column: "TenantId",
                unique: true);

            // ── RLS: loyalty_memberships ─────────────────────────────────────
            // Canonical fail-closed triad + the new identity-based consumer_self_access
            // policy (see class remarks). provider_bypass includes provider_admin from
            // day one (see class remarks).
            migrationBuilder.Sql(@"
                ALTER TABLE loyalty_memberships ENABLE ROW LEVEL SECURITY;
                ALTER TABLE loyalty_memberships FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON loyalty_memberships
                  USING (""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);
                CREATE POLICY provider_bypass ON loyalty_memberships
                  USING (current_setting('app.role', true) IN ('provider', 'provider_admin'));
                CREATE POLICY worker_bypass ON loyalty_memberships
                  USING (current_setting('app.role', true) = 'worker');
                CREATE POLICY consumer_self_access ON loyalty_memberships
                  USING (""ConsumerAccountId"" = (NULLIF(current_setting('app.consumer_account_id', true), ''))::uuid);
            ");

            // ── RLS: loyalty_ledger_entries ──────────────────────────────────
            // Same triad. consumer_self_access here has no direct ConsumerAccountId column
            // to compare against, so it EXISTS-subqueries through the parent membership —
            // same shape database-schema.md documents for child tables without their own
            // tenant column, just keyed on consumer identity instead of tenant identity.
            migrationBuilder.Sql(@"
                ALTER TABLE loyalty_ledger_entries ENABLE ROW LEVEL SECURITY;
                ALTER TABLE loyalty_ledger_entries FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON loyalty_ledger_entries
                  USING (""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);
                CREATE POLICY provider_bypass ON loyalty_ledger_entries
                  USING (current_setting('app.role', true) IN ('provider', 'provider_admin'));
                CREATE POLICY worker_bypass ON loyalty_ledger_entries
                  USING (current_setting('app.role', true) = 'worker');
                CREATE POLICY consumer_self_access ON loyalty_ledger_entries
                  USING (
                    EXISTS (
                      SELECT 1 FROM loyalty_memberships m
                      WHERE m.""Id"" = loyalty_ledger_entries.""MembershipId""
                        AND m.""ConsumerAccountId"" = (NULLIF(current_setting('app.consumer_account_id', true), ''))::uuid
                    )
                  );
            ");

            // ── RLS: loyalty_program_settings ────────────────────────────────
            // Canonical triad only — no consumer_self_access (consumers never read this
            // table; it's staff/enterprise_admin configuration, per the plan's endpoint
            // table: GET/PUT /api/settings/loyalty is enterprise_admin-only).
            migrationBuilder.Sql(@"
                ALTER TABLE loyalty_program_settings ENABLE ROW LEVEL SECURITY;
                ALTER TABLE loyalty_program_settings FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON loyalty_program_settings
                  USING (""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);
                CREATE POLICY provider_bypass ON loyalty_program_settings
                  USING (current_setting('app.role', true) IN ('provider', 'provider_admin'));
                CREATE POLICY worker_bypass ON loyalty_program_settings
                  USING (current_setting('app.role', true) = 'worker');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "loyalty_ledger_entries");

            migrationBuilder.DropTable(
                name: "loyalty_program_settings");

            migrationBuilder.DropTable(
                name: "loyalty_memberships");

            migrationBuilder.DropTable(
                name: "consumer_accounts");
        }
    }
}
