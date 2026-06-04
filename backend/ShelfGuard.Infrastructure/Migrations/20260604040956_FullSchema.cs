using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FullSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "activity_logs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    Meta = table.Column<string>(type: "jsonb", nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    IsImpersonated = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_activity_logs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_categories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_categories_categories_ParentId",
                        column: x => x.ParentId,
                        principalTable: "categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_categories_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "discounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductStockId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    DiscountPercent = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    PriceOriginal = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    PriceDiscounted = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    Reason = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "expiry"),
                    ValidFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    ValidUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "pending"),
                    AutoApplied = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    WebhookSentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_discounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "notification_queue",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Channel = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Payload = table.Column<string>(type: "jsonb", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "pending"),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_queue", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "notification_settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Channel = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_settings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_notification_settings_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "stock_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ProductStockId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceDeviceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    QuantityDelta = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    Confidence = table.Column<int>(type: "integer", nullable: false, defaultValue: 100),
                    Meta = table.Column<string>(type: "jsonb", nullable: true),
                    PerformedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "stock_movements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    MovementType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ProductStockId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromStoreId = table.Column<Guid>(type: "uuid", nullable: true),
                    ToStoreId = table.Column<Guid>(type: "uuid", nullable: true),
                    FromZoneId = table.Column<Guid>(type: "uuid", nullable: true),
                    ToZoneId = table.Column<Guid>(type: "uuid", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    QuantityBefore = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    QuantityAfter = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    UnitPrice = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    TotalAmount = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    ReferenceId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReferenceType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    PerformedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_movements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "stock_receipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    DestinationStoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    ViaCentralStore = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "draft"),
                    ExpectedAt = table.Column<DateOnly>(type: "date", nullable: true),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ReceivedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_receipts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "stock_transfers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromStoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToStoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransferType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "draft"),
                    InitiatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ConfirmedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_transfers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "stores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Address = table.Column<string>(type: "text", nullable: true),
                    Latitude = table.Column<decimal>(type: "numeric(10,7)", nullable: true),
                    Longitude = table.Column<decimal>(type: "numeric(10,7)", nullable: true),
                    Type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    FloorPlan = table.Column<string>(type: "jsonb", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_stores_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "suppliers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Edrpou = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ContactPerson = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    DeliveryDays = table.Column<int>(type: "integer", nullable: false, defaultValue: 3),
                    HasSupplierPortal = table.Column<bool>(type: "boolean", nullable: false),
                    ReturnPolicy = table.Column<bool>(type: "boolean", nullable: false),
                    PaymentTerms = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_suppliers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_suppliers_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "write_offs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "draft"),
                    Reason = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    TotalLossAmount = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    PdfUrl = table.Column<string>(type: "text", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    ApprovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_write_offs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "product_segments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_segments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_product_segments_categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_product_segments_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_receipt_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    ReceiptId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuantityOrdered = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    QuantityReceived = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    PricePurchase = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    BatchNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DiscrepancyNotes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_receipt_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_stock_receipt_items_stock_receipts_ReceiptId",
                        column: x => x.ReceiptId,
                        principalTable: "stock_receipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "stock_transfer_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TransferId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductStockId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: false),
                    BatchNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_transfer_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_stock_transfer_items_stock_transfers_TransferId",
                        column: x => x.TransferId,
                        principalTable: "stock_transfers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "store_zones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Position = table.Column<string>(type: "jsonb", nullable: true),
                    ShelvesCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    TempMin = table.Column<decimal>(type: "numeric(5,1)", nullable: true),
                    TempMax = table.Column<decimal>(type: "numeric(5,1)", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_store_zones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_store_zones_stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "write_off_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    WriteOffId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductStockId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    LossAmount = table.Column<decimal>(type: "numeric(12,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_write_off_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_write_off_items_write_offs_WriteOffId",
                        column: x => x.WriteOffId,
                        principalTable: "write_offs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "catalog_products",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Barcode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    SegmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "шт"),
                    ManagementType = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "MTS"),
                    MinStock = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    MaxStock = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    SafetyBuffer = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    StorageTempMin = table.Column<decimal>(type: "numeric(5,1)", nullable: true),
                    StorageTempMax = table.Column<decimal>(type: "numeric(5,1)", nullable: true),
                    ShelfLifeDays = table.Column<int>(type: "integer", nullable: true),
                    DefaultSupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    VatRate = table.Column<decimal>(type: "numeric(5,2)", nullable: false, defaultValue: 20m),
                    PricePurchase = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    PriceRetail = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    ImageUrl = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_catalog_products", x => x.Id);
                    table.ForeignKey(
                        name: "FK_catalog_products_categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_catalog_products_product_segments_SegmentId",
                        column: x => x.SegmentId,
                        principalTable: "product_segments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_catalog_products_suppliers_DefaultSupplierId",
                        column: x => x.DefaultSupplierId,
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_catalog_products_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "product_stock",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    ZoneId = table.Column<Guid>(type: "uuid", nullable: true),
                    ShelfNumber = table.Column<int>(type: "integer", nullable: true),
                    BatchNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    QuantityInitial = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "safe"),
                    SourceType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: true),
                    AddedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    AddedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    LastCheckedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    NotifiedWarningAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NotifiedCriticalAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_stock", x => x.Id);
                    table.ForeignKey(
                        name: "FK_product_stock_catalog_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "catalog_products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_product_stock_store_zones_ZoneId",
                        column: x => x.ZoneId,
                        principalTable: "store_zones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_product_stock_stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "product_supplier_settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    Moq = table.Column<decimal>(type: "numeric(10,2)", nullable: false, defaultValue: 1m),
                    Usq = table.Column<decimal>(type: "numeric(10,2)", nullable: false, defaultValue: 1m),
                    PricePurchase = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    DeliveryDays = table.Column<int>(type: "integer", nullable: false, defaultValue: 3),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_supplier_settings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_product_supplier_settings_catalog_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "catalog_products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_product_supplier_settings_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_catalog_products_CategoryId",
                table: "catalog_products",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_catalog_products_DefaultSupplierId",
                table: "catalog_products",
                column: "DefaultSupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_catalog_products_SegmentId",
                table: "catalog_products",
                column: "SegmentId");

            migrationBuilder.CreateIndex(
                name: "IX_catalog_products_TenantId",
                table: "catalog_products",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_categories_ParentId",
                table: "categories",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_categories_TenantId",
                table: "categories",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_notification_settings_UserId_EventType_Channel",
                table: "notification_settings",
                columns: new[] { "UserId", "EventType", "Channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_product_segments_CategoryId",
                table: "product_segments",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_product_segments_TenantId",
                table: "product_segments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_product_stock_ProductId",
                table: "product_stock",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_product_stock_StoreId",
                table: "product_stock",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_product_stock_ZoneId",
                table: "product_stock",
                column: "ZoneId");

            migrationBuilder.CreateIndex(
                name: "IX_product_supplier_settings_ProductId_SupplierId_TenantId",
                table: "product_supplier_settings",
                columns: new[] { "ProductId", "SupplierId", "TenantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_product_supplier_settings_SupplierId",
                table: "product_supplier_settings",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_receipt_items_ReceiptId",
                table: "stock_receipt_items",
                column: "ReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_transfer_items_TransferId",
                table: "stock_transfer_items",
                column: "TransferId");

            migrationBuilder.CreateIndex(
                name: "IX_store_zones_StoreId",
                table: "store_zones",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_stores_TenantId",
                table: "stores",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_suppliers_TenantId",
                table: "suppliers",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_write_off_items_WriteOffId",
                table: "write_off_items",
                column: "WriteOffId");

            // ── FEFO index (critical for expiry-first consumption) ──────────
            migrationBuilder.Sql(@"
                CREATE INDEX idx_stock_expiry_active
                  ON product_stock(""TenantId"", ""StoreId"", ""ProductId"", ""ExpiryDate"")
                  WHERE ""Quantity"" > 0 AND ""Status"" NOT IN ('sold_out', 'archived');

                CREATE INDEX idx_stock_tenant_store
                  ON product_stock(""TenantId"", ""StoreId"");
            ");

            // ── RLS: stores ─────────────────────────────────────────────────
            migrationBuilder.Sql(@"
                ALTER TABLE stores ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON stores
                  USING (""TenantId"" = current_setting('app.tenant_id', true)::uuid);
                CREATE POLICY provider_bypass ON stores
                  USING (current_setting('app.role', true) = 'provider');
            ");

            // ── RLS: store_zones (via stores.TenantId) ──────────────────────
            migrationBuilder.Sql(@"
                ALTER TABLE store_zones ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON store_zones
                  USING (EXISTS (
                    SELECT 1 FROM stores s
                    WHERE s.""Id"" = ""StoreId""
                      AND s.""TenantId"" = current_setting('app.tenant_id', true)::uuid
                  ));
                CREATE POLICY provider_bypass ON store_zones
                  USING (current_setting('app.role', true) = 'provider');
            ");

            // ── RLS: categories ─────────────────────────────────────────────
            migrationBuilder.Sql(@"
                ALTER TABLE categories ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON categories
                  USING (""TenantId"" = current_setting('app.tenant_id', true)::uuid);
                CREATE POLICY provider_bypass ON categories
                  USING (current_setting('app.role', true) = 'provider');
            ");

            // ── RLS: product_segments ────────────────────────────────────────
            migrationBuilder.Sql(@"
                ALTER TABLE product_segments ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON product_segments
                  USING (""TenantId"" = current_setting('app.tenant_id', true)::uuid);
                CREATE POLICY provider_bypass ON product_segments
                  USING (current_setting('app.role', true) = 'provider');
            ");

            // ── RLS: suppliers ───────────────────────────────────────────────
            migrationBuilder.Sql(@"
                ALTER TABLE suppliers ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON suppliers
                  USING (""TenantId"" = current_setting('app.tenant_id', true)::uuid);
                CREATE POLICY provider_bypass ON suppliers
                  USING (current_setting('app.role', true) = 'provider');
            ");

            // ── RLS: catalog_products ────────────────────────────────────────
            migrationBuilder.Sql(@"
                ALTER TABLE catalog_products ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON catalog_products
                  USING (""TenantId"" = current_setting('app.tenant_id', true)::uuid);
                CREATE POLICY provider_bypass ON catalog_products
                  USING (current_setting('app.role', true) = 'provider');
            ");

            // ── RLS: product_supplier_settings ───────────────────────────────
            migrationBuilder.Sql(@"
                ALTER TABLE product_supplier_settings ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON product_supplier_settings
                  USING (""TenantId"" = current_setting('app.tenant_id', true)::uuid);
                CREATE POLICY provider_bypass ON product_supplier_settings
                  USING (current_setting('app.role', true) = 'provider');
            ");

            // ── RLS: product_stock ───────────────────────────────────────────
            migrationBuilder.Sql(@"
                ALTER TABLE product_stock ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON product_stock
                  USING (""TenantId"" = current_setting('app.tenant_id', true)::uuid);
                CREATE POLICY provider_bypass ON product_stock
                  USING (current_setting('app.role', true) = 'provider');
            ");

            // ── RLS: stock_movements ─────────────────────────────────────────
            migrationBuilder.Sql(@"
                ALTER TABLE stock_movements ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON stock_movements
                  USING (""TenantId"" = current_setting('app.tenant_id', true)::uuid);
                CREATE POLICY provider_bypass ON stock_movements
                  USING (current_setting('app.role', true) = 'provider');
            ");

            // ── RLS: stock_events ────────────────────────────────────────────
            migrationBuilder.Sql(@"
                ALTER TABLE stock_events ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON stock_events
                  USING (""TenantId"" = current_setting('app.tenant_id', true)::uuid);
                CREATE POLICY provider_bypass ON stock_events
                  USING (current_setting('app.role', true) = 'provider');
            ");

            // ── RLS: stock_receipts ──────────────────────────────────────────
            migrationBuilder.Sql(@"
                ALTER TABLE stock_receipts ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON stock_receipts
                  USING (""TenantId"" = current_setting('app.tenant_id', true)::uuid);
                CREATE POLICY provider_bypass ON stock_receipts
                  USING (current_setting('app.role', true) = 'provider');
            ");

            // ── RLS: stock_receipt_items (via receipt TenantId) ──────────────
            migrationBuilder.Sql(@"
                ALTER TABLE stock_receipt_items ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON stock_receipt_items
                  USING (EXISTS (
                    SELECT 1 FROM stock_receipts r
                    WHERE r.""Id"" = ""ReceiptId""
                      AND r.""TenantId"" = current_setting('app.tenant_id', true)::uuid
                  ));
                CREATE POLICY provider_bypass ON stock_receipt_items
                  USING (current_setting('app.role', true) = 'provider');
            ");

            // ── RLS: stock_transfers ─────────────────────────────────────────
            migrationBuilder.Sql(@"
                ALTER TABLE stock_transfers ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON stock_transfers
                  USING (""TenantId"" = current_setting('app.tenant_id', true)::uuid);
                CREATE POLICY provider_bypass ON stock_transfers
                  USING (current_setting('app.role', true) = 'provider');
            ");

            // ── RLS: stock_transfer_items (via transfer TenantId) ────────────
            migrationBuilder.Sql(@"
                ALTER TABLE stock_transfer_items ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON stock_transfer_items
                  USING (EXISTS (
                    SELECT 1 FROM stock_transfers t
                    WHERE t.""Id"" = ""TransferId""
                      AND t.""TenantId"" = current_setting('app.tenant_id', true)::uuid
                  ));
                CREATE POLICY provider_bypass ON stock_transfer_items
                  USING (current_setting('app.role', true) = 'provider');
            ");

            // ── RLS: write_offs ──────────────────────────────────────────────
            migrationBuilder.Sql(@"
                ALTER TABLE write_offs ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON write_offs
                  USING (""TenantId"" = current_setting('app.tenant_id', true)::uuid);
                CREATE POLICY provider_bypass ON write_offs
                  USING (current_setting('app.role', true) = 'provider');
            ");

            // ── RLS: write_off_items (via write_off TenantId) ────────────────
            migrationBuilder.Sql(@"
                ALTER TABLE write_off_items ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON write_off_items
                  USING (EXISTS (
                    SELECT 1 FROM write_offs w
                    WHERE w.""Id"" = ""WriteOffId""
                      AND w.""TenantId"" = current_setting('app.tenant_id', true)::uuid
                  ));
                CREATE POLICY provider_bypass ON write_off_items
                  USING (current_setting('app.role', true) = 'provider');
            ");

            // ── RLS: discounts ───────────────────────────────────────────────
            migrationBuilder.Sql(@"
                ALTER TABLE discounts ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON discounts
                  USING (""TenantId"" = current_setting('app.tenant_id', true)::uuid);
                CREATE POLICY provider_bypass ON discounts
                  USING (current_setting('app.role', true) = 'provider');
            ");

            // ── RLS: notification_queue ──────────────────────────────────────
            migrationBuilder.Sql(@"
                ALTER TABLE notification_queue ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON notification_queue
                  USING (""TenantId"" = current_setting('app.tenant_id', true)::uuid
                         OR ""TenantId"" IS NULL);
                CREATE POLICY provider_bypass ON notification_queue
                  USING (current_setting('app.role', true) = 'provider');
            ");

            // ── RLS: activity_logs ───────────────────────────────────────────
            migrationBuilder.Sql(@"
                ALTER TABLE activity_logs ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON activity_logs
                  USING (""TenantId"" = current_setting('app.tenant_id', true)::uuid
                         OR ""TenantId"" IS NULL);
                CREATE POLICY provider_bypass ON activity_logs
                  USING (current_setting('app.role', true) = 'provider');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "activity_logs");

            migrationBuilder.DropTable(
                name: "discounts");

            migrationBuilder.DropTable(
                name: "notification_queue");

            migrationBuilder.DropTable(
                name: "notification_settings");

            migrationBuilder.DropTable(
                name: "product_stock");

            migrationBuilder.DropTable(
                name: "product_supplier_settings");

            migrationBuilder.DropTable(
                name: "stock_events");

            migrationBuilder.DropTable(
                name: "stock_movements");

            migrationBuilder.DropTable(
                name: "stock_receipt_items");

            migrationBuilder.DropTable(
                name: "stock_transfer_items");

            migrationBuilder.DropTable(
                name: "write_off_items");

            migrationBuilder.DropTable(
                name: "store_zones");

            migrationBuilder.DropTable(
                name: "catalog_products");

            migrationBuilder.DropTable(
                name: "stock_receipts");

            migrationBuilder.DropTable(
                name: "stock_transfers");

            migrationBuilder.DropTable(
                name: "write_offs");

            migrationBuilder.DropTable(
                name: "stores");

            migrationBuilder.DropTable(
                name: "product_segments");

            migrationBuilder.DropTable(
                name: "suppliers");

            migrationBuilder.DropTable(
                name: "categories");
        }
    }
}
