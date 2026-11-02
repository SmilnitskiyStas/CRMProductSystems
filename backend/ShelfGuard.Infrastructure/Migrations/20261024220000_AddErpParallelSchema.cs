using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddErpParallelSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_product_stock_TenantId_Id_ProductId_LocationId",
                table: "product_stock",
                columns: new[] { "TenantId", "Id", "ProductId", "LocationId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_locations_TenantId_Id",
                table: "locations",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_location_zones_LocationId_Id",
                table: "location_zones",
                columns: new[] { "LocationId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_legal_entities_TenantId_Id",
                table: "legal_entities",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_items_TenantId_Id",
                table: "items",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateTable(
                name: "business_documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentKey = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    DocumentType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "draft"),
                    LegalEntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceSystem = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ReversesDocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PostedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_business_documents", x => x.Id);
                    table.UniqueConstraint("AK_business_documents_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_business_documents_currency", "\"Currency\" ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("CK_business_documents_hash", "\"ContentHash\" IS NULL OR \"ContentHash\" ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("CK_business_documents_identity", "\"DocumentKey\"<>'00000000-0000-0000-0000-000000000000'::uuid AND length(btrim(\"DocumentType\"))>0 AND length(btrim(\"SourceSystem\"))>0 AND (\"ReversesDocumentId\" IS NULL OR \"ReversesDocumentId\"<>\"Id\")");
                    table.CheckConstraint("CK_business_documents_posting_snapshot", "(\"Status\" IN ('posted','reversed')) = (\"PostedAt\" IS NOT NULL) AND (\"Status\" NOT IN ('posted','reversed') OR \"ContentHash\" IS NOT NULL)");
                    table.CheckConstraint("CK_business_documents_scope", "\"Id\"<>'00000000-0000-0000-0000-000000000000'::uuid AND \"TenantId\"<>'00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_business_documents_status", "\"Status\" IN ('draft','submitted','approved','posted','cancelled','reversed')");
                    table.CheckConstraint("CK_business_documents_version", "\"Version\">0");
                    table.ForeignKey(
                        name: "FK_business_documents_business_documents_TenantId_ReversesDocu~",
                        columns: x => new { x.TenantId, x.ReversesDocumentId },
                        principalTable: "business_documents",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_business_documents_legal_entities_TenantId_LegalEntityId",
                        columns: x => new { x.TenantId, x.LegalEntityId },
                        principalTable: "legal_entities",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_business_documents_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inbox_deduplication",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Consumer = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Result = table.Column<string>(type: "jsonb", nullable: true),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbox_deduplication", x => x.Id);
                    table.CheckConstraint("CK_inbox_deduplication_identity", "\"MessageId\"<>'00000000-0000-0000-0000-000000000000'::uuid AND length(btrim(\"Consumer\"))>0 AND \"PayloadHash\" ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("CK_inbox_deduplication_scope", "\"Id\"<>'00000000-0000-0000-0000-000000000000'::uuid AND \"TenantId\"<>'00000000-0000-0000-0000-000000000000'::uuid");
                    table.ForeignKey(
                        name: "FK_inbox_deduplication_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inventory_reconciliations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    LegacyQuantity = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    LedgerQuantity = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    Difference = table.Column<decimal>(type: "numeric(18,4)", nullable: true, computedColumnSql: "\"LegacyQuantity\"-\"LedgerQuantity\"", stored: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Explanation = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    AsOf = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_reconciliations", x => x.Id);
                    table.CheckConstraint("CK_inventory_reconciliations_finite", "(\"LegacyQuantity\" IS NULL OR \"LegacyQuantity\"<>'NaN'::numeric) AND (\"LedgerQuantity\" IS NULL OR \"LedgerQuantity\"<>'NaN'::numeric) AND (\"Difference\" IS NULL OR \"Difference\"<>'NaN'::numeric)");
                    table.CheckConstraint("CK_inventory_reconciliations_result", "(\"Status\"='unverifiable' AND (\"LegacyQuantity\" IS NULL OR \"LedgerQuantity\" IS NULL)) OR (\"LegacyQuantity\" IS NOT NULL AND \"LedgerQuantity\" IS NOT NULL AND ((\"Status\"='matched' AND \"LegacyQuantity\"=\"LedgerQuantity\") OR (\"Status\"='discrepancy' AND \"LegacyQuantity\"<>\"LedgerQuantity\")))");
                    table.CheckConstraint("CK_inventory_reconciliations_scope", "\"Id\"<>'00000000-0000-0000-0000-000000000000'::uuid AND \"TenantId\"<>'00000000-0000-0000-0000-000000000000'::uuid");
                    table.ForeignKey(
                        name: "FK_inventory_reconciliations_items_TenantId_ItemId",
                        columns: x => new { x.TenantId, x.ItemId },
                        principalTable: "items",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_reconciliations_locations_TenantId_LocationId",
                        columns: x => new { x.TenantId, x.LocationId },
                        principalTable: "locations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_reconciliations_product_stock_TenantId_BatchId_It~",
                        columns: x => new { x.TenantId, x.BatchId, x.ItemId, x.LocationId },
                        principalTable: "product_stock",
                        principalColumns: new[] { "TenantId", "Id", "ProductId", "LocationId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_reconciliations_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "document_approvals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Stage = table.Column<int>(type: "integer", nullable: false),
                    ApproverId = table.Column<Guid>(type: "uuid", nullable: false),
                    Decision = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_approvals", x => x.Id);
                    table.CheckConstraint("CK_document_approvals_decision", "\"Stage\">0 AND \"ApproverId\"<>'00000000-0000-0000-0000-000000000000'::uuid AND \"Decision\" IN ('approved','rejected') AND \"ContentHash\" ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("CK_document_approvals_scope", "\"Id\"<>'00000000-0000-0000-0000-000000000000'::uuid AND \"TenantId\"<>'00000000-0000-0000-0000-000000000000'::uuid");
                    table.ForeignKey(
                        name: "FK_document_approvals_business_documents_TenantId_DocumentId",
                        columns: x => new { x.TenantId, x.DocumentId },
                        principalTable: "business_documents",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_document_approvals_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "document_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineNumber = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromLocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ToLocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    Unit = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    VatRate = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    VatAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    UnitCost = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    ItemNameSnapshot = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    BarcodeSnapshot = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    BatchNumberSnapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ExpiryDateSnapshot = table.Column<DateOnly>(type: "date", nullable: true),
                    Metadata = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_lines", x => x.Id);
                    table.UniqueConstraint("AK_document_lines_TenantId_DocumentId_Id_ItemId", x => new { x.TenantId, x.DocumentId, x.Id, x.ItemId });
                    table.CheckConstraint("CK_document_lines_finite", "(\"Quantity\" IS NULL OR \"Quantity\"<>'NaN'::numeric) AND (\"UnitPrice\" IS NULL OR \"UnitPrice\"<>'NaN'::numeric) AND (\"VatRate\" IS NULL OR \"VatRate\"<>'NaN'::numeric) AND (\"VatAmount\" IS NULL OR \"VatAmount\"<>'NaN'::numeric) AND (\"UnitCost\" IS NULL OR \"UnitCost\"<>'NaN'::numeric)");
                    table.CheckConstraint("CK_document_lines_scope", "\"Id\"<>'00000000-0000-0000-0000-000000000000'::uuid AND \"TenantId\"<>'00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_document_lines_snapshot", "length(btrim(\"Unit\"))>0 AND length(btrim(\"ItemNameSnapshot\"))>0");
                    table.CheckConstraint("CK_document_lines_values", "\"LineNumber\">0 AND \"Quantity\">0 AND (\"UnitPrice\" IS NULL OR \"UnitPrice\">=0) AND (\"UnitCost\" IS NULL OR \"UnitCost\">=0) AND (\"VatRate\" IS NULL OR \"VatRate\" BETWEEN 0 AND 100) AND (\"VatAmount\" IS NULL OR \"VatAmount\">=0)");
                    table.ForeignKey(
                        name: "FK_document_lines_business_documents_TenantId_DocumentId",
                        columns: x => new { x.TenantId, x.DocumentId },
                        principalTable: "business_documents",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_document_lines_items_TenantId_ItemId",
                        columns: x => new { x.TenantId, x.ItemId },
                        principalTable: "items",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_document_lines_locations_TenantId_FromLocationId",
                        columns: x => new { x.TenantId, x.FromLocationId },
                        principalTable: "locations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_document_lines_locations_TenantId_ToLocationId",
                        columns: x => new { x.TenantId, x.ToLocationId },
                        principalTable: "locations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_document_lines_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "document_links",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    RelationType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_links", x => x.Id);
                    table.CheckConstraint("CK_document_links_relation", "\"FromDocumentId\"<>\"ToDocumentId\" AND length(btrim(\"RelationType\"))>0");
                    table.CheckConstraint("CK_document_links_scope", "\"Id\"<>'00000000-0000-0000-0000-000000000000'::uuid AND \"TenantId\"<>'00000000-0000-0000-0000-000000000000'::uuid");
                    table.ForeignKey(
                        name: "FK_document_links_business_documents_TenantId_FromDocumentId",
                        columns: x => new { x.TenantId, x.FromDocumentId },
                        principalTable: "business_documents",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_document_links_business_documents_TenantId_ToDocumentId",
                        columns: x => new { x.TenantId, x.ToDocumentId },
                        principalTable: "business_documents",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_document_links_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "document_postings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReversesPostingId = table.Column<Guid>(type: "uuid", nullable: true),
                    PerformedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    PostedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_postings", x => x.Id);
                    table.UniqueConstraint("AK_document_postings_TenantId_DocumentId_Id", x => new { x.TenantId, x.DocumentId, x.Id });
                    table.UniqueConstraint("AK_document_postings_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_document_postings_identity", "length(btrim(\"IdempotencyKey\"))>0 AND \"RequestHash\" ~ '^[0-9a-f]{64}$' AND (\"ReversesPostingId\" IS NULL OR \"ReversesPostingId\"<>\"Id\")");
                    table.CheckConstraint("CK_document_postings_scope", "\"Id\"<>'00000000-0000-0000-0000-000000000000'::uuid AND \"TenantId\"<>'00000000-0000-0000-0000-000000000000'::uuid");
                    table.ForeignKey(
                        name: "FK_document_postings_business_documents_TenantId_DocumentId",
                        columns: x => new { x.TenantId, x.DocumentId },
                        principalTable: "business_documents",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_document_postings_document_postings_TenantId_ReversesPostin~",
                        columns: x => new { x.TenantId, x.ReversesPostingId },
                        principalTable: "document_postings",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_document_postings_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inventory_reservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    RemainingQuantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_reservations", x => x.Id);
                    table.CheckConstraint("CK_inventory_reservations_finite", "(\"Quantity\" IS NULL OR \"Quantity\"<>'NaN'::numeric) AND (\"RemainingQuantity\" IS NULL OR \"RemainingQuantity\"<>'NaN'::numeric)");
                    table.CheckConstraint("CK_inventory_reservations_quantities", "\"Quantity\">0 AND \"RemainingQuantity\">=0 AND \"RemainingQuantity\"<=\"Quantity\" AND length(btrim(\"IdempotencyKey\"))>0");
                    table.CheckConstraint("CK_inventory_reservations_scope", "\"Id\"<>'00000000-0000-0000-0000-000000000000'::uuid AND \"TenantId\"<>'00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_inventory_reservations_status", "(\"Status\"='active' AND \"RemainingQuantity\">0) OR (\"Status\" IN ('consumed','released','expired') AND \"RemainingQuantity\"=0)");
                    table.ForeignKey(
                        name: "FK_inventory_reservations_business_documents_TenantId_Document~",
                        columns: x => new { x.TenantId, x.DocumentId },
                        principalTable: "business_documents",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_reservations_document_lines_TenantId_DocumentId_D~",
                        columns: x => new { x.TenantId, x.DocumentId, x.DocumentLineId, x.ItemId },
                        principalTable: "document_lines",
                        principalColumns: new[] { "TenantId", "DocumentId", "Id", "ItemId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_reservations_items_TenantId_ItemId",
                        columns: x => new { x.TenantId, x.ItemId },
                        principalTable: "items",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_reservations_locations_TenantId_LocationId",
                        columns: x => new { x.TenantId, x.LocationId },
                        principalTable: "locations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_reservations_product_stock_TenantId_BatchId_ItemI~",
                        columns: x => new { x.TenantId, x.BatchId, x.ItemId, x.LocationId },
                        principalTable: "product_stock",
                        principalColumns: new[] { "TenantId", "Id", "ProductId", "LocationId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_reservations_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inventory_ledger_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    PostingId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntryNumber = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ZoneId = table.Column<Guid>(type: "uuid", nullable: true),
                    QuantityDelta = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    QuantityBefore = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    QuantityAfter = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    UnitCost = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    ValueDelta = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    BatchNumberSnapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ExpiryDateSnapshot = table.Column<DateOnly>(type: "date", nullable: true),
                    ReversesEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_ledger_entries", x => x.Id);
                    table.UniqueConstraint("AK_inventory_ledger_entries_TenantId_BatchId_Id", x => new { x.TenantId, x.BatchId, x.Id });
                    table.UniqueConstraint("AK_inventory_ledger_entries_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_inventory_ledger_entries_balance", "(\"QuantityBefore\" IS NULL AND \"QuantityAfter\" IS NULL) OR (\"QuantityBefore\" IS NOT NULL AND \"QuantityAfter\" IS NOT NULL AND \"QuantityBefore\">=0 AND \"QuantityAfter\">=0 AND \"QuantityAfter\"=\"QuantityBefore\"+\"QuantityDelta\")");
                    table.CheckConstraint("CK_inventory_ledger_entries_delta", "\"EntryNumber\">0 AND \"QuantityDelta\"<>0 AND \"Currency\" ~ '^[A-Z]{3}$' AND (\"ReversesEntryId\" IS NULL OR \"ReversesEntryId\"<>\"Id\")");
                    table.CheckConstraint("CK_inventory_ledger_entries_finite", "(\"QuantityDelta\" IS NULL OR \"QuantityDelta\"<>'NaN'::numeric) AND (\"QuantityBefore\" IS NULL OR \"QuantityBefore\"<>'NaN'::numeric) AND (\"QuantityAfter\" IS NULL OR \"QuantityAfter\"<>'NaN'::numeric) AND (\"UnitCost\" IS NULL OR \"UnitCost\"<>'NaN'::numeric) AND (\"ValueDelta\" IS NULL OR \"ValueDelta\"<>'NaN'::numeric)");
                    table.CheckConstraint("CK_inventory_ledger_entries_scope", "\"Id\"<>'00000000-0000-0000-0000-000000000000'::uuid AND \"TenantId\"<>'00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_inventory_ledger_entries_value", "(\"UnitCost\" IS NULL OR \"UnitCost\">=0) AND (\"ValueDelta\" IS NULL OR (\"UnitCost\" IS NOT NULL AND \"ValueDelta\"=round(\"QuantityDelta\"*\"UnitCost\",4)))");
                    table.ForeignKey(
                        name: "FK_inventory_ledger_entries_document_lines_TenantId_DocumentId~",
                        columns: x => new { x.TenantId, x.DocumentId, x.DocumentLineId, x.ItemId },
                        principalTable: "document_lines",
                        principalColumns: new[] { "TenantId", "DocumentId", "Id", "ItemId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_ledger_entries_document_postings_TenantId_Documen~",
                        columns: x => new { x.TenantId, x.DocumentId, x.PostingId },
                        principalTable: "document_postings",
                        principalColumns: new[] { "TenantId", "DocumentId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_ledger_entries_inventory_ledger_entries_TenantId_~",
                        columns: x => new { x.TenantId, x.ReversesEntryId },
                        principalTable: "inventory_ledger_entries",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_ledger_entries_items_TenantId_ItemId",
                        columns: x => new { x.TenantId, x.ItemId },
                        principalTable: "items",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_ledger_entries_location_zones_LocationId_ZoneId",
                        columns: x => new { x.LocationId, x.ZoneId },
                        principalTable: "location_zones",
                        principalColumns: new[] { "LocationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_ledger_entries_locations_TenantId_LocationId",
                        columns: x => new { x.TenantId, x.LocationId },
                        principalTable: "locations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_ledger_entries_product_stock_TenantId_BatchId_Ite~",
                        columns: x => new { x.TenantId, x.BatchId, x.ItemId, x.LocationId },
                        principalTable: "product_stock",
                        principalColumns: new[] { "TenantId", "Id", "ProductId", "LocationId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_ledger_entries_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "outbox_events",
                columns: table => new
                {
                    DeliveryScope = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "live"),
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PostingId = table.Column<Guid>(type: "uuid", nullable: true),
                    EventType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    AggregateType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AggregateId = table.Column<Guid>(type: "uuid", nullable: false),
                    AggregateVersion = table.Column<int>(type: "integer", nullable: false),
                    EventOrdinal = table.Column<int>(type: "integer", nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: true),
                    CausationId = table.Column<Guid>(type: "uuid", nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "pending"),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    AvailableAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    LockToken = table.Column<Guid>(type: "uuid", nullable: true),
                    LockedUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_events", x => x.Id);
                    table.CheckConstraint("CK_outbox_events_delivery", "\"Attempts\">=0 AND ((\"DeliveryScope\"='live' AND \"Status\" IN ('pending','processing','published','dead')) OR (\"DeliveryScope\"='historical' AND \"Status\"='suppressed' AND \"Attempts\"=0)) AND ((\"Status\"='processing' AND \"LockToken\" IS NOT NULL AND \"LockedUntil\" IS NOT NULL) OR (\"Status\"<>'processing' AND \"LockToken\" IS NULL AND \"LockedUntil\" IS NULL)) AND ((\"Status\"='published')=(\"PublishedAt\" IS NOT NULL))");
                    table.CheckConstraint("CK_outbox_events_envelope", "\"AggregateId\"<>'00000000-0000-0000-0000-000000000000'::uuid AND \"SchemaVersion\">0 AND \"AggregateVersion\">0 AND \"EventOrdinal\">0 AND length(btrim(\"EventType\"))>0 AND length(btrim(\"AggregateType\"))>0 AND jsonb_typeof(\"Payload\")='object'");
                    table.CheckConstraint("CK_outbox_events_scope", "\"Id\"<>'00000000-0000-0000-0000-000000000000'::uuid AND \"TenantId\"<>'00000000-0000-0000-0000-000000000000'::uuid");
                    table.ForeignKey(
                        name: "FK_outbox_events_document_postings_TenantId_PostingId",
                        columns: x => new { x.TenantId, x.PostingId },
                        principalTable: "document_postings",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_outbox_events_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inventory_balance_projections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    ReservedQuantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    LastLedgerEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_balance_projections", x => x.Id);
                    table.CheckConstraint("CK_inventory_balance_projections_finite", "(\"Quantity\" IS NULL OR \"Quantity\"<>'NaN'::numeric) AND (\"ReservedQuantity\" IS NULL OR \"ReservedQuantity\"<>'NaN'::numeric)");
                    table.CheckConstraint("CK_inventory_balance_projections_quantities", "\"Quantity\">=0 AND \"ReservedQuantity\">=0 AND \"ReservedQuantity\"<=\"Quantity\"");
                    table.CheckConstraint("CK_inventory_balance_projections_scope", "\"Id\"<>'00000000-0000-0000-0000-000000000000'::uuid AND \"TenantId\"<>'00000000-0000-0000-0000-000000000000'::uuid");
                    table.ForeignKey(
                        name: "FK_inventory_balance_projections_inventory_ledger_entries_Tena~",
                        columns: x => new { x.TenantId, x.BatchId, x.LastLedgerEntryId },
                        principalTable: "inventory_ledger_entries",
                        principalColumns: new[] { "TenantId", "BatchId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_balance_projections_items_TenantId_ItemId",
                        columns: x => new { x.TenantId, x.ItemId },
                        principalTable: "items",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_balance_projections_locations_TenantId_LocationId",
                        columns: x => new { x.TenantId, x.LocationId },
                        principalTable: "locations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_balance_projections_product_stock_TenantId_BatchI~",
                        columns: x => new { x.TenantId, x.BatchId, x.ItemId, x.LocationId },
                        principalTable: "product_stock",
                        principalColumns: new[] { "TenantId", "Id", "ProductId", "LocationId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_balance_projections_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_business_documents_TenantId_DocumentKey_Version",
                table: "business_documents",
                columns: new[] { "TenantId", "DocumentKey", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_business_documents_TenantId_DocumentType_Number",
                table: "business_documents",
                columns: new[] { "TenantId", "DocumentType", "Number" });

            migrationBuilder.CreateIndex(
                name: "IX_business_documents_TenantId_LegalEntityId",
                table: "business_documents",
                columns: new[] { "TenantId", "LegalEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_business_documents_TenantId_ReversesDocumentId",
                table: "business_documents",
                columns: new[] { "TenantId", "ReversesDocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_business_documents_TenantId_SourceSystem_SourceId_Version",
                table: "business_documents",
                columns: new[] { "TenantId", "SourceSystem", "SourceId", "Version" },
                unique: true,
                filter: "\"SourceId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_business_documents_TenantId_Status_CreatedAt",
                table: "business_documents",
                columns: new[] { "TenantId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_document_approvals_TenantId_DocumentId_Stage_ApproverId",
                table: "document_approvals",
                columns: new[] { "TenantId", "DocumentId", "Stage", "ApproverId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_document_lines_TenantId_DocumentId_LineNumber",
                table: "document_lines",
                columns: new[] { "TenantId", "DocumentId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_document_lines_TenantId_FromLocationId",
                table: "document_lines",
                columns: new[] { "TenantId", "FromLocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_document_lines_TenantId_ItemId",
                table: "document_lines",
                columns: new[] { "TenantId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_document_lines_TenantId_ToLocationId",
                table: "document_lines",
                columns: new[] { "TenantId", "ToLocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_document_links_TenantId_FromDocumentId_ToDocumentId_Relatio~",
                table: "document_links",
                columns: new[] { "TenantId", "FromDocumentId", "ToDocumentId", "RelationType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_document_links_TenantId_ToDocumentId",
                table: "document_links",
                columns: new[] { "TenantId", "ToDocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_document_postings_TenantId_DocumentId",
                table: "document_postings",
                columns: new[] { "TenantId", "DocumentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_document_postings_TenantId_IdempotencyKey",
                table: "document_postings",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_document_postings_TenantId_ReversesPostingId",
                table: "document_postings",
                columns: new[] { "TenantId", "ReversesPostingId" },
                unique: true,
                filter: "\"ReversesPostingId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_inbox_deduplication_TenantId_Consumer_MessageId",
                table: "inbox_deduplication",
                columns: new[] { "TenantId", "Consumer", "MessageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_balance_projections_TenantId_BatchId",
                table: "inventory_balance_projections",
                columns: new[] { "TenantId", "BatchId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_balance_projections_TenantId_BatchId_ItemId_Locat~",
                table: "inventory_balance_projections",
                columns: new[] { "TenantId", "BatchId", "ItemId", "LocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_balance_projections_TenantId_BatchId_LastLedgerEn~",
                table: "inventory_balance_projections",
                columns: new[] { "TenantId", "BatchId", "LastLedgerEntryId" });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_balance_projections_TenantId_ItemId",
                table: "inventory_balance_projections",
                columns: new[] { "TenantId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_balance_projections_TenantId_LocationId_ItemId",
                table: "inventory_balance_projections",
                columns: new[] { "TenantId", "LocationId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_ledger_entries_LocationId_ZoneId",
                table: "inventory_ledger_entries",
                columns: new[] { "LocationId", "ZoneId" });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_ledger_entries_TenantId_BatchId_ItemId_LocationId",
                table: "inventory_ledger_entries",
                columns: new[] { "TenantId", "BatchId", "ItemId", "LocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_ledger_entries_TenantId_DocumentId_DocumentLineId~",
                table: "inventory_ledger_entries",
                columns: new[] { "TenantId", "DocumentId", "DocumentLineId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_ledger_entries_TenantId_DocumentId_PostingId",
                table: "inventory_ledger_entries",
                columns: new[] { "TenantId", "DocumentId", "PostingId" });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_ledger_entries_TenantId_ItemId",
                table: "inventory_ledger_entries",
                columns: new[] { "TenantId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_ledger_entries_TenantId_LocationId_ItemId_BatchId~",
                table: "inventory_ledger_entries",
                columns: new[] { "TenantId", "LocationId", "ItemId", "BatchId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_ledger_entries_TenantId_PostingId_EntryNumber",
                table: "inventory_ledger_entries",
                columns: new[] { "TenantId", "PostingId", "EntryNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_ledger_entries_TenantId_ReversesEntryId",
                table: "inventory_ledger_entries",
                columns: new[] { "TenantId", "ReversesEntryId" },
                unique: true,
                filter: "\"ReversesEntryId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_reconciliations_TenantId_BatchId_ItemId_LocationId",
                table: "inventory_reconciliations",
                columns: new[] { "TenantId", "BatchId", "ItemId", "LocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_reconciliations_TenantId_ItemId",
                table: "inventory_reconciliations",
                columns: new[] { "TenantId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_reconciliations_TenantId_LocationId",
                table: "inventory_reconciliations",
                columns: new[] { "TenantId", "LocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_reconciliations_TenantId_RunId_BatchId",
                table: "inventory_reconciliations",
                columns: new[] { "TenantId", "RunId", "BatchId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_reconciliations_TenantId_Status_AsOf",
                table: "inventory_reconciliations",
                columns: new[] { "TenantId", "Status", "AsOf" });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_reservations_Status_ExpiresAt",
                table: "inventory_reservations",
                columns: new[] { "Status", "ExpiresAt" },
                filter: "\"Status\"='active'");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_reservations_TenantId_BatchId_ItemId_LocationId",
                table: "inventory_reservations",
                columns: new[] { "TenantId", "BatchId", "ItemId", "LocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_reservations_TenantId_DocumentId_DocumentLineId_I~",
                table: "inventory_reservations",
                columns: new[] { "TenantId", "DocumentId", "DocumentLineId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_reservations_TenantId_IdempotencyKey",
                table: "inventory_reservations",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_reservations_TenantId_ItemId",
                table: "inventory_reservations",
                columns: new[] { "TenantId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_reservations_TenantId_LocationId_ItemId_Status",
                table: "inventory_reservations",
                columns: new[] { "TenantId", "LocationId", "ItemId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_outbox_events_AvailableAt_CreatedAt",
                table: "outbox_events",
                columns: new[] { "AvailableAt", "CreatedAt" },
                filter: "\"Status\"='pending'");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_events_LockedUntil",
                table: "outbox_events",
                column: "LockedUntil",
                filter: "\"Status\"='processing'");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_events_TenantId_AggregateType_AggregateId_AggregateV~",
                table: "outbox_events",
                columns: new[] { "TenantId", "AggregateType", "AggregateId", "AggregateVersion", "EventOrdinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_outbox_events_TenantId_PostingId",
                table: "outbox_events",
                columns: new[] { "TenantId", "PostingId" },
                unique: true,
                filter: "\"PostingId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_events_TenantId_Status_CreatedAt",
                table: "outbox_events",
                columns: new[] { "TenantId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_business_documents_active_version",
                table: "business_documents",
                columns: new[] { "TenantId", "DocumentKey" },
                unique: true,
                filter: "\"Status\"='posted'");

            InstallGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("Retain ERP documents, ledger and delivery identities. Roll back the application while preserving this additive schema.");
    }
}
