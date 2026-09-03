using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <summary>
    /// Supplier-portal expansion — Phase 2 (plan `1-partitioned-book.md`, decisions D2, D3).
    ///
    /// Four new supplier-owned tables — parallel to the retail Stock / Receipts model, NOT a
    /// reuse of it (D2): the supplier catalog is <c>supplier_items</c> (nullable <c>ItemId</c>),
    /// not <c>items</c>, and <c>product_stock</c> carries a RESTRICTIVE <c>store_scope</c> policy
    /// (ADR-022) that a supplier_admin — who has no <c>user_locations</c> rows — would read as
    /// zero rows.
    ///   • supplier_stock             — FEFO batches, keyed on (SupplierItemId, WarehouseId);
    ///                                   xmin optimistic-concurrency token; partial FEFO index.
    ///   • supplier_stock_movements   — append-only ledger (receipt / ship / adjust / write_off).
    ///   • supplier_stock_receipts    — manual "what actually arrived" intake documents.
    ///   • supplier_stock_receipt_items — N rows per (SupplierItemId, ExpiryDate, BatchNumber);
    ///                                   TenantId denormalized so RLS is a plain tenant_isolation.
    ///
    /// RLS (D8): every table gets tenant_isolation (NULLIF-guarded, fail-closed, WITH CHECK) +
    /// provider_bypass IN ('provider','provider_admin') + worker_bypass, all under FORCE ROW
    /// LEVEL SECURITY. Policy SQL added by hand below (EF does not emit it), copied from the
    /// current supplier_* policy shape (cf. 20260901193439_AddSupplierMetricsHistory).
    ///
    /// NO <c>store_scope</c> policy — deliberate. Supplier tenants have no <c>user_locations</c>
    /// model, so the per-store narrowing the retail product_stock table applies has no meaning
    /// here; a supplier_admin sees every batch of their own tenant.
    /// </summary>
    public partial class AddSupplierInventory : Migration
    {
        private static readonly string[] Tables =
        {
            "supplier_stock",
            "supplier_stock_movements",
            "supplier_stock_receipts",
            "supplier_stock_receipt_items",
        };

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "supplier_stock",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(12,3)", nullable: false),
                    QuantityInitial = table.Column<decimal>(type: "numeric(12,3)", nullable: false),
                    BatchNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "safe"),
                    SourceType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: true),
                    AddedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    AddedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    LastCheckedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_stock", x => x.Id);
                    table.ForeignKey(
                        name: "FK_supplier_stock_locations_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supplier_stock_supplier_items_SupplierItemId",
                        column: x => x.SupplierItemId,
                        principalTable: "supplier_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supplier_stock_receipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "draft"),
                    Reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ReceivedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_stock_receipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_supplier_stock_receipts_locations_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supplier_stock_movements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    MovementType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SupplierStockId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromWarehouseId = table.Column<Guid>(type: "uuid", nullable: true),
                    ToWarehouseId = table.Column<Guid>(type: "uuid", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(12,3)", nullable: false),
                    QuantityBefore = table.Column<decimal>(type: "numeric(12,3)", nullable: false),
                    QuantityAfter = table.Column<decimal>(type: "numeric(12,3)", nullable: false),
                    ReferenceType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ReferenceId = table.Column<Guid>(type: "uuid", nullable: true),
                    PerformedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_stock_movements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_supplier_stock_movements_locations_FromWarehouseId",
                        column: x => x.FromWarehouseId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supplier_stock_movements_locations_ToWarehouseId",
                        column: x => x.ToWarehouseId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supplier_stock_movements_supplier_items_SupplierItemId",
                        column: x => x.SupplierItemId,
                        principalTable: "supplier_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supplier_stock_movements_supplier_stock_SupplierStockId",
                        column: x => x.SupplierStockId,
                        principalTable: "supplier_stock",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supplier_stock_receipt_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    ReceiptId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(12,3)", nullable: false),
                    BatchNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UnitCost = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    Notes = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_stock_receipt_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_supplier_stock_receipt_items_supplier_items_SupplierItemId",
                        column: x => x.SupplierItemId,
                        principalTable: "supplier_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supplier_stock_receipt_items_supplier_stock_receipts_Receip~",
                        column: x => x.ReceiptId,
                        principalTable: "supplier_stock_receipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_supplier_stock_fefo",
                table: "supplier_stock",
                columns: new[] { "TenantId", "WarehouseId", "SupplierItemId", "ExpiryDate" },
                filter: "\"Quantity\" > 0");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_stock_SupplierItemId",
                table: "supplier_stock",
                column: "SupplierItemId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_stock_WarehouseId",
                table: "supplier_stock",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_stock_movements_FromWarehouseId",
                table: "supplier_stock_movements",
                column: "FromWarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_stock_movements_SupplierItemId",
                table: "supplier_stock_movements",
                column: "SupplierItemId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_stock_movements_SupplierStockId",
                table: "supplier_stock_movements",
                column: "SupplierStockId");

            migrationBuilder.CreateIndex(
                name: "ix_supplier_stock_movements_tenant_created",
                table: "supplier_stock_movements",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "ix_supplier_stock_movements_tenant_stock",
                table: "supplier_stock_movements",
                columns: new[] { "TenantId", "SupplierStockId" });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_stock_movements_ToWarehouseId",
                table: "supplier_stock_movements",
                column: "ToWarehouseId");

            migrationBuilder.CreateIndex(
                name: "ix_supplier_stock_receipt_items_receipt",
                table: "supplier_stock_receipt_items",
                column: "ReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_stock_receipt_items_SupplierItemId",
                table: "supplier_stock_receipt_items",
                column: "SupplierItemId");

            migrationBuilder.CreateIndex(
                name: "ix_supplier_stock_receipt_items_tenant",
                table: "supplier_stock_receipt_items",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "ix_supplier_stock_receipts_tenant_warehouse_status",
                table: "supplier_stock_receipts",
                columns: new[] { "TenantId", "WarehouseId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_stock_receipts_WarehouseId",
                table: "supplier_stock_receipts",
                column: "WarehouseId");

            // ── RLS: the four new supplier inventory tables ────────────────────
            // New tables — the triad does NOT auto-inherit; add all three explicitly in the
            // same migration that enables FORCE RLS (feedback-rls-worker-bypass-missing).
            // tenant_isolation is NULLIF-guarded and fail-closed (no `IS NULL OR` branch), with
            // a WITH CHECK mirroring USING. NO store_scope policy — supplier tenants have no
            // user_locations model (see class summary).
            foreach (var t in Tables)
            {
                migrationBuilder.Sql($@"
                    ALTER TABLE {t} ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE {t} FORCE ROW LEVEL SECURITY;
                    CREATE POLICY tenant_isolation ON {t}
                      USING (""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid)
                      WITH CHECK (""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);
                    CREATE POLICY provider_bypass ON {t}
                      USING (current_setting('app.role', true) IN ('provider', 'provider_admin'));
                    CREATE POLICY worker_bypass ON {t}
                      USING (current_setting('app.role', true) = 'worker');
                ");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var t in Tables)
            {
                migrationBuilder.Sql($@"
                    DROP POLICY IF EXISTS worker_bypass ON {t};
                    DROP POLICY IF EXISTS provider_bypass ON {t};
                    DROP POLICY IF EXISTS tenant_isolation ON {t};
                    ALTER TABLE {t} DISABLE ROW LEVEL SECURITY;
                ");
            }

            migrationBuilder.DropTable(
                name: "supplier_stock_movements");

            migrationBuilder.DropTable(
                name: "supplier_stock_receipt_items");

            migrationBuilder.DropTable(
                name: "supplier_stock");

            migrationBuilder.DropTable(
                name: "supplier_stock_receipts");
        }
    }
}
