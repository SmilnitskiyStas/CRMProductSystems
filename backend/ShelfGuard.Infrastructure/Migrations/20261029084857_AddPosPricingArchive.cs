using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPosPricingArchive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PricingArchiveRequired",
                table: "pos_transactions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_pos_transactions_TenantId_Id",
                table: "pos_transactions",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateTable(
                name: "pos_pricing_snapshots",
                columns: table => new
                {
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginTransactionId = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "pg_current_xact_id()::text::bigint"),
                    Protocol = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Channel = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    EvaluatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Payload = table.Column<string>(type: "json", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pos_pricing_snapshots", x => x.TransactionId);
                    table.CheckConstraint("CK_pos_pricing_snapshot_hash", "\"ContentHash\" ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("CK_pos_pricing_snapshot_protocol", "\"Protocol\"='pos.pricing.archive.v1' AND \"Channel\" IN ('pos-online','pos-offline-legacy','pos-offline-pr1')");
                    table.ForeignKey(
                        name: "FK_pos_pricing_snapshots_locations_TenantId_StoreId",
                        columns: x => new { x.TenantId, x.StoreId },
                        principalTable: "locations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pos_pricing_snapshots_pos_transactions_TenantId_Transaction~",
                        columns: x => new { x.TenantId, x.TransactionId },
                        principalTable: "pos_transactions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pos_pricing_snapshots_TenantId_StoreId_EvaluatedAt",
                table: "pos_pricing_snapshots",
                columns: new[] { "TenantId", "StoreId", "EvaluatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_pos_pricing_snapshots_TenantId_TransactionId",
                table: "pos_pricing_snapshots",
                columns: new[] { "TenantId", "TransactionId" },
                unique: true);
            InstallGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("Pricing audit history must be preserved; use a forward migration.");
    }
}
