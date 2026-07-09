using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLegalEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LegalEntityId",
                table: "users",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LegalEntityId",
                table: "locations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "legal_entities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    LegalName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Edrpou = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    LegalAddress = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DirectorName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Iban = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    BankName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    IsVatPayer = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_legal_entities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_legal_entities_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_users_LegalEntityId",
                table: "users",
                column: "LegalEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_locations_LegalEntityId",
                table: "locations",
                column: "LegalEntityId");

            migrationBuilder.CreateIndex(
                name: "idx_legal_entities_tenant",
                table: "legal_entities",
                column: "TenantId");

            migrationBuilder.AddForeignKey(
                name: "FK_locations_legal_entities_LegalEntityId",
                table: "locations",
                column: "LegalEntityId",
                principalTable: "legal_entities",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_users_legal_entities_LegalEntityId",
                table: "users",
                column: "LegalEntityId",
                principalTable: "legal_entities",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // ── RLS: legal_entities ──────────────────────────────────────────────
            // Single-tenant pattern (like supplier_contract_settings): only the
            // owning tenant sees its own legal entities. NULLIF guard: current_setting
            // (..., true) returns '' after RESET, which breaks the ::uuid cast without it.
            migrationBuilder.Sql(@"
                ALTER TABLE legal_entities ENABLE ROW LEVEL SECURITY;
                ALTER TABLE legal_entities FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON legal_entities
                  USING (""TenantId"" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
                CREATE POLICY provider_bypass ON legal_entities
                  USING (current_setting('app.role', true) = 'provider');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS provider_bypass ON legal_entities;
                DROP POLICY IF EXISTS tenant_isolation ON legal_entities;
                ALTER TABLE legal_entities DISABLE ROW LEVEL SECURITY;
            ");

            migrationBuilder.DropForeignKey(
                name: "FK_locations_legal_entities_LegalEntityId",
                table: "locations");

            migrationBuilder.DropForeignKey(
                name: "FK_users_legal_entities_LegalEntityId",
                table: "users");

            migrationBuilder.DropTable(
                name: "legal_entities");

            migrationBuilder.DropIndex(
                name: "IX_users_LegalEntityId",
                table: "users");

            migrationBuilder.DropIndex(
                name: "IX_locations_LegalEntityId",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "LegalEntityId",
                table: "users");

            migrationBuilder.DropColumn(
                name: "LegalEntityId",
                table: "locations");
        }
    }
}
