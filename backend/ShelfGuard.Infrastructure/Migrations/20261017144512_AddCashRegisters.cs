using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCashRegisters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_pos_shifts_LocationId",
                table: "pos_shifts");

            migrationBuilder.AddColumn<string>(
                name: "PosPinHash",
                table: "users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PosPinSetAt",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClientTransactionId",
                table: "pos_transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RegisterId",
                table: "pos_shifts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "items",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "NOW()");

            migrationBuilder.CreateTable(
                name: "cash_registers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "line"),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "pending"),
                    DeviceTokenHash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ActivationCodeHash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ActivationCodeExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ActivatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AppVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    FailedPinAttempts = table.Column<int>(type: "integer", nullable: false),
                    PinLockoutUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cash_registers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_cash_registers_locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cash_registers_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "register_sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RegisterId = table.Column<Guid>(type: "uuid", nullable: false),
                    CashierId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    EndedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EndReason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_register_sessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_register_sessions_cash_registers_RegisterId",
                        column: x => x.RegisterId,
                        principalTable: "cash_registers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_register_sessions_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_register_sessions_users_CashierId",
                        column: x => x.CashierId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pos_transactions_CashRegisterId",
                table: "pos_transactions",
                column: "CashRegisterId");

            migrationBuilder.CreateIndex(
                name: "uq_pos_transactions_client_tx",
                table: "pos_transactions",
                columns: new[] { "TenantId", "ClientTransactionId" },
                unique: true,
                filter: "\"ClientTransactionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_pos_shifts_LocationId",
                table: "pos_shifts",
                column: "LocationId",
                unique: true,
                filter: "\"ClosedAt\" IS NULL AND \"RegisterId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "uq_pos_shifts_open_per_register",
                table: "pos_shifts",
                column: "RegisterId",
                unique: true,
                filter: "\"ClosedAt\" IS NULL AND \"RegisterId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_items_tenant_updated",
                table: "items",
                columns: new[] { "TenantId", "UpdatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_cash_registers_LocationId",
                table: "cash_registers",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_cash_registers_TenantId_LocationId",
                table: "cash_registers",
                columns: new[] { "TenantId", "LocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_cash_registers_TenantId_Number",
                table: "cash_registers",
                columns: new[] { "TenantId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_register_sessions_open",
                table: "register_sessions",
                column: "RegisterId",
                filter: "\"EndedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_register_sessions_CashierId",
                table: "register_sessions",
                column: "CashierId");

            migrationBuilder.CreateIndex(
                name: "IX_register_sessions_RegisterId_StartedAt",
                table: "register_sessions",
                columns: new[] { "RegisterId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_register_sessions_TenantId",
                table: "register_sessions",
                column: "TenantId");

            migrationBuilder.AddForeignKey(
                name: "FK_pos_shifts_cash_registers_RegisterId",
                table: "pos_shifts",
                column: "RegisterId",
                principalTable: "cash_registers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // TASK-613 reserved CashRegisterId with no FK/entity; cash_registers is brand new so any
            // pre-existing value is necessarily dangling and would violate the FK below.
            migrationBuilder.Sql("UPDATE pos_transactions SET \"CashRegisterId\" = NULL WHERE \"CashRegisterId\" IS NOT NULL;");

            migrationBuilder.AddForeignKey(
                name: "FK_pos_transactions_cash_registers_CashRegisterId",
                table: "pos_transactions",
                column: "CashRegisterId",
                principalTable: "cash_registers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // ── items.UpdatedAt: backfill + trigger (delta catalog sync) ─────────────
            // A trigger (not EF) so EVERY write path - bulk import, marketplace merge, raw SQL -
            // bumps it. Backfill from CreatedAt first so the initial AddColumn NOW() does not make
            // every existing item look "just changed"; the trigger is created after the backfill.
            migrationBuilder.Sql(@"
                UPDATE items SET ""UpdatedAt"" = ""CreatedAt"";
                CREATE OR REPLACE FUNCTION items_set_updated_at() RETURNS trigger AS $$
                BEGIN
                  NEW.""UpdatedAt"" := NOW();
                  RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                CREATE TRIGGER trg_items_set_updated_at BEFORE UPDATE ON items
                  FOR EACH ROW EXECUTE FUNCTION items_set_updated_at();
            ");

            // ── RLS: cash_registers + register_sessions ──────────────────────────────
            // Canonical triad (tenant_isolation with NULLIF guard + provider_bypass + worker_bypass
            // added immediately - new RLS tables without worker_bypass silently break worker jobs).
            // Device endpoints (activate / PIN session) have no user JWT, so they open a tenant
            // scope via ITenantSessionOverride (SET LOCAL app.tenant_id) - no extra policy needed.
            migrationBuilder.Sql(@"
                ALTER TABLE cash_registers ENABLE ROW LEVEL SECURITY;
                ALTER TABLE cash_registers FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON cash_registers
                  USING (""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);
                CREATE POLICY provider_bypass ON cash_registers
                  USING (current_setting('app.role', true) IN ('provider', 'provider_admin'));
                CREATE POLICY worker_bypass ON cash_registers
                  USING (current_setting('app.role', true) = 'worker');

                ALTER TABLE register_sessions ENABLE ROW LEVEL SECURITY;
                ALTER TABLE register_sessions FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON register_sessions
                  USING (""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);
                CREATE POLICY provider_bypass ON register_sessions
                  USING (current_setting('app.role', true) IN ('provider', 'provider_admin'));
                CREATE POLICY worker_bypass ON register_sessions
                  USING (current_setting('app.role', true) = 'worker');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS trg_items_set_updated_at ON items;
                DROP FUNCTION IF EXISTS items_set_updated_at();
                DROP POLICY IF EXISTS worker_bypass ON register_sessions;
                DROP POLICY IF EXISTS provider_bypass ON register_sessions;
                DROP POLICY IF EXISTS tenant_isolation ON register_sessions;
                DROP POLICY IF EXISTS worker_bypass ON cash_registers;
                DROP POLICY IF EXISTS provider_bypass ON cash_registers;
                DROP POLICY IF EXISTS tenant_isolation ON cash_registers;
            ");

            migrationBuilder.DropForeignKey(
                name: "FK_pos_shifts_cash_registers_RegisterId",
                table: "pos_shifts");

            migrationBuilder.DropForeignKey(
                name: "FK_pos_transactions_cash_registers_CashRegisterId",
                table: "pos_transactions");

            migrationBuilder.DropTable(
                name: "register_sessions");

            migrationBuilder.DropTable(
                name: "cash_registers");

            migrationBuilder.DropIndex(
                name: "IX_pos_transactions_CashRegisterId",
                table: "pos_transactions");

            migrationBuilder.DropIndex(
                name: "uq_pos_transactions_client_tx",
                table: "pos_transactions");

            migrationBuilder.DropIndex(
                name: "IX_pos_shifts_LocationId",
                table: "pos_shifts");

            migrationBuilder.DropIndex(
                name: "uq_pos_shifts_open_per_register",
                table: "pos_shifts");

            migrationBuilder.DropIndex(
                name: "idx_items_tenant_updated",
                table: "items");

            migrationBuilder.DropColumn(
                name: "PosPinHash",
                table: "users");

            migrationBuilder.DropColumn(
                name: "PosPinSetAt",
                table: "users");

            migrationBuilder.DropColumn(
                name: "ClientTransactionId",
                table: "pos_transactions");

            migrationBuilder.DropColumn(
                name: "RegisterId",
                table: "pos_shifts");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "items");

            migrationBuilder.CreateIndex(
                name: "IX_pos_shifts_LocationId",
                table: "pos_shifts",
                column: "LocationId",
                unique: true,
                filter: "\"ClosedAt\" IS NULL");
        }
    }
}
