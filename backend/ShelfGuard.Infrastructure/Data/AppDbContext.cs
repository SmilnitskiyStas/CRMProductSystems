using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Infrastructure.Data;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    // Auth (existing)
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    // POC catalog (kept for backward compat with existing catalog API)
    public DbSet<Product> Products => Set<Product>();

    // Structure
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<LocationZone> LocationZones => Set<LocationZone>();
    public DbSet<PlatformCategory> PlatformCategories => Set<PlatformCategory>();
    public DbSet<ProductSegment> ProductSegments => Set<ProductSegment>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();

    // Products (v1 tenant-aware)
    public DbSet<Item> Items => Set<Item>();
    public DbSet<ProductSupplierSetting> ProductSupplierSettings => Set<ProductSupplierSetting>();

    // Stock
    public DbSet<ProductStock> ProductStocks => Set<ProductStock>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<StockEvent> StockEvents => Set<StockEvent>();
    // Daily Safe/Warning/Critical/Expired count snapshots, one row per (tenant, store, day) (TASK-335)
    public DbSet<StockStatusSnapshot> StockStatusSnapshots => Set<StockStatusSnapshot>();

    // Documents
    public DbSet<StockReceipt> StockReceipts => Set<StockReceipt>();
    public DbSet<StockReceiptItem> StockReceiptItems => Set<StockReceiptItem>();
    public DbSet<StockTransfer> StockTransfers => Set<StockTransfer>();
    public DbSet<StockTransferItem> StockTransferItems => Set<StockTransferItem>();
    public DbSet<WriteOff> WriteOffs => Set<WriteOff>();
    public DbSet<WriteOffItem> WriteOffItems => Set<WriteOffItem>();
    public DbSet<Discount> Discounts => Set<Discount>();

    // Notifications
    public DbSet<NotificationSetting> NotificationSettings => Set<NotificationSetting>();
    public DbSet<NotificationQueue> NotificationQueues => Set<NotificationQueue>();
    public DbSet<CustomerMessageCampaign> CustomerMessageCampaigns => Set<CustomerMessageCampaign>();
    public DbSet<CustomerMessageRecipient> CustomerMessageRecipients => Set<CustomerMessageRecipient>();

    // Integrations
    public DbSet<IntegrationConfig> IntegrationConfigs => Set<IntegrationConfig>();

    // Logs
    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();

    // v2 — Auto Order data foundation
    public DbSet<DailySale> DailySales => Set<DailySale>();
    public DbSet<ProductAdu> ProductAdus => Set<ProductAdu>();
    public DbSet<SupplySchedule> SupplySchedules => Set<SupplySchedule>();
    public DbSet<ProductBuffer> ProductBuffers => Set<ProductBuffer>();
    public DbSet<DemandEvent> DemandEvents => Set<DemandEvent>();
    public DbSet<DemandEventCoefficient> DemandEventCoefficients => Set<DemandEventCoefficient>();
    public DbSet<DemandEventStore> DemandEventStores => Set<DemandEventStore>();
    public DbSet<WeatherData> WeatherData => Set<WeatherData>();
    public DbSet<WeatherCoefficient> WeatherCoefficients => Set<WeatherCoefficient>();
    public DbSet<PromoCannibalization> PromoCannibalizations => Set<PromoCannibalization>();
    public DbSet<AiOrderSuggestion> AiOrderSuggestions => Set<AiOrderSuggestion>();
    public DbSet<AiOrderSuggestionItem> AiOrderSuggestionItems => Set<AiOrderSuggestionItem>();
    public DbSet<TelegramLinkCode> TelegramLinkCodes => Set<TelegramLinkCode>();

    // v3 — IoT foundation
    public DbSet<IotDevice> IotDevices => Set<IotDevice>();
    public DbSet<TemperatureReading> TemperatureReadings => Set<TemperatureReading>();
    public DbSet<WeightReading> WeightReadings => Set<WeightReading>();

    // v3 — POS (ПРРО Каса)
    public DbSet<PosShift> PosShifts => Set<PosShift>();
    public DbSet<PosTransaction> PosTransactions => Set<PosTransaction>();
    public DbSet<PosTransactionItem> PosTransactionItems => Set<PosTransactionItem>();

    // Support
    public DbSet<SupportTicket>  SupportTickets  => Set<SupportTicket>();
    public DbSet<SupportMessage> SupportMessages => Set<SupportMessage>();
    public DbSet<TicketComment>  TicketComments  => Set<TicketComment>();

    // Live Chat (TASK-278)
    public DbSet<ChatSession> ChatSessions => Set<ChatSession>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();

    // v4 Phase 3 — Supplier Marketplace
    public DbSet<SupplierProfile> SupplierProfiles => Set<SupplierProfile>();
    public DbSet<SupplierItem>    SupplierItems    => Set<SupplierItem>();
    public DbSet<SupplierMetrics> SupplierMetrics  => Set<SupplierMetrics>();
    public DbSet<SupplierMetricsSnapshot> SupplierMetricsSnapshots => Set<SupplierMetricsSnapshot>();
    public DbSet<SupplierReview>  SupplierReviews  => Set<SupplierReview>();
    public DbSet<SupplierItemBarcode> SupplierItemBarcodes => Set<SupplierItemBarcode>();
    public DbSet<SupplierItemImage>   SupplierItemImages   => Set<SupplierItemImage>();

    // Supplier-portal expansion Phase 2 — supplier warehouse inventory (D2, D3)
    public DbSet<SupplierStock>              SupplierStocks             => Set<SupplierStock>();
    public DbSet<SupplierStockMovement>      SupplierStockMovements     => Set<SupplierStockMovement>();
    public DbSet<SupplierStockReceipt>       SupplierStockReceipts      => Set<SupplierStockReceipt>();
    public DbSet<SupplierStockReceiptItem>   SupplierStockReceiptItems  => Set<SupplierStockReceiptItem>();

    // v4 Phase 4 — Auto Service Module
    public DbSet<AsCustomer>      AsCustomers      => Set<AsCustomer>();
    public DbSet<AsVehicle>       AsVehicles       => Set<AsVehicle>();
    public DbSet<AsServiceCatalog> AsServiceCatalogs => Set<AsServiceCatalog>();
    public DbSet<AsWorkOrder>     AsWorkOrders     => Set<AsWorkOrder>();
    public DbSet<AsWorkOrderLine> AsWorkOrderLines => Set<AsWorkOrderLine>();

    // CRM Customers
    public DbSet<Customer> Customers => Set<Customer>();

    // Workforce Schedules
    public DbSet<WorkSchedule> WorkSchedules => Set<WorkSchedule>();
    public DbSet<ScheduleShift> ScheduleShifts => Set<ScheduleShift>();

    // Provider team schedules (TASK-274)
    public DbSet<ProviderScheduleSlot> ProviderScheduleSlots => Set<ProviderScheduleSlot>();

    // Provider RBAC — custom roles
    public DbSet<ProviderRole> ProviderRoles => Set<ProviderRole>();

    // Landing page leads — provider-level, no tenant_id (TASK-333)
    public DbSet<LandingLead> LandingLeads => Set<LandingLead>();

    // Supplier RBAC — custom roles (tenant-scoped) + task board (TASK-305)
    public DbSet<SupplierRole> SupplierRoles => Set<SupplierRole>();
    public DbSet<SupplierTask> SupplierTasks => Set<SupplierTask>();

    // Supplier ↔ Client chat (TASK-312)
    public DbSet<SupplierChatSession> SupplierChatSessions => Set<SupplierChatSession>();
    public DbSet<SupplierChatMessage> SupplierChatMessages => Set<SupplierChatMessage>();

    // Supplier cooperation: agreements, marketplace orders, support tickets (TASK-316)
    public DbSet<SupplierContractSettings> SupplierContractSettings => Set<SupplierContractSettings>();
    public DbSet<SupplierAgreement> SupplierAgreements => Set<SupplierAgreement>();
    public DbSet<MarketplaceOrder> MarketplaceOrders => Set<MarketplaceOrder>();
    public DbSet<MarketplaceOrderItem> MarketplaceOrderItems => Set<MarketplaceOrderItem>();

    // Supplier-shipped batch allocations — supplier-write / client-read split RLS (Phase 3, D4)
    public DbSet<MarketplaceOrderItemBatch> MarketplaceOrderItemBatches => Set<MarketplaceOrderItemBatch>();

    // Marketplace order receiving — client-confirmed receipt (TASK-586, ADR-033)
    public DbSet<MarketplaceOrderReceipt> MarketplaceOrderReceipts => Set<MarketplaceOrderReceipt>();
    public DbSet<MarketplaceOrderReceiptItem> MarketplaceOrderReceiptItems => Set<MarketplaceOrderReceiptItem>();
    public DbSet<SupplierSupportTicket> SupplierSupportTickets => Set<SupplierSupportTicket>();
    public DbSet<SupplierSupportTicketMessage> SupplierSupportTicketMessages => Set<SupplierSupportTicketMessage>();

    // Legal entities — multi-company support per tenant (TASK-321)
    public DbSet<LegalEntity> LegalEntities => Set<LegalEntity>();

    // v4 Phase 5 — Production Module
    public DbSet<Recipe>                     Recipes                     => Set<Recipe>();
    public DbSet<RecipeIngredient>           RecipeIngredients           => Set<RecipeIngredient>();
    public DbSet<ProductionOrder>            ProductionOrders            => Set<ProductionOrder>();
    public DbSet<ProductionOrderConsumption> ProductionOrderConsumptions => Set<ProductionOrderConsumption>();

    // Temporary per-user permission grants (ADR-019, TASK-341)
    public DbSet<UserPermissionGrant> UserPermissionGrants => Set<UserPermissionGrant>();

    // Tenant custom role templates (ADR-020, TASK-345)
    public DbSet<TenantRole> TenantRoles => Set<TenantRole>();

    // Store-scoped user access grants — Stage 1 schema only, enforcement is Stage 3 (TASK-392)
    public DbSet<UserLocation> UserLocations => Set<UserLocation>();

    // Loyalty program — Фаза 0 (TASK-404). ConsumerAccount is global/no-RLS by design;
    // the other three are tenant-scoped (see AddLoyaltyProgram migration for RLS detail).
    public DbSet<ConsumerAccount> ConsumerAccounts => Set<ConsumerAccount>();
    public DbSet<LoyaltyMembership> LoyaltyMemberships => Set<LoyaltyMembership>();
    public DbSet<LoyaltyLedgerEntry> LoyaltyLedgerEntries => Set<LoyaltyLedgerEntry>();
    public DbSet<LoyaltyBonusLot> LoyaltyBonusLots => Set<LoyaltyBonusLot>();
    public DbSet<PromotionCampaign> PromotionCampaigns => Set<PromotionCampaign>();
    public DbSet<PromotionCampaignLocation> PromotionCampaignLocations => Set<PromotionCampaignLocation>();
    public DbSet<PromotionCampaignProduct> PromotionCampaignProducts => Set<PromotionCampaignProduct>();
    public DbSet<MobileCatalogSettings> MobileCatalogSettings => Set<MobileCatalogSettings>();
    public DbSet<MobileCatalogItem> MobileCatalogItems => Set<MobileCatalogItem>();
    public DbSet<MobileCatalogLocation> MobileCatalogLocations => Set<MobileCatalogLocation>();
    public DbSet<MobileCatalogEvent> MobileCatalogEvents => Set<MobileCatalogEvent>();
    public DbSet<LoyaltyProgramSettings> LoyaltyProgramSettings => Set<LoyaltyProgramSettings>();
    public DbSet<PriceSegmentSettings> PriceSegmentSettings => Set<PriceSegmentSettings>();

    // Post-campaign audience analysis — Фаза 4 (TASK-471). Unlike Фаза 1-3, this persists the
    // uploaded id list + frozen before/after windows (see PostCampaignSegment class remarks).
    public DbSet<PostCampaignSegment> PostCampaignSegments => Set<PostCampaignSegment>();
    public DbSet<PostCampaignSegmentMember> PostCampaignSegmentMembers => Set<PostCampaignSegmentMember>();

    // Consumer App — banners, promoted products, view/click log (TASK-520). Schema only;
    // service/controller land in TASK-521.
    public DbSet<Banner> Banners => Set<Banner>();
    public DbSet<BannerLocation> BannerLocations => Set<BannerLocation>();
    public DbSet<BannerProduct> BannerProducts => Set<BannerProduct>();
    public DbSet<BannerEvent> BannerEvents => Set<BannerEvent>();
    public DbSet<PromotionCampaignEvent> PromotionCampaignEvents => Set<PromotionCampaignEvent>();

    // Mobile Configuration domain — multi-tenant consumer app-builder (CLAUDE CODE SPEC ЕТАП 3,
    // TASK-531). Schema only; validation/CRUD/publish/API land in TASK-532/534.
    public DbSet<MobileConfiguration> MobileConfigurations => Set<MobileConfiguration>();
    public DbSet<MobileConfigurationVersion> MobileConfigurationVersions => Set<MobileConfigurationVersion>();
    public DbSet<MobileTheme> MobileThemes => Set<MobileTheme>();

    // Customer/loyalty domain expansion (TASK-613): profile-change history (no RLS,
    // mirrors ConsumerAccount), loyalty tier ladder + append-only tier-change history,
    // consumer support tickets, purchase reviews.
    public DbSet<ConsumerAccountProfileChange> ConsumerAccountProfileChanges => Set<ConsumerAccountProfileChange>();
    public DbSet<LoyaltyTierDefinition> LoyaltyTierDefinitions => Set<LoyaltyTierDefinition>();
    public DbSet<LoyaltyTierChangeHistory> LoyaltyTierChangeHistories => Set<LoyaltyTierChangeHistory>();
    public DbSet<ConsumerSupportTicket> ConsumerSupportTickets => Set<ConsumerSupportTicket>();
    public DbSet<ConsumerSupportTicketMessage> ConsumerSupportTicketMessages => Set<ConsumerSupportTicketMessage>();
    public DbSet<PurchaseReview> PurchaseReviews => Set<PurchaseReview>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // ── Tenant ─────────────────────────────────────────────────────────
        builder.Entity<Tenant>(e =>
        {
            e.ToTable("tenants");
            e.HasKey(t => t.Id);
            e.Property(t => t.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(t => t.Name).HasMaxLength(255).IsRequired();
            e.Property(t => t.Slug).HasMaxLength(100).IsRequired();
            e.HasIndex(t => t.Slug).IsUnique();
            e.Property(t => t.Plan).HasMaxLength(50).HasDefaultValue("basic");
            e.Property(t => t.Modules).HasColumnType("jsonb").HasDefaultValue("[]");
            e.Property(t => t.BusinessType).HasMaxLength(50).HasDefaultValue("retail");
            e.Property(t => t.IsActive).HasDefaultValue(true);
            e.Property(t => t.LogoUrl).HasColumnType("text").IsRequired(false);
            e.Property(t => t.CreatedAt).HasDefaultValueSql("NOW()");
            e.Property(t => t.UpdatedAt).HasDefaultValueSql("NOW()");
        });

        // ── User ────────────────────────────────────────────────────────────
        builder.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasKey(u => u.Id);
            e.Property(u => u.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(u => u.Email).HasMaxLength(255).IsRequired();
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.Phone).HasMaxLength(20);
            e.Property(u => u.FullName).HasMaxLength(255).IsRequired();
            e.Property(u => u.PasswordHash).HasMaxLength(255).IsRequired();
            e.Property(u => u.Role).HasMaxLength(50).IsRequired();
            e.Property(u => u.TelegramChatId).HasMaxLength(100);
            // i18n Block 1 (TASK-375): "uk"/"en"; null = browser fallback. Length 5 leaves room for "uk-UA"-style tags.
            e.Property(u => u.PreferredLocale).HasMaxLength(5).IsRequired(false);
            e.Property(u => u.IsActive).HasDefaultValue(true);
            e.Property(u => u.CreatedAt).HasDefaultValueSql("NOW()");
            e.Property(u => u.InvitedByName).HasMaxLength(255).IsRequired(false);
            // Supplier-portal expansion (plan #3): per-user "last opened supplier orders" marker.
            e.Property(u => u.SupplierOrdersLastViewedAt).IsRequired(false);
            e.Property(u => u.ProviderRoleId).IsRequired(false);
            e.HasOne<ProviderRole>().WithMany()
             .HasForeignKey(u => u.ProviderRoleId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
            e.Property(u => u.SupplierRoleId).IsRequired(false);
            e.HasOne<SupplierRole>().WithMany()
             .HasForeignKey(u => u.SupplierRoleId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
            e.Property(u => u.TenantRoleId).IsRequired(false);
            e.HasOne<TenantRole>().WithMany()
             .HasForeignKey(u => u.TenantRoleId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
            e.Property(u => u.Permissions)
             .HasColumnType("jsonb")
             .HasConversion(
                 v => v == null ? null : System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                 v => v == null ? null : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, bool>>(v, (System.Text.Json.JsonSerializerOptions?)null))
             .IsRequired(false)
             .Metadata.SetValueComparer(new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<Dictionary<string, bool>?>(
                 (a, b) => System.Text.Json.JsonSerializer.Serialize(a, (System.Text.Json.JsonSerializerOptions?)null) ==
                            System.Text.Json.JsonSerializer.Serialize(b, (System.Text.Json.JsonSerializerOptions?)null),
                 v => v == null ? 0 : System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null).GetHashCode(),
                 v => v == null ? null : new Dictionary<string, bool>(v)));
            e.HasOne(u => u.Tenant).WithMany(t => t.Users)
             .HasForeignKey(u => u.TenantId).OnDelete(DeleteBehavior.Restrict).IsRequired(false);
            e.Property(u => u.LegalEntityId).IsRequired(false);
            e.HasOne<LegalEntity>().WithMany()
             .HasForeignKey(u => u.LegalEntityId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);

            // Home location — UI/invite-time default only (TASK-392 Stage 1). The physical
            // column has been "StoreId" since AddAuth (2026-06-03) but was never mapped in
            // EF — no HasColumnName/FK/index — unlike the ~19 other pre-v4 entities
            // (ProductStock, WriteOff, PosShift, ...) that got `.HasColumnName("LocationId")`
            // in V4LocationsRename while keeping their C# property named StoreId. CLR name
            // intentionally stays StoreId here too, for the same reason. Never read by
            // access-control enforcement — that's Stage 3's RLS policies driven by
            // UserLocation below; this is purely a default-location hint for invites/UI.
            e.Property(u => u.StoreId).HasColumnName("LocationId").IsRequired(false);
            e.HasOne<Location>().WithMany()
             .HasForeignKey(u => u.StoreId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);

            // Account lockout (TASK-329)
            e.Property(u => u.FailedLoginAttempts).HasDefaultValue(0);
            e.Property(u => u.LockoutUntil).IsRequired(false);

            // Temporary password (forgot-password flow, TASK-464/465)
            e.Property(u => u.TempPasswordExpiresAt).IsRequired(false);

            // 2FA TOTP (TASK-330)
            e.Property(u => u.TotpSecret).IsRequired(false);
            e.Property(u => u.TotpEnabled).HasDefaultValue(false);
            e.Property(u => u.TotpLastTimestep).IsRequired(false);
            e.Property(u => u.TotpRecoveryCodes)
             .HasColumnType("jsonb")
             .HasConversion(
                 v => v == null ? null : System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                 v => v == null ? null : System.Text.Json.JsonSerializer.Deserialize<List<string>>(v, (System.Text.Json.JsonSerializerOptions?)null))
             .IsRequired(false)
             .Metadata.SetValueComparer(new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<string>?>(
                 (a, b) => System.Text.Json.JsonSerializer.Serialize(a, (System.Text.Json.JsonSerializerOptions?)null) ==
                            System.Text.Json.JsonSerializer.Serialize(b, (System.Text.Json.JsonSerializerOptions?)null),
                 v => v == null ? 0 : System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null).GetHashCode(),
                 v => v == null ? null : new List<string>(v)));
        });

        // ── RefreshToken ────────────────────────────────────────────────────
        builder.Entity<RefreshToken>(e =>
        {
            e.ToTable("refresh_tokens");
            e.HasKey(rt => rt.Id);
            e.Property(rt => rt.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(rt => rt.TokenHash).HasMaxLength(64).IsRequired();
            e.HasIndex(rt => rt.TokenHash).IsUnique();
            e.Property(rt => rt.ExpiresAt).IsRequired();
            e.Property(rt => rt.ReplacedByTokenHash).HasMaxLength(64);
            e.Property(rt => rt.CreatedAt).HasDefaultValueSql("NOW()");
            e.HasOne(rt => rt.User).WithMany(u => u.RefreshTokens)
             .HasForeignKey(rt => rt.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── Product (POC) ───────────────────────────────────────────────────
        builder.Entity<Product>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Sku).HasMaxLength(100).IsRequired();
            e.HasIndex(p => p.Sku).IsUnique();
            e.Property(p => p.Name).HasMaxLength(200).IsRequired();
            e.Property(p => p.Description).HasMaxLength(1000);
            e.Property(p => p.Category).HasMaxLength(100).IsRequired();
            e.Property(p => p.Unit).HasMaxLength(50).IsRequired();
            e.Property(p => p.CostPrice).HasColumnType("numeric(18,4)");
            e.Property(p => p.SalePrice).HasColumnType("numeric(18,4)");
            e.Property(p => p.StockQuantity).HasColumnType("numeric(18,4)");
            e.Property(p => p.ReorderLevel).HasColumnType("numeric(18,4)");
        });

        // ── Location (v4: mapped to "locations" table) ──────────────────────
        builder.Entity<Location>(e =>
        {
            e.ToTable("locations");
            e.HasKey(s => s.Id);
            e.Property(s => s.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(s => s.Name).HasMaxLength(255).IsRequired();
            e.Property(s => s.Type).HasMaxLength(50).IsRequired();
            e.Property(s => s.LocationType).HasMaxLength(50).HasDefaultValue("retail_store");
            // TASK-649: structured Ukraine region code; varchar(20) fits the longest
            // city code shape "UA-XX-LONGTRANSLIT" (e.g. UA-12-KRYVYI-RIH).
            e.Property(s => s.RegionCode).HasMaxLength(20);
            e.Property(s => s.FloorPlan).HasColumnType("jsonb");
            e.Property(s => s.Latitude).HasColumnType("decimal(10,7)");
            e.Property(s => s.Longitude).HasColumnType("decimal(10,7)");
            e.Property(s => s.IsActive).HasDefaultValue(true);
            e.Property(s => s.CreatedAt).HasDefaultValueSql("NOW()");
            e.HasOne(s => s.Tenant).WithMany()
             .HasForeignKey(s => s.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.Property(s => s.LegalEntityId).IsRequired(false);
            e.HasOne<LegalEntity>().WithMany()
             .HasForeignKey(s => s.LegalEntityId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
        });

        // ── LocationZone (v4: mapped to "location_zones" table) ─────────────
        builder.Entity<LocationZone>(e =>
        {
            e.ToTable("location_zones");
            e.HasKey(z => z.Id);
            e.Property(z => z.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(z => z.Name).HasMaxLength(255).IsRequired();
            e.Property(z => z.Type).HasMaxLength(50).IsRequired();
            e.Property(z => z.Position).HasColumnType("jsonb");
            e.Property(z => z.TempMin).HasColumnType("decimal(5,1)");
            e.Property(z => z.TempMax).HasColumnType("decimal(5,1)");
            e.Property(z => z.ShelvesCount).HasDefaultValue(1);
            e.Property(z => z.IsActive).HasDefaultValue(true);
            e.Property(z => z.LocationId).HasColumnName("LocationId");
            e.HasOne(z => z.Location).WithMany(s => s.Zones)
             .HasForeignKey(z => z.LocationId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── PlatformCategory ────────────────────────────────────────────────
        // Global, provider-curated category catalogue (B1). No TenantId, no RLS:
        // reference data readable by every authenticated tenant, written only via the
        // provider-only endpoints. Tenants filter it by Tenant.BusinessType (B2).
        builder.Entity<PlatformCategory>(e =>
        {
            e.ToTable("platform_categories");
            e.HasKey(c => c.Id);
            e.Property(c => c.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(c => c.Name).HasMaxLength(255).IsRequired();
            // jsonb string array — same EnableDynamicJson path as Item.Barcodes, no value converter.
            e.Property(c => c.BusinessTypes)
             .HasColumnType("jsonb")
             .HasDefaultValueSql("'[]'::jsonb");
            e.Property(c => c.SortOrder).HasDefaultValue(0);
            e.Property(c => c.IsActive).HasDefaultValue(true);
            e.Property(c => c.CreatedAt).HasDefaultValueSql("NOW()");
            // Tree walk: children of a parent, active only.
            e.HasIndex(c => new { c.ParentId, c.IsActive })
             .HasDatabaseName("idx_platform_categories_parent_active");
            // Flat active list ordered for display (the GET /api/categories shape).
            e.HasIndex(c => new { c.IsActive, c.SortOrder })
             .HasDatabaseName("idx_platform_categories_active_sort");
            e.HasOne(c => c.Parent).WithMany()
             .HasForeignKey(c => c.ParentId).OnDelete(DeleteBehavior.Restrict).IsRequired(false);
        });

        // ── ProductSegment ──────────────────────────────────────────────────
        builder.Entity<ProductSegment>(e =>
        {
            e.ToTable("product_segments");
            e.HasKey(s => s.Id);
            e.Property(s => s.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(s => s.Name).HasMaxLength(255).IsRequired();
            e.Property(s => s.IsActive).HasDefaultValue(true);
            e.HasOne(s => s.Tenant).WithMany()
             .HasForeignKey(s => s.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(s => s.Category).WithMany()
             .HasForeignKey(s => s.CategoryId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
        });

        // ── Supplier ────────────────────────────────────────────────────────
        builder.Entity<Supplier>(e =>
        {
            e.ToTable("suppliers");
            e.HasKey(s => s.Id);
            e.Property(s => s.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(s => s.Name).HasMaxLength(255).IsRequired();
            e.Property(s => s.Edrpou).HasMaxLength(20);
            e.Property(s => s.ContactPerson).HasMaxLength(255);
            e.Property(s => s.Phone).HasMaxLength(20);
            e.Property(s => s.Email).HasMaxLength(255);
            e.Property(s => s.DeliveryDays).HasDefaultValue(3);
            e.Property(s => s.IsActive).HasDefaultValue(true);
            e.HasOne(s => s.Tenant).WithMany()
             .HasForeignKey(s => s.TenantId).OnDelete(DeleteBehavior.Restrict);
        });

        // ── Item ──────────────────────────────────────────────────
        builder.Entity<Item>(e =>
        {
            e.ToTable("items");
            e.HasKey(p => p.Id);
            e.Property(p => p.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(p => p.Barcodes)
             .HasColumnType("jsonb")
             .HasDefaultValueSql("'[]'::jsonb");
            e.Property(p => p.Manufacturer).HasMaxLength(255);
            e.Property(p => p.CountryOrigin).HasMaxLength(100);
            e.Property(p => p.Name).HasMaxLength(255).IsRequired();
            e.Property(p => p.Unit).HasMaxLength(20).HasDefaultValue("шт");
            e.Property(p => p.ManagementType).HasMaxLength(10).HasDefaultValue("MTS");
            e.Property(p => p.ItemType).HasMaxLength(50).HasDefaultValue("product");
            // Matches hand-written migration AddItemPerishabilityClass (varchar(20) DEFAULT 'standard')
            e.Property(p => p.PerishabilityClass).HasMaxLength(20).HasDefaultValue("standard");
            e.Property(p => p.MinStock).HasColumnType("decimal(10,2)");
            e.Property(p => p.MaxStock).HasColumnType("decimal(10,2)");
            e.Property(p => p.SafetyBuffer).HasColumnType("decimal(10,2)");
            e.Property(p => p.StorageTempMin).HasColumnType("decimal(5,1)");
            e.Property(p => p.StorageTempMax).HasColumnType("decimal(5,1)");
            e.Property(p => p.VatRate).HasColumnType("decimal(5,2)").HasDefaultValue(20m);
            e.Property(p => p.PricePurchase).HasColumnType("decimal(12,2)");
            e.Property(p => p.PriceRetail).HasColumnType("decimal(12,2)");
            e.Property(p => p.DefaultReimbursementType).HasMaxLength(10);
            e.Property(p => p.DefaultReimbursementValue).HasColumnType("decimal(12,2)");
            e.Property(p => p.IsActive).HasDefaultValue(true);
            e.Property(p => p.CreatedAt).HasDefaultValueSql("NOW()");
            // Catalog browse: filter by category + segment + active status
            e.HasIndex(p => new { p.TenantId, p.CategoryId, p.SegmentId, p.IsActive })
             .HasDatabaseName("idx_items_tenant_category_segment_active");
            // Barcodes GIN index for array containment queries
            e.HasIndex(p => p.Barcodes)
             .HasDatabaseName("idx_items_barcodes_gin")
             .HasAnnotation("Npgsql:IndexMethod", "gin");
            // Trigram GIN index for substring search (Фаза 3 AudienceBuilder — ILIKE '%term%' on
            // Name). Same pattern as idx_notification_queue_title_trgm; pg_trgm already enabled by
            // 20260712122713_ExtendNotificationQueueFiltering.
            e.HasIndex(p => p.Name)
             .HasDatabaseName("idx_items_name_trgm")
             .HasMethod("gin")
             .HasOperators("gin_trgm_ops");
            e.HasOne(p => p.Tenant).WithMany()
             .HasForeignKey(p => p.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(p => p.Category).WithMany()
             .HasForeignKey(p => p.CategoryId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
            e.HasOne(p => p.Segment).WithMany()
             .HasForeignKey(p => p.SegmentId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
            e.HasOne(p => p.DefaultSupplier).WithMany()
             .HasForeignKey(p => p.DefaultSupplierId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
            // Lineage pointer (TASK-596): which SupplierItem this Item was auto-provisioned
            // from at order time. SET NULL, not Cascade/Restrict — an Item must survive even
            // if the source supplier listing is later removed. No explicit standalone
            // HasIndex() here (unlike SupplierItem.ItemId's own convention above) — Item and
            // SupplierItem already have a reference nav pointing at each other in the OTHER
            // direction (SupplierItem.Item -> Item), and pairing an explicit index with this
            // FK made EF's relationship discovery treat the two as one 1:1 pair, incorrectly
            // marking this FK's index unique (confirmed by generating the migration and
            // reverting once). The plain FK-by-convention index below is sufficient and
            // correctly non-unique.
            e.HasOne(p => p.SourceSupplierItem).WithMany()
             .HasForeignKey(p => p.SourceSupplierItemId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
        });

        // ── ProductSupplierSetting ──────────────────────────────────────────
        builder.Entity<ProductSupplierSetting>(e =>
        {
            e.ToTable("product_supplier_settings");
            e.HasKey(s => s.Id);
            e.Property(s => s.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(s => s.Moq).HasColumnType("decimal(10,2)").HasDefaultValue(1m);
            e.Property(s => s.Usq).HasColumnType("decimal(10,2)").HasDefaultValue(1m);
            e.Property(s => s.PricePurchase).HasColumnType("decimal(12,2)");
            e.Property(s => s.DeliveryDays).HasDefaultValue(3);
            e.Property(s => s.IsActive).HasDefaultValue(true);
            e.HasIndex(s => new { s.ProductId, s.SupplierId, s.TenantId }).IsUnique();
            e.HasOne(s => s.Product).WithMany()
             .HasForeignKey(s => s.ProductId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(s => s.Supplier).WithMany()
             .HasForeignKey(s => s.SupplierId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── ProductStock ────────────────────────────────────────────────────
        builder.Entity<ProductStock>(e =>
        {
            e.ToTable("product_stock");
            e.HasKey(s => s.Id);
            e.Property(s => s.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(s => s.BatchNumber).HasMaxLength(100);
            e.Property(s => s.Quantity).HasColumnType("decimal(10,2)").IsRequired();
            e.Property(s => s.QuantityInitial).HasColumnType("decimal(10,2)").IsRequired();
            e.Property(s => s.ExpiryDate).IsRequired();
            e.Property(s => s.Status).HasMaxLength(30).HasDefaultValue("safe");
            e.Property(s => s.SourceType).HasMaxLength(50);
            e.Property(s => s.AddedAt).HasDefaultValueSql("NOW()");
            e.Property(s => s.LastCheckedAt).HasDefaultValueSql("NOW()");
            e.Property(s => s.StoreId).HasColumnName("LocationId");
            // TASK-356: optimistic concurrency on the Postgres system column (no schema
            // change needed — xmin already exists on every row). Without this, two
            // concurrent writers decrementing the same batch's Quantity (e.g. two cashiers
            // selling the last unit at the same moment via POS, or a POS sale racing a
            // transfer/write-off) both succeed with a last-write-wins UPDATE — a silent
            // lost update that can oversell stock. Now the loser's SaveChangesAsync throws
            // DbUpdateConcurrencyException instead of corrupting Quantity; callers (POS:
            // PosService.CreateSaleAsync) turn that into a clean "retry" error.
            e.Property<uint>("xmin").IsRowVersion();
            // FEFO active stock — most critical query path
            e.HasIndex(s => new { s.TenantId, s.StoreId, s.ProductId, s.ExpiryDate })
             .HasDatabaseName("idx_stock_fefo_active")
             .HasFilter("\"Quantity\" > 0");
            // Store dashboard — covering index includes Status + Quantity
            e.HasIndex(s => new { s.TenantId, s.StoreId })
             .HasDatabaseName("idx_stock_tenant_store_covering")
             .IncludeProperties(s => new { s.Status, s.Quantity });
            // Status filter for analytics — AddedAt is the creation timestamp
            e.HasIndex(s => new { s.TenantId, s.Status, s.AddedAt })
             .IsDescending(false, false, true)
             .HasDatabaseName("idx_stock_tenant_status_addedat");
            // Zone analytics — active stock only
            e.HasIndex(s => new { s.TenantId, s.ZoneId })
             .HasDatabaseName("idx_stock_tenant_zone_active")
             .HasFilter("\"Quantity\" > 0");
            e.HasOne(s => s.Product).WithMany()
             .HasForeignKey(s => s.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(s => s.Store).WithMany()
             .HasForeignKey(s => s.StoreId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(s => s.Zone).WithMany()
             .HasForeignKey(s => s.ZoneId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
        });

        // ── StockStatusSnapshot (TASK-335) ─────────────────────────────────────
        // Daily per-store snapshot of product_stock Status counts, written by a
        // worker cron job so the dashboard can diff "today vs a week ago". The
        // network-wide (all stores) view is a SUM over rows for (TenantId, SnapshotDate)
        // computed at query time — no separate rollup row is stored.
        builder.Entity<StockStatusSnapshot>(e =>
        {
            e.ToTable("stock_status_snapshots");
            e.HasKey(s => s.Id);
            e.Property(s => s.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(s => s.SnapshotDate).HasColumnType("date").IsRequired();
            e.Property(s => s.SafeCount).IsRequired();
            e.Property(s => s.WarningCount).IsRequired();
            e.Property(s => s.CriticalCount).IsRequired();
            e.Property(s => s.ExpiredCount).IsRequired();
            e.Property(s => s.CreatedAt).HasDefaultValueSql("NOW()");
            e.Property(s => s.StoreId).HasColumnName("LocationId");
            // Idempotent upsert key — worker does ON CONFLICT (TenantId, LocationId, SnapshotDate)
            e.HasIndex(s => new { s.TenantId, s.StoreId, s.SnapshotDate })
             .IsUnique()
             .HasDatabaseName("idx_stock_status_snapshots_tenant_store_date");
            // Network-wide (all-store) rollup query
            e.HasIndex(s => new { s.TenantId, s.SnapshotDate })
             .HasDatabaseName("idx_stock_status_snapshots_tenant_date");
            e.HasOne(s => s.Store).WithMany()
             .HasForeignKey(s => s.StoreId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── StockMovement ───────────────────────────────────────────────────
        builder.Entity<StockMovement>(e =>
        {
            e.ToTable("stock_movements");
            e.HasKey(m => m.Id);
            e.Property(m => m.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(m => m.FromStoreId).HasColumnName("FromLocationId");
            e.Property(m => m.ToStoreId).HasColumnName("ToLocationId");
            e.Property(m => m.MovementType).HasMaxLength(50).IsRequired();
            e.Property(m => m.Quantity).HasColumnType("decimal(10,2)").IsRequired();
            e.Property(m => m.QuantityBefore).HasColumnType("decimal(10,2)");
            e.Property(m => m.QuantityAfter).HasColumnType("decimal(10,2)");
            e.Property(m => m.UnitPrice).HasColumnType("decimal(12,2)");
            e.Property(m => m.TotalAmount).HasColumnType("decimal(12,2)");
            e.Property(m => m.ReferenceType).HasMaxLength(50);
            e.Property(m => m.CreatedAt).HasDefaultValueSql("NOW()");
        });

        // ── StockEvent ──────────────────────────────────────────────────────
        builder.Entity<StockEvent>(e =>
        {
            e.ToTable("stock_events");
            e.HasKey(ev => ev.Id);
            e.Property(ev => ev.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(ev => ev.EventType).HasMaxLength(50).IsRequired();
            e.Property(ev => ev.SourceDeviceId).HasMaxLength(100);
            e.Property(ev => ev.QuantityDelta).HasColumnType("decimal(10,2)");
            e.Property(ev => ev.Confidence).HasDefaultValue(100);
            e.Property(ev => ev.Meta).HasColumnType("jsonb");
            e.Property(ev => ev.CreatedAt).HasDefaultValueSql("NOW()");
        });

        // ── StockReceipt ────────────────────────────────────────────────────
        builder.Entity<StockReceipt>(e =>
        {
            e.ToTable("stock_receipts");
            e.HasKey(r => r.Id);
            e.Property(r => r.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(r => r.Status).HasMaxLength(30).HasDefaultValue("draft");
            e.Property(r => r.CreatedAt).HasDefaultValueSql("NOW()");
            e.Property(r => r.DestinationStoreId).HasColumnName("DestinationLocationId");
            // TASK-354: TenantId had no index at all on this table (RLS filters every
            // query by TenantId — same gap pattern as WriteOff had before its
            // idx_write_offs_tenant_store_status index further below).
            e.HasIndex(r => new { r.TenantId, r.DestinationStoreId, r.Status, r.CreatedAt })
             .IsDescending(false, false, false, true)
             .HasDatabaseName("idx_stock_receipts_tenant_store_status");
        });

        // ── StockReceiptItem ────────────────────────────────────────────────
        builder.Entity<StockReceiptItem>(e =>
        {
            e.ToTable("stock_receipt_items");
            e.HasKey(i => i.Id);
            e.Property(i => i.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(i => i.QuantityOrdered).HasColumnType("decimal(10,2)").IsRequired();
            e.Property(i => i.QuantityReceived).HasColumnType("decimal(10,2)");
            e.Property(i => i.PricePurchase).HasColumnType("decimal(12,2)");
            e.Property(i => i.BatchNumber).HasMaxLength(100);
            e.HasOne(i => i.Receipt).WithMany(r => r.Items)
             .HasForeignKey(i => i.ReceiptId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── StockTransfer ───────────────────────────────────────────────────
        builder.Entity<StockTransfer>(e =>
        {
            e.ToTable("stock_transfers");
            e.HasKey(t => t.Id);
            e.Property(t => t.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(t => t.TransferType).HasMaxLength(50);
            e.Property(t => t.Status).HasMaxLength(30).HasDefaultValue("draft");
            e.Property(t => t.CreatedAt).HasDefaultValueSql("NOW()");
            e.Property(t => t.FromStoreId).HasColumnName("FromLocationId");
            e.Property(t => t.ToStoreId).HasColumnName("ToLocationId");
            // TASK-354: same TenantId index gap as StockReceipt above. Two composite
            // indexes (not one) because GetAllAsync/GetPagedAsync filter store_id via
            // `FromStoreId == storeId || ToStoreId == storeId` — one index per side of
            // the OR so Postgres can bitmap-OR them instead of falling back to a seq scan.
            e.HasIndex(t => new { t.TenantId, t.FromStoreId, t.Status, t.CreatedAt })
             .IsDescending(false, false, false, true)
             .HasDatabaseName("idx_stock_transfers_tenant_from_status");
            e.HasIndex(t => new { t.TenantId, t.ToStoreId, t.Status, t.CreatedAt })
             .IsDescending(false, false, false, true)
             .HasDatabaseName("idx_stock_transfers_tenant_to_status");
        });

        // ── StockTransferItem ───────────────────────────────────────────────
        builder.Entity<StockTransferItem>(e =>
        {
            e.ToTable("stock_transfer_items");
            e.HasKey(i => i.Id);
            e.Property(i => i.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(i => i.Quantity).HasColumnType("decimal(10,2)").IsRequired();
            e.Property(i => i.ExpiryDate).IsRequired();
            e.Property(i => i.BatchNumber).HasMaxLength(100);
            e.HasOne(i => i.Transfer).WithMany(t => t.Items)
             .HasForeignKey(i => i.TransferId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── WriteOff ────────────────────────────────────────────────────────
        builder.Entity<WriteOff>(e =>
        {
            e.ToTable("write_offs");
            e.HasKey(w => w.Id);
            e.Property(w => w.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(w => w.Status).HasMaxLength(30).HasDefaultValue("draft");
            e.Property(w => w.Reason).HasMaxLength(50);
            e.Property(w => w.TotalLossAmount).HasColumnType("decimal(12,2)");
            e.Property(w => w.TotalLossAmountPurchase).HasColumnType("decimal(12,2)");
            e.Property(w => w.TotalReimbursementAmount).HasColumnType("decimal(12,2)");
            e.Property(w => w.CreatedAt).HasDefaultValueSql("NOW()");
            e.Property(w => w.StoreId).HasColumnName("LocationId");
            e.HasIndex(w => new { w.TenantId, w.StoreId, w.Status, w.CreatedAt })
             .IsDescending(false, false, false, true)
             .HasDatabaseName("idx_write_offs_tenant_store_status");
        });

        // ── WriteOffItem ────────────────────────────────────────────────────
        builder.Entity<WriteOffItem>(e =>
        {
            e.ToTable("write_off_items");
            e.HasKey(i => i.Id);
            e.Property(i => i.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(i => i.Quantity).HasColumnType("decimal(10,2)").IsRequired();
            e.Property(i => i.UnitPrice).HasColumnType("decimal(12,2)");
            e.Property(i => i.LossAmount).HasColumnType("decimal(12,2)");
            e.Property(i => i.UnitPricePurchase).HasColumnType("decimal(12,2)");
            e.Property(i => i.LossAmountPurchase).HasColumnType("decimal(12,2)");
            e.Property(i => i.IsReturnedToSupplier).HasDefaultValue(false);
            e.Property(i => i.ReimbursementType).HasMaxLength(10);
            e.Property(i => i.ReimbursementValue).HasColumnType("decimal(12,2)");
            e.Property(i => i.ReimbursementAmount).HasColumnType("decimal(12,2)");
            e.HasOne(i => i.WriteOff).WithMany(w => w.Items)
             .HasForeignKey(i => i.WriteOffId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── Discount ────────────────────────────────────────────────────────
        builder.Entity<Discount>(e =>
        {
            e.ToTable("discounts");
            e.HasKey(d => d.Id);
            e.Property(d => d.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(d => d.DiscountPercent).HasColumnType("decimal(5,2)").IsRequired();
            e.Property(d => d.PriceOriginal).HasColumnType("decimal(12,2)");
            e.Property(d => d.PriceDiscounted).HasColumnType("decimal(12,2)");
            e.Property(d => d.Reason).HasMaxLength(50).HasDefaultValue("expiry");
            e.Property(d => d.Status).HasMaxLength(20).HasDefaultValue("pending");
            e.Property(d => d.ValidFrom).HasDefaultValueSql("NOW()");
            e.Property(d => d.StoreId).HasColumnName("LocationId");
            // Active discounts by validity window
            e.HasIndex(d => new { d.TenantId, d.Status, d.ValidFrom, d.ValidUntil })
             .HasDatabaseName("idx_discounts_active")
             .HasFilter("\"Status\" = 'active'");
            e.HasOne(d => d.Tenant).WithMany()
             .HasForeignKey(d => d.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(d => d.Creator).WithMany()
             .HasForeignKey(d => d.CreatedBy).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(d => d.Approver).WithMany()
             .HasForeignKey(d => d.ApprovedBy).OnDelete(DeleteBehavior.SetNull);
        });

        // ── NotificationSetting ─────────────────────────────────────────────
        builder.Entity<NotificationSetting>(e =>
        {
            e.ToTable("notification_settings");
            e.HasKey(n => n.Id);
            e.Property(n => n.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(n => n.EventType).HasMaxLength(100).IsRequired();
            e.Property(n => n.Channel).HasMaxLength(50).IsRequired();
            e.Property(n => n.IsEnabled).HasDefaultValue(true);
            e.HasIndex(n => new { n.UserId, n.EventType, n.Channel }).IsUnique();
            e.HasOne(n => n.User).WithMany()
             .HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── NotificationQueue ───────────────────────────────────────────────
        builder.Entity<NotificationQueue>(e =>
        {
            e.ToTable("notification_queue");
            e.HasKey(n => n.Id);
            e.Property(n => n.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(n => n.StoreId).HasColumnName("LocationId");
            e.Property(n => n.Title).HasMaxLength(255);
            e.Property(n => n.Channel).HasMaxLength(50).IsRequired();
            e.Property(n => n.EventType).HasMaxLength(100);
            e.Property(n => n.Payload).HasColumnType("jsonb");
            e.Property(n => n.Status).HasMaxLength(20).HasDefaultValue("pending");
            e.Property(n => n.CreatedAt).HasDefaultValueSql("NOW()");
            // Worker queue polling: find pending items by tenant, ordered by time
            e.HasIndex(n => new { n.TenantId, n.Status, n.CreatedAt })
             .HasDatabaseName("idx_notification_queue_tenant_status");
            // Notifications page filter drawer (TASK-338, ADR-018 §3)
            e.HasIndex(n => new { n.TenantId, n.CreatedAt })
             .HasDatabaseName("idx_notification_queue_tenant_createdat");
            e.HasIndex(n => new { n.TenantId, n.EventType })
             .HasDatabaseName("idx_notification_queue_tenant_eventtype");
            e.HasIndex(n => new { n.TenantId, n.StoreId })
             .HasDatabaseName("idx_notification_queue_tenant_store");
            e.HasIndex(n => new { n.TenantId, n.UserId })
             .HasDatabaseName("idx_notification_queue_tenant_user");
            // Keyword search on Title without parsing Payload JSONB per-query
            e.HasIndex(n => n.Title)
             .HasDatabaseName("idx_notification_queue_title_trgm")
             .HasMethod("gin")
             .HasOperators("gin_trgm_ops");
        });

        builder.Entity<CustomerMessageCampaign>(e =>
        {
            e.ToTable("customer_message_campaigns");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.Title).HasMaxLength(120).IsRequired();
            e.Property(x => x.Message).HasMaxLength(2000).IsRequired();
            e.Property(x => x.AudienceSource).HasMaxLength(50).IsRequired();
            e.Property(x => x.AudienceDefinition).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.Channels).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.MessengerProvider).HasMaxLength(30);
            e.Property(x => x.ContentType).HasMaxLength(30);
            e.Property(x => x.ContentTitle).HasMaxLength(200);
            e.Property(x => x.ContentImageUrl).HasMaxLength(2000);
            e.Property(x => x.DeliveryMode).HasMaxLength(30).IsRequired();
            e.Property(x => x.Status).HasMaxLength(30).IsRequired();
            e.Property(x => x.CreatedAt).HasDefaultValueSql("NOW()");
            e.HasIndex(x => new { x.TenantId, x.CreatedAt }).IsDescending(false, true)
             .HasDatabaseName("idx_customer_message_campaigns_tenant_created");
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Recipients).WithOne(x => x.Campaign).HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<CustomerMessageRecipient>(e =>
        {
            e.ToTable("customer_message_recipients");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.CreatedAt).HasDefaultValueSql("NOW()");
            e.HasIndex(x => new { x.CampaignId, x.CustomerId }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.CustomerId })
             .HasDatabaseName("idx_customer_message_recipients_tenant_customer");
            e.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        });

        // ── IntegrationConfig ───────────────────────────────────────────────
        builder.Entity<IntegrationConfig>(e =>
        {
            e.ToTable("integration_configs");
            e.HasKey(i => i.Id);
            e.Property(i => i.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(i => i.Service).HasMaxLength(50).IsRequired();
            e.Property(i => i.Config).HasColumnType("jsonb").HasDefaultValue("{}");
            e.Property(i => i.IsEnabled).HasDefaultValue(true);
            e.Property(i => i.CreatedAt).HasDefaultValueSql("NOW()");
            e.Property(i => i.UpdatedAt).HasDefaultValueSql("NOW()");
            e.HasIndex(i => new { i.TenantId, i.Service }).IsUnique();
            e.HasOne(i => i.Tenant).WithMany()
             .HasForeignKey(i => i.TenantId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── ActivityLog ─────────────────────────────────────────────────────
        builder.Entity<ActivityLog>(e =>
        {
            e.ToTable("activity_logs");
            e.HasKey(a => a.Id);
            e.Property(a => a.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(a => a.Action).HasMaxLength(100).IsRequired();
            e.Property(a => a.EntityType).HasMaxLength(50);
            e.Property(a => a.IpAddress).HasMaxLength(50);
            e.Property(a => a.Meta).HasColumnType("text");
            e.Property(a => a.CreatedAt).HasDefaultValueSql("NOW()");
            // Block 16 (pre-launch DB audit): table had ONLY the PK index — every RLS query
            // (which always adds "TenantId" = ... via tenant_isolation) and every repository
            // read (GetByTenantAsync/GetByUserAsync/GetFilteredAsync/GetAllTenantsAsync, all
            // ORDER BY CreatedAt DESC) forced a seq scan + sort. Currently invisible at 133
            // rows (dev), but this is a write-on-every-action audit trail with unbounded
            // growth — will matter fast in production.
            e.HasIndex(a => new { a.TenantId, a.CreatedAt })
             .IsDescending(false, true)
             .HasDatabaseName("idx_activity_logs_tenant_created");
            e.HasIndex(a => new { a.TenantId, a.UserId, a.CreatedAt })
             .IsDescending(false, false, true)
             .HasDatabaseName("idx_activity_logs_tenant_user_created");
            e.HasIndex(a => a.CreatedAt)
             .IsDescending(true)
             .HasDatabaseName("idx_activity_logs_created");
        });

        // ── DailySale (v2) ──────────────────────────────────────────────────
        builder.Entity<DailySale>(e =>
        {
            e.ToTable("daily_sales");
            e.HasKey(d => d.Id);
            e.Property(d => d.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(d => d.QuantitySold).HasColumnType("decimal(10,2)").IsRequired();
            e.Property(d => d.QuantityEndOfDay).HasColumnType("decimal(10,2)");
            e.Property(d => d.IsPromoDay).HasDefaultValue(false);
            e.Property(d => d.IsAnomaly).HasDefaultValue(false);
            e.Property(d => d.Source).HasMaxLength(20).HasDefaultValue("manual");
            e.Property(d => d.CreatedAt).HasDefaultValueSql("NOW()");
            e.Property(d => d.StoreId).HasColumnName("LocationId");
            e.HasIndex(d => new { d.StoreId, d.ProductId, d.Date }).IsUnique();
            e.HasIndex(d => new { d.TenantId, d.Date });
            // ADU source data: fetch recent sales per product+store
            e.HasIndex(d => new { d.TenantId, d.StoreId, d.ProductId, d.Date })
             .IsDescending(false, false, false, true)
             .HasDatabaseName("idx_daily_sales_tenant_store_product_date");
            e.HasOne(d => d.Product).WithMany()
             .HasForeignKey(d => d.ProductId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(d => d.Store).WithMany()
             .HasForeignKey(d => d.StoreId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── ProductAdu (v2) ─────────────────────────────────────────────────
        builder.Entity<ProductAdu>(e =>
        {
            e.ToTable("product_adu");
            e.HasKey(a => a.Id);
            e.Property(a => a.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(a => a.Adu30d).HasColumnType("decimal(10,4)");
            e.Property(a => a.Adu60d).HasColumnType("decimal(10,4)");
            e.Property(a => a.Adu90d).HasColumnType("decimal(10,4)");
            e.Property(a => a.AduEffective).HasColumnType("decimal(10,4)");
            e.Property(a => a.CalculatedAt).HasDefaultValueSql("NOW()");
            e.Property(a => a.StoreId).HasColumnName("LocationId");
            e.HasIndex(a => new { a.StoreId, a.ProductId }).IsUnique();
            e.HasOne(a => a.Product).WithMany()
             .HasForeignKey(a => a.ProductId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(a => a.Store).WithMany()
             .HasForeignKey(a => a.StoreId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── ProductBuffer (v2) ──────────────────────────────────────────────
        builder.Entity<ProductBuffer>(e =>
        {
            e.ToTable("product_buffer");
            e.HasKey(b => b.Id);
            e.Property(b => b.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(b => b.BufferTotal).HasColumnType("decimal(10,2)").IsRequired();
            e.Property(b => b.BufferGreen).HasColumnType("decimal(10,2)").IsRequired();
            e.Property(b => b.BufferYellow).HasColumnType("decimal(10,2)").IsRequired();
            e.Property(b => b.BufferRed).HasColumnType("decimal(10,2)").IsRequired();
            e.Property(b => b.LeadTimeDays).HasColumnType("decimal(5,1)").IsRequired();
            e.Property(b => b.OrderCycleDays).HasColumnType("decimal(5,1)").IsRequired();
            e.Property(b => b.CalculatedAt).HasDefaultValueSql("NOW()");
            e.Property(b => b.StoreId).HasColumnName("LocationId");
            e.HasIndex(b => new { b.StoreId, b.ProductId }).IsUnique();
            e.HasOne(b => b.Product).WithMany()
             .HasForeignKey(b => b.ProductId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(b => b.Store).WithMany()
             .HasForeignKey(b => b.StoreId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── TelegramLinkCode ────────────────────────────────────────────────
        builder.Entity<TelegramLinkCode>(e =>
        {
            e.ToTable("telegram_link_codes");
            e.HasKey(t => t.Id);
            e.Property(t => t.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(t => t.Code).HasMaxLength(16).IsRequired();
            e.Property(t => t.CreatedAt).HasDefaultValueSql("NOW()");
            e.HasIndex(t => t.Code).IsUnique();
            e.HasOne(t => t.User).WithMany()
             .HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── AiOrderSuggestion (v2) ──────────────────────────────────────────
        builder.Entity<AiOrderSuggestion>(e =>
        {
            e.ToTable("ai_order_suggestions");
            e.HasKey(s => s.Id);
            e.Property(s => s.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(s => s.GeneratedAt).HasDefaultValueSql("NOW()");
            e.Property(s => s.ContextSnapshot).HasColumnType("jsonb");
            e.Property(s => s.Status).HasMaxLength(30).HasDefaultValue("pending");
            e.Property(s => s.AiModel).HasMaxLength(50);
            e.Property(s => s.StoreId).HasColumnName("LocationId");
            e.HasIndex(s => new { s.TenantId, s.StoreId, s.OrderDate });
            e.HasOne(s => s.Store).WithMany()
             .HasForeignKey(s => s.StoreId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(s => s.Items).WithOne(i => i.Suggestion)
             .HasForeignKey(i => i.SuggestionId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<AiOrderSuggestionItem>(e =>
        {
            e.ToTable("ai_order_suggestion_items");
            e.HasKey(i => i.Id);
            e.Property(i => i.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(i => i.QuantityBase).HasColumnType("decimal(10,2)").IsRequired();
            e.Property(i => i.QuantitySuggested).HasColumnType("decimal(10,2)").IsRequired();
            e.Property(i => i.QuantityFinal).HasColumnType("decimal(10,2)").IsRequired();
            e.Property(i => i.Confidence).HasMaxLength(10);
            e.Property(i => i.Factors).HasColumnType("jsonb");
            e.Property(i => i.WasEdited).HasDefaultValue(false);
            e.HasIndex(i => i.SuggestionId);
            e.HasOne(i => i.Product).WithMany()
             .HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── PromoCannibalization (v2) ───────────────────────────────────────
        builder.Entity<PromoCannibalization>(e =>
        {
            e.ToTable("promo_cannibalization");
            e.HasKey(p => p.Id);
            e.Property(p => p.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(p => p.OrderCoefficient).HasColumnType("decimal(5,2)").IsRequired();
            e.Property(p => p.Source).HasMaxLength(20).HasDefaultValue("ai_suggested");
            e.Property(p => p.IsApplied).HasDefaultValue(false);
            e.Property(p => p.CreatedAt).HasDefaultValueSql("NOW()");
            e.HasIndex(p => new { p.DiscountId, p.AffectedProductId }).IsUnique();
            e.HasOne(p => p.Discount).WithMany()
             .HasForeignKey(p => p.DiscountId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(p => p.AffectedProduct).WithMany()
             .HasForeignKey(p => p.AffectedProductId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── DemandEvent (v2) ────────────────────────────────────────────────
        builder.Entity<DemandEvent>(e =>
        {
            e.ToTable("demand_events");
            e.HasKey(d => d.Id);
            e.Property(d => d.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(d => d.Name).HasMaxLength(255).IsRequired();
            e.Property(d => d.EventType).HasMaxLength(50).HasDefaultValue("custom");
            e.Property(d => d.Scope).HasMaxLength(50).HasDefaultValue("network");
            e.Property(d => d.RecurrenceRule).HasMaxLength(100);
            e.Property(d => d.CreatedAt).HasDefaultValueSql("NOW()");
            e.Property(d => d.StoreId).HasColumnName("LocationId");
            e.HasIndex(d => new { d.TenantId, d.StartsAt, d.EndsAt });
            e.HasOne(d => d.Store).WithMany()
             .HasForeignKey(d => d.StoreId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
            e.HasMany(d => d.Coefficients).WithOne(c => c.Event)
             .HasForeignKey(c => c.EventId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── DemandEventCoefficient (v2) ─────────────────────────────────────
        builder.Entity<DemandEventCoefficient>(e =>
        {
            e.ToTable("demand_event_coefficients");
            e.HasKey(c => c.Id);
            e.Property(c => c.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(c => c.ScopeType).HasMaxLength(20).IsRequired();
            e.Property(c => c.Coefficient).HasColumnType("decimal(5,2)").HasDefaultValue(1.00m);
            e.Property(c => c.Source).HasMaxLength(20).HasDefaultValue("manual");
            e.HasIndex(c => c.EventId);
        });

        // ── DemandEventStore (TASK-592): event ↔ specific-store links for Scope == "stores" ──
        builder.Entity<DemandEventStore>(e =>
        {
            e.ToTable("demand_event_stores");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.HasIndex(x => x.EventId);
            e.HasIndex(x => new { x.EventId, x.StoreId }).IsUnique();
            e.HasOne(x => x.Event).WithMany(d => d.Stores)
             .HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Location>().WithMany()
             .HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── WeatherData (v2) ────────────────────────────────────────────────
        builder.Entity<WeatherData>(e =>
        {
            e.ToTable("weather_data");
            e.HasKey(w => w.Id);
            e.Property(w => w.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(w => w.TempMin).HasColumnType("decimal(5,1)");
            e.Property(w => w.TempMax).HasColumnType("decimal(5,1)");
            e.Property(w => w.TempAvg).HasColumnType("decimal(5,1)");
            e.Property(w => w.Precipitation).HasColumnType("decimal(6,2)");
            e.Property(w => w.IsForecast).HasDefaultValue(true);
            e.Property(w => w.FetchedAt).HasDefaultValueSql("NOW()");
            e.Property(w => w.StoreId).HasColumnName("LocationId");
            e.HasIndex(w => new { w.StoreId, w.Date }).IsUnique();
            e.HasOne(w => w.Store).WithMany()
             .HasForeignKey(w => w.StoreId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── WeatherCoefficient (v2) ─────────────────────────────────────────
        builder.Entity<WeatherCoefficient>(e =>
        {
            e.ToTable("weather_coefficients");
            e.HasKey(w => w.Id);
            e.Property(w => w.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(w => w.TempAbove).HasColumnType("decimal(5,1)");
            e.Property(w => w.TempBelow).HasColumnType("decimal(5,1)");
            e.Property(w => w.Coefficient).HasColumnType("decimal(5,2)").IsRequired();
            e.Property(w => w.Source).HasMaxLength(20).HasDefaultValue("manual");
            e.HasIndex(w => w.TenantId);
            e.HasOne(w => w.Segment).WithMany()
             .HasForeignKey(w => w.SegmentId).OnDelete(DeleteBehavior.Cascade).IsRequired(false);
            // SetNull, not Cascade: PlatformCategory is global — soft-deleting/removing a
            // category must never cascade-delete a tenant's weather coefficient rows.
            e.HasOne(w => w.Category).WithMany()
             .HasForeignKey(w => w.CategoryId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
        });

        // ── SupplySchedule (v2) ─────────────────────────────────────────────
        builder.Entity<SupplySchedule>(e =>
        {
            e.ToTable("supply_schedules");
            e.HasKey(s => s.Id);
            e.Property(s => s.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(s => s.DayOfWeek).HasColumnType("integer[]");
            e.Property(s => s.IsActive).HasDefaultValue(true);
            e.Property(s => s.CreatedAt).HasDefaultValueSql("NOW()");
            e.Property(s => s.StoreId).HasColumnName("LocationId");
            e.HasIndex(s => new { s.StoreId, s.SupplierId });
            // Block 16 (pre-launch DB audit): GetAsync(storeId?, supplierId?) — both filters
            // optional. When the tenant Settings page loads the schedule list with no filter
            // (the common case), the query becomes RLS-"TenantId"-only with no other index to
            // fall back on — full-table scan across every tenant's schedules.
            e.HasIndex(s => s.TenantId)
             .HasDatabaseName("idx_supply_schedules_tenant");
            e.HasOne(s => s.Store).WithMany()
             .HasForeignKey(s => s.StoreId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(s => s.Supplier).WithMany()
             .HasForeignKey(s => s.SupplierId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── IotDevice (v3) ──────────────────────────────────────────────────
        builder.Entity<IotDevice>(e =>
        {
            e.ToTable("iot_devices");
            e.HasKey(d => d.Id);
            e.Property(d => d.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(d => d.DeviceType).HasMaxLength(50).IsRequired();
            e.Property(d => d.DeviceId).HasMaxLength(100).IsRequired();
            e.Property(d => d.Name).HasMaxLength(255);
            e.Property(d => d.MqttTopic).HasMaxLength(255);
            e.Property(d => d.Config).HasColumnType("jsonb");
            e.Property(d => d.IsActive).HasDefaultValue(true);
            e.Property(d => d.FirmwareVersion).HasMaxLength(50);
            e.Property(d => d.CreatedAt).HasDefaultValueSql("NOW()");
            e.Property(d => d.StoreId).HasColumnName("LocationId");
            e.HasIndex(d => new { d.TenantId, d.DeviceId }).IsUnique();
            e.HasIndex(d => new { d.TenantId, d.StoreId });
            e.HasOne(d => d.Store).WithMany()
             .HasForeignKey(d => d.StoreId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(d => d.Zone).WithMany()
             .HasForeignKey(d => d.ZoneId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
        });

        // ── TemperatureReading (v3) ─────────────────────────────────────────
        builder.Entity<TemperatureReading>(e =>
        {
            e.ToTable("temperature_readings");
            e.HasKey(r => r.Id);
            e.Property(r => r.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(r => r.Temperature).HasColumnType("decimal(5,1)").IsRequired();
            e.Property(r => r.Humidity).HasColumnType("decimal(5,1)");
            e.Property(r => r.IsAlert).HasDefaultValue(false);
            e.Property(r => r.CreatedAt).HasDefaultValueSql("NOW()");
            e.Property(r => r.StoreId).HasColumnName("LocationId");
            e.HasIndex(r => new { r.DeviceId, r.RecordedAt }).IsDescending(false, true);
            e.HasOne(r => r.Device).WithMany()
             .HasForeignKey(r => r.DeviceId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── WeightReading (v3) ──────────────────────────────────────────────
        builder.Entity<WeightReading>(e =>
        {
            e.ToTable("weight_readings");
            e.HasKey(r => r.Id);
            e.Property(r => r.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(r => r.WeightBefore).HasColumnType("decimal(10,2)");
            e.Property(r => r.WeightAfter).HasColumnType("decimal(10,2)");
            e.Property(r => r.DeltaWeight).HasColumnType("decimal(10,2)");
            e.Property(r => r.Processed).HasDefaultValue(false);
            e.HasIndex(r => new { r.DeviceId, r.RecordedAt }).IsDescending(false, true);
            e.HasIndex(r => r.Processed).HasFilter("\"Processed\" = false");
            e.HasOne(r => r.Device).WithMany()
             .HasForeignKey(r => r.DeviceId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── PosShift (v3 Phase 4) ───────────────────────────────────────────
        builder.Entity<PosShift>(e =>
        {
            e.ToTable("pos_shifts");
            e.HasKey(s => s.Id);
            e.Property(s => s.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(s => s.FiscalShiftNumber).HasMaxLength(50);
            e.Property(s => s.OpeningCash).HasColumnType("decimal(12,2)");
            e.Property(s => s.ClosingCash).HasColumnType("decimal(12,2)");
            e.Property(s => s.TotalSales).HasColumnType("decimal(12,2)").HasDefaultValue(0m);
            e.Property(s => s.OpenedAt).HasDefaultValueSql("NOW()");
            e.Property(s => s.StoreId).HasColumnName("LocationId");
            e.HasIndex(s => new { s.TenantId, s.StoreId, s.OpenedAt });
            // one open shift per location at a time
            e.HasIndex(s => s.StoreId).IsUnique().HasFilter("\"ClosedAt\" IS NULL");
            e.HasOne(s => s.Store).WithMany()
             .HasForeignKey(s => s.StoreId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(s => s.Cashier).WithMany()
             .HasForeignKey(s => s.CashierId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
        });

        // ── PosTransaction (v3 Phase 4) ─────────────────────────────────────
        builder.Entity<PosTransaction>(e =>
        {
            e.ToTable("pos_transactions");
            e.HasKey(t => t.Id);
            e.Property(t => t.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(t => t.ReceiptNumber).HasMaxLength(50).IsRequired();
            e.Property(t => t.FiscalNumber).HasMaxLength(100);
            e.Property(t => t.PaymentType).HasMaxLength(20).HasDefaultValue("cash");
            e.Property(t => t.TotalAmount).HasColumnType("decimal(12,2)");
            e.Property(t => t.TaxAmount).HasColumnType("decimal(12,2)");
            e.Property(t => t.Status).HasMaxLength(30).HasDefaultValue("pending_fiscalization");
            e.Property(t => t.CreatedAt).HasDefaultValueSql("NOW()");
            e.Property(t => t.StoreId).HasColumnName("LocationId");
            e.HasIndex(t => new { t.TenantId, t.StoreId, t.CreatedAt });
            e.HasIndex(t => t.Status).HasFilter("\"Status\" = 'pending_fiscalization'");
            e.HasIndex(t => new { t.TenantId, t.ReceiptNumber }).IsUnique();
            // Reporting: exclude failed fiscalization records from dashboard queries
            e.HasIndex(t => new { t.TenantId, t.StoreId, t.CreatedAt })
             .IsDescending(false, false, true)
             .HasDatabaseName("idx_pos_transactions_excl_failed")
             .HasFilter("\"Status\" <> 'fiscalization_failed'");
            e.HasOne(t => t.Store).WithMany()
             .HasForeignKey(t => t.StoreId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(t => t.Shift).WithMany()
             .HasForeignKey(t => t.ShiftId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
            e.HasMany(t => t.Items).WithOne(i => i.Transaction)
             .HasForeignKey(i => i.TransactionId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── PosTransactionItem (v3 Phase 4) ─────────────────────────────────
        builder.Entity<PosTransactionItem>(e =>
        {
            e.ToTable("pos_transaction_items");
            e.HasKey(i => i.Id);
            e.Property(i => i.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(i => i.Quantity).HasColumnType("decimal(10,2)").IsRequired();
            e.Property(i => i.PriceRetail).HasColumnType("decimal(12,2)");
            e.Property(i => i.DiscountAmount).HasColumnType("decimal(12,2)").HasDefaultValue(0m);
            e.Property(i => i.PriceFinal).HasColumnType("decimal(12,2)");
            e.HasIndex(i => i.TransactionId);
            // Covering index: load receipt lines without heap fetch
            e.HasIndex(i => i.TransactionId)
             .HasDatabaseName("idx_pos_transaction_items_txn_covering")
             .IncludeProperties(i => new { i.ProductId, i.PriceFinal, i.Quantity });
            // Covering index: per-product sales trend (TASK-479/482) — ProductId-leading so it also
            // serves as the FK-lookup index for Product below, TransactionId as 2nd key column so the
            // planner can range/join into pos_transactions for the date filter without a heap fetch.
            e.HasIndex(i => new { i.ProductId, i.TransactionId })
             .HasDatabaseName("idx_pos_transaction_items_product_covering")
             .IncludeProperties(i => new { i.Quantity, i.PriceFinal });
            e.HasOne(i => i.Product).WithMany()
             .HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ProductStock>().WithMany()
             .HasForeignKey(i => i.ProductStockId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
        });

        // ── SupportTicket ───────────────────────────────────────────────────
        builder.Entity<SupportTicket>(e =>
        {
            e.ToTable("support_tickets");
            e.HasKey(t => t.Id);
            e.Property(t => t.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(t => t.Number).ValueGeneratedOnAdd();
            e.Property(t => t.Title).IsRequired();
            e.Property(t => t.Description).IsRequired();
            e.Property(t => t.Category).HasMaxLength(50).HasDefaultValue("general").IsRequired();
            e.Property(t => t.Status).HasMaxLength(30).HasDefaultValue("open").IsRequired();
            e.Property(t => t.Priority).HasMaxLength(20).HasDefaultValue("medium").IsRequired();
            e.Property(t => t.CreatedByProvider).HasDefaultValue(false);
            e.Property(t => t.CreatedAt).HasDefaultValueSql("NOW()");
            e.Property(t => t.UpdatedAt).HasDefaultValueSql("NOW()");
            // Composite: tenant + status + created_at DESC — main list query
            e.HasIndex(t => new { t.TenantId, t.Status, t.CreatedAt })
             .IsDescending(false, false, true)
             .HasDatabaseName("idx_tickets_tenant_status");
            // Assigned agent filter (partial: only assigned rows)
            e.HasIndex(t => new { t.TenantId, t.AssignedTo })
             .HasDatabaseName("idx_tickets_assigned")
             .HasFilter("\"AssignedTo\" IS NOT NULL");
            // Creator lookup
            e.HasIndex(t => new { t.TenantId, t.CreatedBy })
             .HasDatabaseName("idx_tickets_created_by");
            // FK: creator
            e.HasOne(t => t.CreatedByUser).WithMany()
             .HasForeignKey(t => t.CreatedBy).OnDelete(DeleteBehavior.Cascade);
            // FK: assignee
            e.HasOne(t => t.AssignedToUser).WithMany()
             .HasForeignKey(t => t.AssignedTo).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
            // FK: location
            e.HasOne(t => t.Location).WithMany()
             .HasForeignKey(t => t.LocationId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
            // Legacy messages
            e.HasMany(t => t.Messages).WithOne(m => m.Ticket)
             .HasForeignKey(m => m.TicketId).OnDelete(DeleteBehavior.Cascade);
            // New comments
            e.HasMany(t => t.Comments).WithOne(c => c.Ticket)
             .HasForeignKey(c => c.TicketId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── SupportMessage ──────────────────────────────────────────────────
        builder.Entity<SupportMessage>(e =>
        {
            e.ToTable("support_messages");
            e.HasKey(m => m.Id);
            e.Property(m => m.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(m => m.Body).IsRequired();
            e.HasIndex(m => m.TicketId);
        });

        // ── TicketComment ───────────────────────────────────────────────────
        builder.Entity<TicketComment>(e =>
        {
            e.ToTable("ticket_comments");
            e.HasKey(c => c.Id);
            e.Property(c => c.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(c => c.Body).IsRequired();
            e.Property(c => c.IsInternal).HasDefaultValue(false);
            e.Property(c => c.CreatedAt).HasDefaultValueSql("NOW()");
            // Load comments for a ticket ordered by time
            e.HasIndex(c => new { c.TicketId, c.CreatedAt })
             .HasDatabaseName("idx_ticket_comments_ticket");
            // Ticket FK is wired via SupportTicket.HasMany above (Cascade)
            e.HasOne(c => c.Author).WithMany()
             .HasForeignKey(c => c.AuthorId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── SupplierProfile (v4 Marketplace) ───────────────────────────────
        builder.Entity<SupplierProfile>(e =>
        {
            e.ToTable("supplier_profiles");
            e.HasKey(p => p.Id);
            e.Property(p => p.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(p => p.Region).HasColumnType("text");
            e.Property(p => p.Categories).HasColumnType("jsonb");
            e.Property(p => p.Website).HasColumnType("text");
#pragma warning disable CS0618 // DeliveryRegions is [Obsolete] (TASK-649) but the column is kept for backfill
            e.Property(p => p.DeliveryRegions).HasColumnType("jsonb");
#pragma warning restore CS0618
            // TASK-649: structured coverage jsonb (served/notServed/note); supersedes DeliveryRegions.
            e.Property(p => p.DeliveryCoverage).HasColumnType("jsonb");
            e.Property(p => p.WorkingHours).HasColumnType("text");
            e.Property(p => p.PaymentTerms).HasColumnType("text");
            e.Property(p => p.IsPublic).HasDefaultValue(false);
            e.Property(p => p.IsOwnerManaged).HasDefaultValue(false);
            e.Property(p => p.Plan).HasMaxLength(50).HasDefaultValue("free");
            e.Property(p => p.CreatedAt).HasDefaultValueSql("NOW()");
            e.Property(p => p.UpdatedAt).HasDefaultValueSql("NOW()");
            // 1-to-1: one profile per supplier
            e.HasIndex(p => p.SupplierId).IsUnique();
            e.HasIndex(p => p.TenantId);
            // ADR-016: deterministic "my profile" lookup — at most one
            // owner-managed profile per supplier tenant.
            e.HasIndex(p => p.TenantId, "UX_supplier_profiles_owner_tenant")
             .IsUnique()
             .HasFilter("\"IsOwnerManaged\"");
            e.HasOne(p => p.Supplier).WithMany()
             .HasForeignKey(p => p.SupplierId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(p => p.Tenant).WithMany()
             .HasForeignKey(p => p.TenantId).OnDelete(DeleteBehavior.Restrict);
        });

        // ── SupplierItem (v4 Marketplace) ──────────────────────────────────
        builder.Entity<SupplierItem>(e =>
        {
            e.ToTable("supplier_items");
            e.HasKey(i => i.Id);
            e.Property(i => i.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(i => i.CustomName).HasColumnType("text");
            e.Property(i => i.Price).HasColumnType("numeric(12,2)");
            e.Property(i => i.Unit).HasColumnType("text");
            e.Property(i => i.IsAvailable).HasDefaultValue(true);
            e.Property(i => i.Category).HasColumnType("text");
            // Value converter (not Npgsql dynamic-json) so the model also works under
            // EF Core InMemory (used by ShelfGuard.Tests), which cannot map Dictionary<string, object?> directly.
            e.Property(i => i.Attributes)
             .HasColumnType("jsonb")
             .HasConversion(
                 v => v == null ? null : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                 v => v == null ? null : JsonSerializer.Deserialize<Dictionary<string, object?>>(v, (JsonSerializerOptions?)null))
             .Metadata.SetValueComparer(new ValueComparer<Dictionary<string, object?>?>(
                 (a, b) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null) == JsonSerializer.Serialize(b, (JsonSerializerOptions?)null),
                 v => v == null ? 0 : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(),
                 v => v == null ? null : JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(v, (JsonSerializerOptions?)null), (JsonSerializerOptions?)null)));
            e.Property(i => i.CreatedAt).HasDefaultValueSql("NOW()");
            // Universal fields (apply regardless of Category) — TASK-299
            e.Property(i => i.Brand).HasColumnType("text");
            e.Property(i => i.Manufacturer).HasColumnType("text");
            e.Property(i => i.ManufacturerCountry).HasColumnType("text");
            e.Property(i => i.GrossWeightKg).HasColumnType("numeric(10,3)");
            e.Property(i => i.HeightCm).HasColumnType("numeric(10,2)");
            e.Property(i => i.DepthCm).HasColumnType("numeric(10,2)");
            e.Property(i => i.WidthCm).HasColumnType("numeric(10,2)");
            e.HasIndex(i => new { i.SupplierId, i.TenantId });
            e.HasIndex(i => i.ItemId);
            e.HasOne(i => i.Supplier).WithMany()
             .HasForeignKey(i => i.SupplierId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(i => i.Tenant).WithMany()
             .HasForeignKey(i => i.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(i => i.Item).WithMany()
             .HasForeignKey(i => i.ItemId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
            e.HasMany(i => i.Barcodes).WithOne(b => b.SupplierItem)
             .HasForeignKey(b => b.SupplierItemId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(i => i.Images).WithOne(img => img.SupplierItem)
             .HasForeignKey(img => img.SupplierItemId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── SupplierItemBarcode (v4 Marketplace, universal fields) ─────────
        builder.Entity<SupplierItemBarcode>(e =>
        {
            e.ToTable("supplier_item_barcodes");
            e.HasKey(b => b.Id);
            e.Property(b => b.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(b => b.Barcode).HasColumnType("text").IsRequired();
            e.Property(b => b.Kind).HasColumnType("text").HasDefaultValue("primary");
            e.Property(b => b.CreatedAt).HasDefaultValueSql("NOW()");
            e.HasIndex(b => new { b.SupplierItemId, b.Barcode }).IsUnique();
            e.HasIndex(b => b.TenantId);
            e.HasOne(b => b.Tenant).WithMany()
             .HasForeignKey(b => b.TenantId).OnDelete(DeleteBehavior.Restrict);
            // SupplierItemId FK is wired via SupplierItem.HasMany above (Cascade)
        });

        // ── SupplierItemImage (v4 Marketplace, universal fields) ───────────
        builder.Entity<SupplierItemImage>(e =>
        {
            e.ToTable("supplier_item_images");
            e.HasKey(img => img.Id);
            e.Property(img => img.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(img => img.Url).HasColumnType("text").IsRequired();
            e.Property(img => img.Kind).HasColumnType("text").HasDefaultValue("gallery");
            e.Property(img => img.SortOrder).HasDefaultValue(0);
            e.Property(img => img.CreatedAt).HasDefaultValueSql("NOW()");
            e.HasIndex(img => img.SupplierItemId);
            e.HasIndex(img => img.TenantId);
            e.HasOne(img => img.Tenant).WithMany()
             .HasForeignKey(img => img.TenantId).OnDelete(DeleteBehavior.Restrict);
            // SupplierItemId FK is wired via SupplierItem.HasMany above (Cascade)
        });

        // ── SupplierStock (supplier-portal expansion Phase 2, D2) ──────────
        // Parallel to ProductStock — keyed on SupplierItemId + WarehouseId, NO store_scope
        // RLS policy (supplier tenants have no user_locations). FEFO logic is duplicated,
        // not extracted (see SupplierStockService).
        builder.Entity<SupplierStock>(e =>
        {
            e.ToTable("supplier_stock");
            e.HasKey(s => s.Id);
            e.Property(s => s.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(s => s.Quantity).HasColumnType("numeric(12,3)").IsRequired();
            e.Property(s => s.QuantityInitial).HasColumnType("numeric(12,3)").IsRequired();
            e.Property(s => s.ExpiryDate).HasColumnType("date").IsRequired();
            e.Property(s => s.BatchNumber).HasMaxLength(100);
            e.Property(s => s.Status).HasMaxLength(30).HasDefaultValue("safe");
            e.Property(s => s.SourceType).HasMaxLength(40);
            e.Property(s => s.AddedAt).HasDefaultValueSql("NOW()");
            e.Property(s => s.LastCheckedAt).HasDefaultValueSql("NOW()");
            // TASK-681: same optimistic-concurrency token as ProductStock (TASK-356) — two
            // concurrent writers decrementing the same batch's Quantity (a shipment racing an
            // adjust) would otherwise last-write-wins and silently oversell. No schema change —
            // xmin already exists on every row.
            e.Property<uint>("xmin").IsRowVersion().HasColumnName("xmin");
            // FEFO active stock — the critical query path (mirrors idx_stock_fefo_active).
            e.HasIndex(s => new { s.TenantId, s.WarehouseId, s.SupplierItemId, s.ExpiryDate })
             .HasDatabaseName("ix_supplier_stock_fefo")
             .HasFilter("\"Quantity\" > 0");
            e.HasOne(s => s.SupplierItem).WithMany()
             .HasForeignKey(s => s.SupplierItemId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(s => s.Warehouse).WithMany()
             .HasForeignKey(s => s.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        });

        // ── SupplierStockMovement (supplier-portal expansion Phase 2, D2) ──
        builder.Entity<SupplierStockMovement>(e =>
        {
            e.ToTable("supplier_stock_movements");
            e.HasKey(m => m.Id);
            e.Property(m => m.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(m => m.MovementType).HasMaxLength(20).IsRequired();
            e.Property(m => m.Quantity).HasColumnType("numeric(12,3)").IsRequired();
            e.Property(m => m.QuantityBefore).HasColumnType("numeric(12,3)").IsRequired();
            e.Property(m => m.QuantityAfter).HasColumnType("numeric(12,3)").IsRequired();
            e.Property(m => m.ReferenceType).HasMaxLength(40);
            e.Property(m => m.Notes).HasMaxLength(500);
            e.Property(m => m.CreatedAt).HasDefaultValueSql("NOW()");
            e.HasIndex(m => new { m.TenantId, m.SupplierStockId })
             .HasDatabaseName("ix_supplier_stock_movements_tenant_stock");
            e.HasIndex(m => new { m.TenantId, m.CreatedAt })
             .HasDatabaseName("ix_supplier_stock_movements_tenant_created");
            e.HasOne<SupplierStock>().WithMany()
             .HasForeignKey(m => m.SupplierStockId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SupplierItem>().WithMany()
             .HasForeignKey(m => m.SupplierItemId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Location>().WithMany()
             .HasForeignKey(m => m.FromWarehouseId).OnDelete(DeleteBehavior.Restrict).IsRequired(false);
            e.HasOne<Location>().WithMany()
             .HasForeignKey(m => m.ToWarehouseId).OnDelete(DeleteBehavior.Restrict).IsRequired(false);
        });

        // ── SupplierStockReceipt (supplier-portal expansion Phase 2, D3) ───
        builder.Entity<SupplierStockReceipt>(e =>
        {
            e.ToTable("supplier_stock_receipts");
            e.HasKey(r => r.Id);
            e.Property(r => r.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(r => r.Status).HasMaxLength(20).HasDefaultValue("draft");
            e.Property(r => r.Reference).HasMaxLength(100);
            e.Property(r => r.Notes).HasMaxLength(500);
            e.Property(r => r.CreatedAt).HasDefaultValueSql("NOW()");
            e.HasIndex(r => new { r.TenantId, r.WarehouseId, r.Status })
             .HasDatabaseName("ix_supplier_stock_receipts_tenant_warehouse_status");
            e.HasOne(r => r.Warehouse).WithMany()
             .HasForeignKey(r => r.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(r => r.Items).WithOne(i => i.Receipt)
             .HasForeignKey(i => i.ReceiptId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── SupplierStockReceiptItem (supplier-portal expansion Phase 2, D3) ─
        // N rows may share SupplierItemId — one row per (expiry, batch). TenantId is
        // denormalized so RLS stays a plain tenant_isolation with no join.
        builder.Entity<SupplierStockReceiptItem>(e =>
        {
            e.ToTable("supplier_stock_receipt_items");
            e.HasKey(i => i.Id);
            e.Property(i => i.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(i => i.Quantity).HasColumnType("numeric(12,3)").IsRequired();
            e.Property(i => i.ExpiryDate).HasColumnType("date");
            e.Property(i => i.BatchNumber).HasMaxLength(100);
            e.Property(i => i.UnitCost).HasColumnType("numeric(12,2)");
            e.Property(i => i.Notes).HasMaxLength(300);
            e.HasIndex(i => i.ReceiptId)
             .HasDatabaseName("ix_supplier_stock_receipt_items_receipt");
            e.HasIndex(i => i.TenantId)
             .HasDatabaseName("ix_supplier_stock_receipt_items_tenant");
            e.HasOne(i => i.SupplierItem).WithMany()
             .HasForeignKey(i => i.SupplierItemId).OnDelete(DeleteBehavior.Restrict);
            // ReceiptId FK is wired via SupplierStockReceipt.HasMany above (Cascade)
        });

        // ── SupplierMetrics (v4 Marketplace) ───────────────────────────────
        builder.Entity<SupplierMetrics>(e =>
        {
            e.ToTable("supplier_metrics");
            e.HasKey(m => m.Id);
            e.Property(m => m.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(m => m.AvgDeliveryDays).HasColumnType("numeric(5,2)");
            e.Property(m => m.OrderAccuracy).HasColumnType("numeric(5,4)");
            e.Property(m => m.QualityScore).HasColumnType("numeric(5,4)");
            e.Property(m => m.Rating).HasColumnType("numeric(3,2)");
            e.Property(m => m.CancellationRate).HasColumnType("numeric(5,4)");
            e.Property(m => m.ResponseTimeHours).HasColumnType("numeric(6,2)");
            // TASK-649: worker-computed aggregates. DeliveryByRegion is a jsonb array
            // [{ regionCode, avgDeliveryDays, sampleSize }]; the other three use default mapping.
            e.Property(m => m.DeliveryByRegion).HasColumnType("jsonb");
            e.Property(m => m.UpdatedAt).HasDefaultValueSql("NOW()");
            // 1-to-1: one metrics record per supplier
            e.HasIndex(m => m.SupplierId).IsUnique();
            e.HasIndex(m => m.TenantId);
            e.HasOne(m => m.Supplier).WithMany()
             .HasForeignKey(m => m.SupplierId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(m => m.Tenant).WithMany()
             .HasForeignKey(m => m.TenantId).OnDelete(DeleteBehavior.Restrict);
        });

        // ── SupplierMetricsSnapshot (TASK-670) ─────────────────────────────
        // Append-only daily copy of supplier_metrics aggregates, written by the
        // nightly supplier-metrics worker job via an idempotent upsert on
        // (SupplierId, SnapshotDate). Feeds the buyer-facing metric trend-chart
        // detail page. Column types mirror SupplierMetrics above. RLS triad
        // (tenant_isolation + provider_bypass + worker_bypass) is applied in the
        // migration, not here (no EF fluent API for CREATE POLICY).
        builder.Entity<SupplierMetricsSnapshot>(e =>
        {
            e.ToTable("supplier_metrics_snapshots");
            e.HasKey(s => s.Id);
            e.Property(s => s.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(s => s.SnapshotDate).HasColumnType("date").IsRequired();
            e.Property(s => s.AvgDeliveryDays).HasColumnType("numeric(5,2)");
            e.Property(s => s.OrderAccuracy).HasColumnType("numeric(5,4)");
            e.Property(s => s.QualityScore).HasColumnType("numeric(5,4)");
            e.Property(s => s.Rating).HasColumnType("numeric(3,2)");
            e.Property(s => s.CancellationRate).HasColumnType("numeric(5,4)");
            e.Property(s => s.ResponseTimeHours).HasColumnType("numeric(6,2)");
            e.Property(s => s.DeliverySampleSize).HasColumnType("integer");
            e.Property(s => s.ResponseSampleSize).HasColumnType("integer");
            e.Property(s => s.CreatedAt).HasDefaultValueSql("NOW()");
            // Idempotent upsert key — worker does ON CONFLICT (SupplierId, SnapshotDate).
            // Also serves the buyer history query (WHERE SupplierId = ? ORDER BY SnapshotDate
            // DESC) via a backward index scan — no dedicated DESC index needed.
            e.HasIndex(s => new { s.SupplierId, s.SnapshotDate })
             .IsUnique()
             .HasDatabaseName("idx_supplier_metrics_snapshots_supplier_date");
            e.HasIndex(s => s.TenantId);
            e.HasOne(s => s.Supplier).WithMany()
             .HasForeignKey(s => s.SupplierId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(s => s.Tenant).WithMany()
             .HasForeignKey(s => s.TenantId).OnDelete(DeleteBehavior.Restrict);
        });

        // ── SupplierReview (v4 Marketplace) ────────────────────────────────
        builder.Entity<SupplierReview>(e =>
        {
            e.ToTable("supplier_reviews");
            e.HasKey(r => r.Id);
            e.Property(r => r.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(r => r.Rating).IsRequired();
            e.Property(r => r.Comment).HasColumnType("text");
            e.Property(r => r.CreatedAt).HasDefaultValueSql("NOW()");
            e.Property(r => r.ReplyText).HasColumnType("text");
            // One review per (supplier, tenant)
            e.HasIndex(r => new { r.SupplierId, r.TenantId }).IsUnique();
            e.HasOne(r => r.Supplier).WithMany()
             .HasForeignKey(r => r.SupplierId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(r => r.Tenant).WithMany()
             .HasForeignKey(r => r.TenantId).OnDelete(DeleteBehavior.Restrict);
        });

        // ── AsCustomer (v4 Auto Service) ────────────────────────────────────
        builder.Entity<AsCustomer>(e =>
        {
            e.ToTable("as_customers");
            e.HasKey(c => c.Id);
            e.Property(c => c.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(c => c.Name).HasColumnType("text").IsRequired();
            e.Property(c => c.Phone).HasColumnType("text");
            e.Property(c => c.Email).HasColumnType("text");
            e.Property(c => c.Notes).HasColumnType("text");
            e.Property(c => c.CreatedAt).HasDefaultValueSql("NOW()");
            e.Property(c => c.UpdatedAt).HasDefaultValueSql("NOW()");
            e.HasIndex(c => c.TenantId);
            e.HasOne(c => c.Tenant).WithMany()
             .HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(c => c.Vehicles).WithOne(v => v.Customer)
             .HasForeignKey(v => v.CustomerId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── AsVehicle (v4 Auto Service) ─────────────────────────────────────
        builder.Entity<AsVehicle>(e =>
        {
            e.ToTable("as_vehicles");
            e.HasKey(v => v.Id);
            e.Property(v => v.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(v => v.Brand).HasColumnType("text").IsRequired();
            e.Property(v => v.Model).HasColumnType("text").IsRequired();
            e.Property(v => v.Year).HasColumnType("smallint");
            e.Property(v => v.Vin).HasColumnType("text");
            e.Property(v => v.LicensePlate).HasColumnType("text");
            e.Property(v => v.Notes).HasColumnType("text");
            e.Property(v => v.CreatedAt).HasDefaultValueSql("NOW()");
            e.Property(v => v.UpdatedAt).HasDefaultValueSql("NOW()");
            e.HasIndex(v => v.TenantId);
            e.HasIndex(v => v.CustomerId);
            e.HasOne(v => v.Tenant).WithMany()
             .HasForeignKey(v => v.TenantId).OnDelete(DeleteBehavior.Restrict);
            // Customer FK is wired via AsCustomer.HasMany above (Cascade)
        });

        // ── AsServiceCatalog (v4 Auto Service) ──────────────────────────────
        builder.Entity<AsServiceCatalog>(e =>
        {
            e.ToTable("as_service_catalog");
            e.HasKey(s => s.Id);
            e.Property(s => s.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(s => s.Name).HasColumnType("text").IsRequired();
            e.Property(s => s.Description).HasColumnType("text");
            e.Property(s => s.DefaultPrice).HasColumnType("numeric(12,2)");
            e.Property(s => s.DurationHours).HasColumnType("numeric(4,2)");
            e.Property(s => s.IsActive).HasDefaultValue(true);
            e.Property(s => s.CreatedAt).HasDefaultValueSql("NOW()");
            e.HasIndex(s => s.TenantId);
            e.HasOne(s => s.Tenant).WithMany()
             .HasForeignKey(s => s.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(s => s.Item).WithMany()
             .HasForeignKey(s => s.ItemId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
        });

        // ── AsWorkOrder (v4 Auto Service) ───────────────────────────────────
        builder.Entity<AsWorkOrder>(e =>
        {
            e.ToTable("as_work_orders");
            e.HasKey(w => w.Id);
            e.Property(w => w.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(w => w.Status)
             .HasConversion<string>()
             .HasMaxLength(30)
             .HasDefaultValue(WorkOrderStatus.New);
            e.Property(w => w.Notes).HasColumnType("text");
            e.Property(w => w.CreatedAt).HasDefaultValueSql("NOW()");
            e.Property(w => w.UpdatedAt).HasDefaultValueSql("NOW()");
            e.HasIndex(w => w.TenantId);
            e.HasIndex(w => w.VehicleId);
            e.HasIndex(w => w.Status);
            e.HasOne(w => w.Tenant).WithMany()
             .HasForeignKey(w => w.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(w => w.Vehicle).WithMany(v => v.WorkOrders)
             .HasForeignKey(w => w.VehicleId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(w => w.Mechanic).WithMany()
             .HasForeignKey(w => w.MechanicUserId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
            e.HasMany(w => w.Lines).WithOne(l => l.WorkOrder)
             .HasForeignKey(l => l.WorkOrderId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── AsWorkOrderLine (v4 Auto Service) ───────────────────────────────
        builder.Entity<AsWorkOrderLine>(e =>
        {
            e.ToTable("as_work_order_lines");
            e.HasKey(l => l.Id);
            e.Property(l => l.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(l => l.Type).HasMaxLength(20).IsRequired();
            e.Property(l => l.Qty).HasColumnType("numeric(10,3)").HasDefaultValue(1m);
            e.Property(l => l.Price).HasColumnType("numeric(12,2)").IsRequired();
            e.Property(l => l.Discount).HasColumnType("numeric(12,2)").HasDefaultValue(0m);
            e.HasIndex(l => l.WorkOrderId);
            // WorkOrder FK is wired via AsWorkOrder.HasMany above (Cascade)
            e.HasOne(l => l.ServiceCatalog).WithMany()
             .HasForeignKey(l => l.ServiceCatalogId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
            e.HasOne(l => l.Item).WithMany()
             .HasForeignKey(l => l.ItemId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
        });

        // ── Recipe (v4 Production) ───────────────────────────────────────────
        builder.Entity<Recipe>(e =>
        {
            e.ToTable("recipes");
            e.HasKey(r => r.Id);
            e.Property(r => r.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(r => r.Name).HasColumnType("text").IsRequired();
            e.Property(r => r.OutputQty).HasColumnType("numeric(10,3)").IsRequired();
            e.Property(r => r.Unit).HasColumnType("text").IsRequired();
            e.Property(r => r.Notes).HasColumnType("text");
            e.Property(r => r.IsActive).HasDefaultValue(true);
            e.Property(r => r.CreatedAt).HasDefaultValueSql("NOW()");
            e.Property(r => r.UpdatedAt).HasDefaultValueSql("NOW()");
            e.HasIndex(r => r.TenantId);
            e.HasOne(r => r.Tenant).WithMany()
             .HasForeignKey(r => r.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(r => r.OutputItem).WithMany()
             .HasForeignKey(r => r.OutputItemId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(r => r.Ingredients).WithOne(i => i.Recipe)
             .HasForeignKey(i => i.RecipeId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── RecipeIngredient (v4 Production) ────────────────────────────────
        builder.Entity<RecipeIngredient>(e =>
        {
            e.ToTable("recipe_ingredients");
            e.HasKey(i => i.Id);
            e.Property(i => i.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(i => i.Qty).HasColumnType("numeric(10,3)").IsRequired();
            e.Property(i => i.Unit).HasColumnType("text").IsRequired();
            e.HasIndex(i => i.RecipeId);
            // Recipe FK is wired via Recipe.HasMany above (Cascade)
            e.HasOne(i => i.Item).WithMany()
             .HasForeignKey(i => i.ItemId).OnDelete(DeleteBehavior.Restrict);
        });

        // ── ProductionOrder (v4 Production) ─────────────────────────────────
        builder.Entity<ProductionOrder>(e =>
        {
            e.ToTable("production_orders");
            e.HasKey(o => o.Id);
            e.Property(o => o.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(o => o.PlannedQty).HasColumnType("numeric(10,3)").IsRequired();
            e.Property(o => o.Status)
             .HasConversion<string>()
             .HasMaxLength(30)
             .HasDefaultValue(ProductionOrderStatus.Planned);
            e.Property(o => o.Notes).HasColumnType("text");
            e.Property(o => o.CreatedAt).HasDefaultValueSql("NOW()");
            e.Property(o => o.UpdatedAt).HasDefaultValueSql("NOW()");
            e.HasIndex(o => o.TenantId);
            e.HasIndex(o => o.RecipeId);
            e.HasIndex(o => o.Status);
            e.HasOne(o => o.Tenant).WithMany()
             .HasForeignKey(o => o.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(o => o.Recipe).WithMany()
             .HasForeignKey(o => o.RecipeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(o => o.Location).WithMany()
             .HasForeignKey(o => o.LocationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(o => o.Creator).WithMany()
             .HasForeignKey(o => o.CreatedBy).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
            e.HasMany(o => o.Consumptions).WithOne(c => c.ProductionOrder)
             .HasForeignKey(c => c.ProductionOrderId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── ProductionOrderConsumption (v4 Production) ───────────────────────
        builder.Entity<ProductionOrderConsumption>(e =>
        {
            e.ToTable("production_order_consumptions");
            e.HasKey(c => c.Id);
            e.Property(c => c.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(c => c.QtyConsumed).HasColumnType("numeric(10,3)").IsRequired();
            e.Property(c => c.ConsumedAt).HasDefaultValueSql("NOW()");
            e.HasIndex(c => c.ProductionOrderId);
            // ProductionOrder FK is wired via ProductionOrder.HasMany above (Cascade)
            e.HasOne(c => c.Item).WithMany()
             .HasForeignKey(c => c.ItemId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(c => c.ProductStock).WithMany()
             .HasForeignKey(c => c.ProductStockId).OnDelete(DeleteBehavior.Restrict);
        });

        // ── Customer (CRM) ───────────────────────────────────────────────────
        builder.Entity<Customer>(e =>
        {
            e.ToTable("customers");
            e.HasKey(c => c.Id);
            e.Property(c => c.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(c => c.Name).HasColumnType("text").IsRequired();
            e.Property(c => c.Phone).HasColumnType("text");
            e.Property(c => c.Email).HasColumnType("text");
            e.Property(c => c.Notes).HasColumnType("text");
            e.Property(c => c.Tags).HasColumnType("text[]");
            e.Property(c => c.TotalOrders).HasDefaultValue(0);
            e.Property(c => c.TotalSpent).HasColumnType("decimal(18,2)").HasDefaultValue(0m);
            e.Property(c => c.CreatedAt).HasDefaultValueSql("NOW()");
            e.Property(c => c.UpdatedAt).HasDefaultValueSql("NOW()");
            // Tenant isolation (RLS)
            e.HasIndex(c => c.TenantId)
             .HasDatabaseName("idx_customers_tenant");
            // Phone lookup — partial: only rows with a phone number
            e.HasIndex(c => new { c.TenantId, c.Phone })
             .HasDatabaseName("idx_customers_phone")
             .HasFilter("\"Phone\" IS NOT NULL");
            // Email lookup — partial
            e.HasIndex(c => new { c.TenantId, c.Email })
             .HasDatabaseName("idx_customers_email")
             .HasFilter("\"Email\" IS NOT NULL");
            e.HasOne(c => c.Tenant).WithMany()
             .HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(c => c.Transactions).WithOne(t => t.Customer)
             .HasForeignKey(t => t.CustomerId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
        });

        // ── PosTransaction — customer FK index ──────────────────────────────
        builder.Entity<PosTransaction>(e =>
        {
            e.HasIndex(t => t.CustomerId)
             .HasDatabaseName("idx_pos_tx_customer")
             .HasFilter("\"CustomerId\" IS NOT NULL");
            e.HasIndex(t => t.LoyaltyMembershipId)
             .HasDatabaseName("idx_pos_tx_loyalty_membership")
             .HasFilter("\"LoyaltyMembershipId\" IS NOT NULL");
            e.HasOne(t => t.LoyaltyMembership).WithMany()
             .HasForeignKey(t => t.LoyaltyMembershipId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
            // Store-migration analytics (RFM dashboard, TASK-501): per-customer first/last
            // transaction lookup within a tenant+date window via
            // DISTINCT ON ("CustomerId") ... WHERE "TenantId" = ? AND "CreatedAt" BETWEEN ? AND ?
            // ORDER BY "CustomerId", "CreatedAt" [DESC]. Neither existing index above has
            // CustomerId as a usable index condition together with TenantId, so this pattern
            // would otherwise force a scan of the tenant's *entire* transaction history (no
            // index lets CreatedAt narrow the scan without an unconstrained column in between)
            // followed by an explicit in-memory sort. Partial predicate mirrors the two
            // conditions already precedented individually on this table (idx_pos_tx_customer's
            // CustomerId IS NOT NULL, idx_pos_transactions_excl_failed's Status filter) so both
            // are satisfied by the index itself, no heap fetch needed to evaluate them.
            // LocationId (StoreId) travels via INCLUDE so the from/to store is available without
            // a heap fetch either. Ascending CreatedAt serves the "first transaction" query via
            // a pure ordered Index Scan (no sort); the "last transaction" (DESC) query still
            // benefits — Postgres's Incremental Sort (PG13+) exploits the CustomerId ordering
            // already provided by the index, sorting only within each customer's small group
            // instead of the whole tenant-period result set.
            // Kept CustomerId as 2nd key (not leading) intentionally — unlike the analogous
            // TASK-479 product-covering index, dropping the plain idx_pos_tx_customer here would
            // regress the Customer→PosTransaction OnDelete(SetNull) FK action (Customer delete
            // issues an update filtered by CustomerId alone, no TenantId in scope) to a full
            // table scan, so both indexes stay.
            e.HasIndex(t => new { t.TenantId, t.CustomerId, t.CreatedAt })
             .HasDatabaseName("idx_pos_tx_customer_migration")
             .HasFilter("\"CustomerId\" IS NOT NULL AND \"Status\" <> 'fiscalization_failed'")
             .IncludeProperties(t => new { t.StoreId });
        });

        // ── WorkSchedule (Workforce) ─────────────────────────────────────────
        builder.Entity<WorkSchedule>(e =>
        {
            e.ToTable("work_schedules");
            e.HasKey(s => s.Id);
            e.Property(s => s.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(s => s.Name).HasColumnType("text").IsRequired();
            e.Property(s => s.Status).HasMaxLength(30).HasDefaultValue("draft");
            e.Property(s => s.CreatedAt).HasDefaultValueSql("NOW()");
            e.Property(s => s.UpdatedAt).HasDefaultValueSql("NOW()");
            // Unique active schedule per (tenant, location, week) — archived schedules are excluded via filter.
            // This prevents duplicate schedules for the same week and location.
            e.HasIndex(s => new { s.TenantId, s.LocationId, s.WeekStart })
             .IsUnique()
             .HasFilter("\"Status\" <> 'archived'")
             .HasDatabaseName("uq_work_schedules_tenant_location_week");
            e.HasOne(s => s.Location).WithMany()
             .HasForeignKey(s => s.LocationId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(s => s.CreatedByUser).WithMany()
             .HasForeignKey(s => s.CreatedBy).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
            e.HasMany(s => s.Shifts).WithOne(sh => sh.Schedule)
             .HasForeignKey(sh => sh.ScheduleId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── ScheduleShift (Workforce) ────────────────────────────────────────
        builder.Entity<ScheduleShift>(e =>
        {
            e.ToTable("schedule_shifts");
            e.HasKey(s => s.Id);
            e.Property(s => s.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(s => s.RoleOverride).HasColumnType("text");
            e.Property(s => s.Notes).HasColumnType("text");
            e.Property(s => s.Status).HasMaxLength(30).HasDefaultValue("scheduled");
            e.Property(s => s.BreakMinutes).HasDefaultValue(60);
            e.Property(s => s.CreatedAt).HasDefaultValueSql("NOW()");
            // Shifts within a schedule
            e.HasIndex(s => s.ScheduleId)
             .HasDatabaseName("idx_schedule_shifts_schedule");
            // Employee schedule lookup by date
            e.HasIndex(s => new { s.TenantId, s.UserId, s.ShiftDate })
             .HasDatabaseName("idx_schedule_shifts_user_date");
            // Daily roster per location
            e.HasIndex(s => new { s.TenantId, s.LocationId, s.ShiftDate })
             .HasDatabaseName("idx_schedule_shifts_date");
            // Schedule FK is wired via WorkSchedule.HasMany above (Cascade)
            e.HasOne(s => s.User).WithMany()
             .HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(s => s.Location).WithMany()
             .HasForeignKey(s => s.LocationId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── ProviderScheduleSlot (TASK-274) ──────────────────────────────────
        builder.Entity<ProviderScheduleSlot>(e =>
        {
            e.ToTable("provider_schedule_slots");
            e.HasKey(s => s.Id);
            e.Property(s => s.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(s => s.DayOfWeek).IsRequired();
            e.Property(s => s.StartTime).IsRequired();
            e.Property(s => s.EndTime).IsRequired();
            e.Property(s => s.IsActive).HasDefaultValue(true);
            e.Property(s => s.CreatedAt).HasDefaultValueSql("NOW()");
            e.HasOne(s => s.User).WithMany()
             .HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── ProviderRole (TASK-279) ───────────────────────────────────────────
        builder.Entity<ProviderRole>(e =>
        {
            e.ToTable("provider_roles");
            e.HasKey(r => r.Id);
            e.Property(r => r.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(r => r.DisplayName).HasMaxLength(200).IsRequired();
            e.Property(r => r.BaseRole).HasMaxLength(50).IsRequired();
            e.Property(r => r.Permissions).HasColumnType("text[]").IsRequired();
            e.Property(r => r.IsSystem).HasDefaultValue(false);
            e.Property(r => r.CreatedAt).HasDefaultValueSql("NOW()");
        });

        // ── LandingLead (TASK-333) ────────────────────────────────────────────
        // Provider-level table: no tenant_id, no RLS (same as provider_roles).
        builder.Entity<LandingLead>(e =>
        {
            e.ToTable("landing_leads");
            e.HasKey(l => l.Id);
            e.Property(l => l.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(l => l.Name).HasMaxLength(100).IsRequired();
            e.Property(l => l.Phone).HasMaxLength(30).IsRequired();
            e.Property(l => l.Company).HasMaxLength(150).IsRequired(false);
            e.Property(l => l.Message).HasMaxLength(1000).IsRequired(false);
            e.Property(l => l.Source).HasMaxLength(50).IsRequired().HasDefaultValue("landing");
            e.Property(l => l.IsProcessed).HasDefaultValue(false);
            e.Property(l => l.CreatedAt).HasDefaultValueSql("NOW()");
            e.HasIndex(l => l.CreatedAt);
        });

        // ── SupplierRole (TASK-305) ───────────────────────────────────────────
        builder.Entity<SupplierRole>(e =>
        {
            e.ToTable("supplier_roles");
            e.HasKey(r => r.Id);
            e.Property(r => r.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(r => r.TenantId).IsRequired();
            e.Property(r => r.DisplayName).HasMaxLength(200).IsRequired();
            e.Property(r => r.BaseRole).HasMaxLength(50).IsRequired();
            e.Property(r => r.Permissions).HasColumnType("text[]").IsRequired();
            e.Property(r => r.IsSystem).HasDefaultValue(false);
            e.Property(r => r.CreatedAt).HasDefaultValueSql("NOW()");
            e.HasIndex(r => r.TenantId);
        });

        // ── SupplierTask (TASK-305) ────────────────────────────────────────────
        builder.Entity<SupplierTask>(e =>
        {
            e.ToTable("supplier_tasks");
            e.HasKey(t => t.Id);
            e.Property(t => t.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(t => t.TenantId).IsRequired();
            e.Property(t => t.SupplierId).IsRequired();
            e.Property(t => t.Title).HasColumnType("text").IsRequired();
            e.Property(t => t.Description).HasColumnType("text");
            e.Property(t => t.Status).HasMaxLength(20).HasDefaultValue("pending");
            e.Property(t => t.CreatedAt).HasDefaultValueSql("NOW()");
            e.HasIndex(t => t.TenantId);
            e.HasIndex(t => t.SupplierId);
            e.HasIndex(t => t.AssignedToUserId);
            e.HasIndex(t => t.ClientTenantId);
            e.HasIndex(t => t.Status);
            e.HasOne(t => t.Supplier).WithMany()
             .HasForeignKey(t => t.SupplierId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(t => t.Tenant).WithMany()
             .HasForeignKey(t => t.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(t => t.ClientTenant).WithMany()
             .HasForeignKey(t => t.ClientTenantId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
            e.HasOne(t => t.AssignedToUser).WithMany()
             .HasForeignKey(t => t.AssignedToUserId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
            e.HasOne(t => t.CreatedByUser).WithMany()
             .HasForeignKey(t => t.CreatedByUserId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
        });

        // ── ChatSession (TASK-278) ────────────────────────────────────────────
        builder.Entity<ChatSession>(e =>
        {
            e.ToTable("chat_sessions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasMaxLength(20).HasDefaultValue("open");
            e.Property(x => x.Subject).HasMaxLength(500);
            e.Property(x => x.AssignedAgentName).HasMaxLength(200).IsRequired(false);
            e.Property(x => x.ClosedAt).IsRequired(false);
            e.Property(x => x.Rating).IsRequired(false);
            e.Property(x => x.RatingComment).HasMaxLength(1000).IsRequired(false);
            // Block 16 (pre-launch DB audit): ChatService.GetSessionsAsync (tenant chat inbox,
            // "WHERE TenantId == tenantId ORDER BY UpdatedAt DESC") had only the PK index to
            // work with — full-table scan across every tenant's chat sessions on every load of
            // this page. Live chat is an actively growing feature; fix now before it compounds.
            e.HasIndex(x => new { x.TenantId, x.UpdatedAt })
             .IsDescending(false, true)
             .HasDatabaseName("idx_chat_sessions_tenant_updated");
            e.HasMany(x => x.Messages)
             .WithOne(x => x.Session)
             .HasForeignKey(x => x.SessionId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── ChatMessage (TASK-278) ────────────────────────────────────────────
        builder.Entity<ChatMessage>(e =>
        {
            e.ToTable("chat_messages");
            e.HasKey(x => x.Id);
            e.Property(x => x.Body).HasMaxLength(4000);
            e.Property(x => x.SenderName).HasMaxLength(200);
            e.Property(x => x.IsSystem).HasDefaultValue(false);
            e.HasIndex(x => x.SessionId);
        });

        // ── SupplierChatSession (TASK-312) ────────────────────────────────────
        builder.Entity<SupplierChatSession>(e =>
        {
            e.ToTable("supplier_chat_sessions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.SupplierTenantId).IsRequired();
            e.Property(x => x.ClientTenantId).IsRequired();
            e.Property(x => x.CreatedByUserId).IsRequired();
            e.Property(x => x.CreatedAt).HasDefaultValueSql("NOW()");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("NOW()");
            e.HasIndex(x => x.SupplierTenantId);
            e.HasIndex(x => x.ClientTenantId);
            e.HasIndex(x => new { x.SupplierTenantId, x.ClientTenantId }).IsUnique();
            e.HasOne<Tenant>().WithMany()
             .HasForeignKey(x => x.SupplierTenantId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Tenant>().WithMany()
             .HasForeignKey(x => x.ClientTenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Messages)
             .WithOne(x => x.Session)
             .HasForeignKey(x => x.SessionId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── SupplierChatMessage (TASK-312) ─────────────────────────────────────
        builder.Entity<SupplierChatMessage>(e =>
        {
            e.ToTable("supplier_chat_messages");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.SessionId).IsRequired();
            e.Property(x => x.SenderUserId).IsRequired();
            e.Property(x => x.SenderTenantId).IsRequired();
            e.Property(x => x.SenderName).HasMaxLength(200).IsRequired();
            e.Property(x => x.Body).HasMaxLength(4000).IsRequired();
            e.Property(x => x.IsRead).HasDefaultValue(false);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("NOW()");
            e.HasIndex(x => x.SessionId);
            e.HasIndex(x => x.CreatedAt);
            // TASK-649: supports the supplier-metrics worker job's per-session "first client
            // message → first supplier reply" scan (response-time median).
            e.HasIndex(x => new { x.SessionId, x.SenderTenantId, x.CreatedAt });
        });

        // ── SupplierContractSettings (TASK-316) ───────────────────────────────
        builder.Entity<SupplierContractSettings>(e =>
        {
            e.ToTable("supplier_contract_settings");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.TenantId).IsRequired();
            e.Property(x => x.LegalName).HasMaxLength(500).IsRequired();
            e.Property(x => x.Edrpou).HasMaxLength(20).IsRequired(false);
            e.Property(x => x.Iban).HasMaxLength(40).IsRequired(false);
            e.Property(x => x.BankName).HasMaxLength(300).IsRequired(false);
            e.Property(x => x.LegalAddress).HasMaxLength(500).IsRequired(false);
            e.Property(x => x.DirectorName).HasMaxLength(300).IsRequired(false);
            e.Property(x => x.Phone).HasMaxLength(30).IsRequired(false);
            e.Property(x => x.Email).HasMaxLength(255).IsRequired(false);
            e.Property(x => x.ServiceName).HasMaxLength(500).IsRequired(false);
            e.Property(x => x.ServiceDescription).HasMaxLength(4000).IsRequired(false);
            e.Property(x => x.SignatureImageUrl).HasMaxLength(1000).IsRequired(false);
            e.Property(x => x.StampImageUrl).HasMaxLength(1000).IsRequired(false);
            e.Property(x => x.IsVatPayer).HasDefaultValue(false);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("NOW()");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("NOW()");
            e.HasIndex(x => x.TenantId).IsUnique();
            e.HasOne<Tenant>().WithMany()
             .HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── LegalEntity (TASK-321) ────────────────────────────────────────────
        builder.Entity<LegalEntity>(e =>
        {
            e.ToTable("legal_entities");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.TenantId).IsRequired();
            e.Property(x => x.LegalName).HasMaxLength(500).IsRequired();
            e.Property(x => x.Edrpou).HasMaxLength(20).IsRequired(false);
            e.Property(x => x.LegalAddress).HasMaxLength(500).IsRequired(false);
            e.Property(x => x.DirectorName).HasMaxLength(300).IsRequired(false);
            e.Property(x => x.Phone).HasMaxLength(30).IsRequired(false);
            e.Property(x => x.Email).HasMaxLength(255).IsRequired(false);
            e.Property(x => x.Iban).HasMaxLength(40).IsRequired(false);
            e.Property(x => x.BankName).HasMaxLength(300).IsRequired(false);
            e.Property(x => x.IsVatPayer).HasDefaultValue(false);
            e.Property(x => x.IsActive).HasDefaultValue(true);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("NOW()");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("NOW()");
            e.HasIndex(x => x.TenantId).HasDatabaseName("idx_legal_entities_tenant");
            e.HasOne(x => x.Tenant).WithMany()
             .HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        });

        // ── SupplierAgreement (TASK-316) ──────────────────────────────────────
        builder.Entity<SupplierAgreement>(e =>
        {
            e.ToTable("supplier_agreements");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.SupplierTenantId).IsRequired();
            e.Property(x => x.ClientTenantId).IsRequired();
            e.Property(x => x.Status).HasMaxLength(30).HasDefaultValue("pending").IsRequired();
            e.Property(x => x.RequestMessage).HasMaxLength(2000).IsRequired(false);
            e.Property(x => x.RejectionReason).HasMaxLength(2000).IsRequired(false);
            e.Property(x => x.ContractNumber).HasMaxLength(100).IsRequired(false);
            e.Property(x => x.ContractFilePath).HasMaxLength(1000).IsRequired(false);
            e.Property(x => x.VchasnoDocumentId).HasMaxLength(200).IsRequired(false);
            e.Property(x => x.SigningMethod).HasMaxLength(20).IsRequired(false);
            e.Property(x => x.SigningEmail).HasMaxLength(255).IsRequired(false);
            e.Property(x => x.RequestedAt).IsRequired();
            e.Property(x => x.CreatedAt).HasDefaultValueSql("NOW()");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("NOW()");
            e.HasIndex(x => x.SupplierTenantId);
            e.HasIndex(x => x.ClientTenantId);
            e.HasIndex(x => x.ClientLegalEntityId);
            // One live agreement per (supplier, client) pair — rejected/terminated
            // rows don't block a new request.
            e.HasIndex(x => new { x.SupplierTenantId, x.ClientTenantId })
             .IsUnique()
             .HasFilter("\"Status\" NOT IN ('rejected', 'terminated')");
            // Mirrors supplier_chat_sessions: supplier CASCADE, client RESTRICT
            // (avoids the multiple-cascade-path conflict on tenants).
            e.HasOne<Tenant>().WithMany()
             .HasForeignKey(x => x.SupplierTenantId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Tenant>().WithMany()
             .HasForeignKey(x => x.ClientTenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany()
             .HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
            e.Property(x => x.ClientLegalEntityId).IsRequired(false);
            e.HasOne<LegalEntity>().WithMany()
             .HasForeignKey(x => x.ClientLegalEntityId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
        });

        // ── MarketplaceOrder (TASK-316) ───────────────────────────────────────
        builder.Entity<MarketplaceOrder>(e =>
        {
            e.ToTable("marketplace_orders");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.OrderNumber).HasMaxLength(50).IsRequired();
            e.Property(x => x.AgreementId).IsRequired();
            e.Property(x => x.SupplierTenantId).IsRequired();
            e.Property(x => x.ClientTenantId).IsRequired();
            e.Property(x => x.Status).HasMaxLength(20).HasDefaultValue("new").IsRequired();
            e.Property(x => x.Comment).HasMaxLength(2000).IsRequired(false);
            e.Property(x => x.CancelReason).HasMaxLength(2000).IsRequired(false);
            e.Property(x => x.DelayReason).HasMaxLength(2000).IsRequired(false);
            e.Property(x => x.TotalAmount).HasColumnType("numeric(14,2)").HasDefaultValue(0m);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("NOW()");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("NOW()");
            // TASK-586, ADR-033 Decision 2: nullable at the DB — historical orders have no
            // backfill value. Application-layer validation enforces it for new orders.
            e.Property(x => x.DestinationStoreId).IsRequired(false);
            // Supplier-portal expansion: #4 — denormalized snapshot of the client user who placed
            // the order (avoids a cross-tenant users join under a supplier session).
            e.Property(x => x.CreatedByUserName).HasMaxLength(255).IsRequired(false);
            // D5 — mutable supplier-set expected delivery date (date only). Landed in Phase 1.
            e.Property(x => x.ExpectedDeliveryDate).IsRequired(false);
            // D4 (Phase 3) — supplier warehouse the order was picked from. One source location
            // per order; nullable for legacy / module-off shipments.
            e.Property(x => x.SourceWarehouseId).IsRequired(false);
            // TASK-649: destination region code, snapshotted at order creation (not a live
            // join through DestinationStoreId). varchar(20) — same sizing as Location.RegionCode.
            e.Property(x => x.DestinationRegionCode).HasMaxLength(20);
            e.HasIndex(x => x.SupplierTenantId);
            e.HasIndex(x => x.ClientTenantId);
            e.HasIndex(x => x.AgreementId);
            e.HasOne(x => x.Agreement).WithMany()
             .HasForeignKey(x => x.AgreementId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Tenant>().WithMany()
             .HasForeignKey(x => x.SupplierTenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Tenant>().WithMany()
             .HasForeignKey(x => x.ClientTenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany()
             .HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
            e.HasOne<Location>().WithMany()
             .HasForeignKey(x => x.DestinationStoreId).OnDelete(DeleteBehavior.Restrict).IsRequired(false);
            e.HasOne<Location>().WithMany()
             .HasForeignKey(x => x.SourceWarehouseId).OnDelete(DeleteBehavior.Restrict).IsRequired(false);
            e.HasMany(x => x.Items)
             .WithOne(x => x.Order)
             .HasForeignKey(x => x.OrderId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── MarketplaceOrderItem (TASK-316) ───────────────────────────────────
        builder.Entity<MarketplaceOrderItem>(e =>
        {
            e.ToTable("marketplace_order_items");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.OrderId).IsRequired();
            // Denormalised copies of the parent order's tenant pair — lets the
            // RLS policy filter without a join (explicit-columns approach).
            e.Property(x => x.SupplierTenantId).IsRequired();
            e.Property(x => x.ClientTenantId).IsRequired();
            e.Property(x => x.ItemName).HasMaxLength(500).IsRequired();
            e.Property(x => x.Unit).HasMaxLength(50).IsRequired(false);
            e.Property(x => x.Price).HasColumnType("numeric(12,2)");
            e.Property(x => x.Qty).HasColumnType("numeric(12,3)");
            e.Property(x => x.LineTotal).HasColumnType("numeric(14,2)");
            e.HasIndex(x => x.OrderId);
            e.HasOne<SupplierItem>().WithMany()
             .HasForeignKey(x => x.SupplierItemId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
            e.HasMany(x => x.Batches)
             .WithOne(x => x.OrderItem)
             .HasForeignKey(x => x.OrderItemId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── MarketplaceOrderItemBatch (Phase 3, plan D4) ──────────────────────
        // The supplier's per-line batch allocation at ship time. RLS is the MIRROR IMAGE of
        // ADR-033's receipt split: supplier writes (tenant_isolation on SupplierTenantId),
        // client only reads (client_read FOR SELECT on ClientTenantId) — see
        // 20260903*_AddMarketplaceOrderItemBatches. Both tenant ids are denormalized onto the
        // row so neither policy needs a join, same convention MarketplaceOrderItem established.
        builder.Entity<MarketplaceOrderItemBatch>(e =>
        {
            e.ToTable("marketplace_order_item_batches");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.OrderItemId).IsRequired();
            e.Property(x => x.OrderId).IsRequired();
            e.Property(x => x.SupplierTenantId).IsRequired();
            e.Property(x => x.ClientTenantId).IsRequired();
            e.Property(x => x.ExpiryDate).IsRequired();
            e.Property(x => x.BatchNumber).HasMaxLength(100);
            // numeric(12,3) — same precision as MarketplaceOrderItem.Qty and SupplierStock.Quantity,
            // so an allocation reconciles against both sides without rounding drift.
            e.Property(x => x.Qty).HasColumnType("numeric(12,3)").IsRequired();
            e.Property(x => x.CreatedAt).HasDefaultValueSql("NOW()");
            e.HasIndex(x => x.OrderId);
            e.HasIndex(x => x.OrderItemId);
            // SET NULL, not Restrict: a consumed batch row may be archived later, but the
            // shipped-history allocation must survive it.
            e.HasOne<SupplierStock>().WithMany()
             .HasForeignKey(x => x.SupplierStockId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
        });

        // ── MarketplaceOrderReceipt (TASK-586, ADR-033) ────────────────────────
        // Client-confirmed receipt of a MarketplaceOrder. Deliberately its own entity, not a
        // StockReceipt reuse (ADR-033 Decision 1) — StockReceipt is single-tenant RLS, this
        // needs client-write + supplier-read.
        builder.Entity<MarketplaceOrderReceipt>(e =>
        {
            e.ToTable("marketplace_order_receipts");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.MarketplaceOrderId).IsRequired();
            e.Property(x => x.ClientTenantId).IsRequired();
            e.Property(x => x.SupplierTenantId).IsRequired();
            e.Property(x => x.DestinationStoreId).IsRequired();
            e.Property(x => x.Status).HasMaxLength(20).HasDefaultValue("draft").IsRequired();
            e.Property(x => x.CreatedAt).HasDefaultValueSql("NOW()");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("NOW()");
            // v1 scope limit (ADR-033): one receiving session per order.
            e.HasIndex(x => x.MarketplaceOrderId).IsUnique();
            e.HasIndex(x => x.ClientTenantId);
            e.HasIndex(x => x.SupplierTenantId);
            e.HasOne(x => x.Order).WithMany()
             .HasForeignKey(x => x.MarketplaceOrderId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.DestinationStore).WithMany()
             .HasForeignKey(x => x.DestinationStoreId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany()
             .HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
            e.HasOne<User>().WithMany()
             .HasForeignKey(x => x.ReceivedByUserId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
            e.HasMany(x => x.Items)
             .WithOne(x => x.Receipt)
             .HasForeignKey(x => x.ReceiptId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── MarketplaceOrderReceiptItem (TASK-586, ADR-033) ────────────────────
        builder.Entity<MarketplaceOrderReceiptItem>(e =>
        {
            e.ToTable("marketplace_order_receipt_items");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.ReceiptId).IsRequired();
            e.Property(x => x.MarketplaceOrderItemId).IsRequired();
            // Denormalised copies of the parent receipt's tenant pair — lets the RLS policy
            // filter without a join, same convention MarketplaceOrderItem already established.
            e.Property(x => x.ClientTenantId).IsRequired();
            e.Property(x => x.SupplierTenantId).IsRequired();
            e.Property(x => x.ItemNameSnapshot).HasMaxLength(500).IsRequired();
            // numeric(12,3) — matches MarketplaceOrderItem.Qty's precision, not
            // StockReceiptItem's numeric(10,2) (ADR-033): must reconcile against the order
            // line without rounding drift.
            e.Property(x => x.QuantityOrdered).HasColumnType("numeric(12,3)").IsRequired();
            e.Property(x => x.QuantityReceived).HasColumnType("numeric(12,3)");
            e.Property(x => x.BatchNumber).HasMaxLength(100);
            e.HasIndex(x => x.ReceiptId);
            e.HasIndex(x => x.MarketplaceOrderItemId);
            e.HasOne(x => x.OrderItem).WithMany()
             .HasForeignKey(x => x.MarketplaceOrderItemId).OnDelete(DeleteBehavior.Restrict);
            // Resolved at barcode-scan time — nullable, unlike StockReceiptItem.ProductId
            // (required). SET NULL, not Cascade/Restrict: an Item being deleted later must not
            // block or cascade-delete the historical receipt record.
            e.HasOne(x => x.Product).WithMany()
             .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
            // Phase 3 (D4): which supplier-shipped batch this sub-row was prefilled from.
            // SET NULL — losing the source allocation must not delete receiving history.
            e.HasOne<MarketplaceOrderItemBatch>().WithMany()
             .HasForeignKey(x => x.SourceOrderItemBatchId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
        });

        // ── SupplierSupportTicket (TASK-316) ──────────────────────────────────
        builder.Entity<SupplierSupportTicket>(e =>
        {
            e.ToTable("supplier_support_tickets");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.SupplierTenantId).IsRequired();
            e.Property(x => x.ClientTenantId).IsRequired();
            e.Property(x => x.Subject).HasMaxLength(500).IsRequired();
            e.Property(x => x.Status).HasMaxLength(20).HasDefaultValue("open").IsRequired();
            e.Property(x => x.CreatedAt).HasDefaultValueSql("NOW()");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("NOW()");
            e.HasIndex(x => x.SupplierTenantId);
            e.HasIndex(x => x.ClientTenantId);
            e.HasIndex(x => x.MarketplaceOrderId);
            e.HasOne<Tenant>().WithMany()
             .HasForeignKey(x => x.SupplierTenantId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Tenant>().WithMany()
             .HasForeignKey(x => x.ClientTenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany()
             .HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
            // TASK-596: order this ticket was auto-opened for on a flagged receiving
            // discrepancy. Restrict (not SetNull like CreatedByUserId above) — matches
            // MarketplaceOrderReceipt.MarketplaceOrderId's own choice: orders are never
            // hard-deleted, only status-transitioned, so Restrict is safe and consistent.
            e.HasOne<MarketplaceOrder>().WithMany()
             .HasForeignKey(x => x.MarketplaceOrderId).OnDelete(DeleteBehavior.Restrict).IsRequired(false);
            e.HasMany(x => x.Messages)
             .WithOne(x => x.Ticket)
             .HasForeignKey(x => x.TicketId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── SupplierSupportTicketMessage (TASK-316) ───────────────────────────
        builder.Entity<SupplierSupportTicketMessage>(e =>
        {
            e.ToTable("supplier_support_ticket_messages");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.TicketId).IsRequired();
            e.Property(x => x.SenderTenantId).IsRequired();
            e.Property(x => x.SenderUserId).IsRequired();
            e.Property(x => x.Body).HasMaxLength(4000).IsRequired();
            e.Property(x => x.IsRead).HasDefaultValue(false);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("NOW()");
            e.HasIndex(x => x.TicketId);
            e.HasIndex(x => x.CreatedAt);
        });

        // ── UserPermissionGrant (ADR-019, TASK-341) ───────────────────────────
        builder.Entity<UserPermissionGrant>(e =>
        {
            e.ToTable("user_permission_grants");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.TenantId).IsRequired();
            e.Property(x => x.UserId).IsRequired();
            e.Property(x => x.PermissionKey).HasMaxLength(100).IsRequired();
            e.Property(x => x.ExpiresAt).IsRequired();
            e.Property(x => x.GrantedByUserId).IsRequired();
            e.Property(x => x.GrantedAt).HasDefaultValueSql("NOW()");
            e.HasIndex(x => new { x.TenantId, x.UserId })
             .HasDatabaseName("idx_user_permission_grants_tenant_user");
            // Partial index for the worker's expiry scan — only rows still eligible
            // to expire (not already revoked) need to be found by ExpiresAt.
            e.HasIndex(x => x.ExpiresAt)
             .HasDatabaseName("idx_user_permission_grants_expires_active")
             .HasFilter("\"RevokedAt\" IS NULL");
            e.HasOne(x => x.User).WithMany()
             .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.GrantedByUser).WithMany()
             .HasForeignKey(x => x.GrantedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.RevokedByUser).WithMany()
             .HasForeignKey(x => x.RevokedByUserId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
        });

        // ── TenantRole (ADR-020, TASK-345) ────────────────────────────────────
        // Same shape as SupplierRole: no explicit Tenant FK (TenantId is a plain
        // indexed/RLS-scoped column, not a hard DB constraint — mirrors SupplierRole,
        // which has none either, avoiding yet another cascade path onto tenants).
        builder.Entity<TenantRole>(e =>
        {
            e.ToTable("tenant_roles");
            e.HasKey(r => r.Id);
            e.Property(r => r.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(r => r.TenantId).IsRequired();
            e.Property(r => r.Name).HasMaxLength(200).IsRequired();
            // text[], not jsonb — matches ProviderRole.Permissions/SupplierRole.Permissions
            // exactly (both are List<string> stored as a native Postgres array via Npgsql's
            // built-in array support). No EnableDynamicJson/converter needed, unlike the
            // jsonb Dictionary<string,object?> pattern used elsewhere (e.g. SupplierItem.
            // Attributes) — that mechanism is for non-list JSON shapes, not simple string lists.
            e.Property(r => r.Capabilities).HasColumnType("text[]").IsRequired();
            // Same text[] treatment as Capabilities above (TASK-391) — deliberately NOT jsonb.
            // Both are plain List<string>; Npgsql's native array support round-trips them with
            // no HasConversion/EnableDynamicJson/ValueComparer plumbing, exactly like
            // ProviderRole.Permissions/SupplierRole.Permissions. A jsonb column would need that
            // extra machinery for no benefit here — AllowedTabs is a flat set of catalog keys,
            // never a nested/structured value. DEFAULT '{}' (not just a C#-side default) is
            // required so the additive migration can backfill this NOT NULL column on the
            // tenant_roles rows that already exist in every deployed environment.
            e.Property(r => r.AllowedTabs).HasColumnType("text[]").IsRequired().HasDefaultValueSql("'{}'");
            e.Property(r => r.IsActive).HasDefaultValue(true);
            e.Property(r => r.CreatedByUserId).IsRequired(false);
            e.Property(r => r.CreatedAt).HasDefaultValueSql("NOW()");
            e.Property(r => r.UpdatedAt).IsRequired(false);
            // Partial unique: only active templates block reusing a name — an archived
            // ("deactivated") template never prevents creating a fresh one with the same name.
            e.HasIndex(r => new { r.TenantId, r.Name })
             .IsUnique()
             .HasDatabaseName("uq_tenant_roles_tenant_name_active")
             .HasFilter("\"IsActive\"");
            e.HasOne<User>().WithMany()
             .HasForeignKey(r => r.CreatedByUserId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
        });

        // ── UserLocation (TASK-392 Stage 1) ───────────────────────────────────
        // Store-scoped access grants: every restricted rank gets exactly one row per
        // assigned location here (enterprise_admin bypasses via app.role — no rows).
        // TenantId is a direct column (not derived via an EXISTS to User/Location) because
        // Stage 3's RLS store_scope policies on other tables will EXISTS-subquery *into*
        // this table on every scoped request, so it needs its own leading index rather than
        // a join back through users. Enforcement itself is not wired here — see RLS below,
        // which is just the standard tenant_isolation/provider_bypass/worker_bypass triad,
        // not a store_scope policy.
        builder.Entity<UserLocation>(e =>
        {
            e.ToTable("user_locations");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.TenantId).IsRequired();
            e.Property(x => x.UserId).IsRequired();
            e.Property(x => x.LocationId).IsRequired();
            e.Property(x => x.AssignedByUserId).IsRequired(false);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("NOW()");
            // Prevents duplicate grants and doubles as the lookup RLS/service code will use
            // ("does user X have a row for location Y").
            e.HasIndex(x => new { x.TenantId, x.UserId, x.LocationId })
             .IsUnique()
             .HasDatabaseName("uq_user_locations_tenant_user_location");
            // Reverse lookup: which users/managers cover location X.
            e.HasIndex(x => new { x.TenantId, x.LocationId })
             .HasDatabaseName("idx_user_locations_tenant_location");
            e.HasOne<User>().WithMany()
             .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Location>().WithMany()
             .HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany()
             .HasForeignKey(x => x.AssignedByUserId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
        });

        // ── ConsumerAccount (Loyalty Фаза 0, TASK-404) ────────────────────────
        // Global identity, no TenantId column at all — deliberately excluded from RLS
        // (see AddLoyaltyProgram migration for the full rationale). Protected only by
        // application code, same precedent as Tenant.
        builder.HasSequence<long>("consumer_account_number_seq").StartsAt(1_000_000_000L).HasMax(9_999_999_999L);
        builder.HasSequence<long>("loyalty_card_number_seq").StartsAt(1_000_000_000L).HasMax(9_999_999_999L);

        builder.Entity<ConsumerAccount>(e =>
        {
            e.ToTable("consumer_accounts");
            e.HasKey(a => a.Id);
            e.Property(a => a.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(a => a.AccountNumber).HasDefaultValueSql("nextval('consumer_account_number_seq')").ValueGeneratedOnAdd();
            e.Property(a => a.Phone).HasColumnType("text").IsRequired();
            e.Property(a => a.PasswordHash).HasColumnType("text").IsRequired();
            e.Property(a => a.LoyaltyTotpSecret).HasColumnType("text").IsRequired();
            e.Property(a => a.FullName).HasColumnType("text").IsRequired();
            e.Property(a => a.Email).HasColumnType("text");
            e.Property(a => a.FailedLoginAttempts).HasDefaultValue(0);
            e.Property(a => a.IsActive).HasDefaultValue(true);
            e.Property(a => a.CreatedAt).HasDefaultValueSql("NOW()");
            // Globally unique — unlike Customer.Phone, which is only unique per-tenant.
            e.HasIndex(a => a.Phone)
             .IsUnique()
             .HasDatabaseName("uq_consumer_accounts_phone");
            e.HasIndex(a => a.AccountNumber).IsUnique().HasDatabaseName("uq_consumer_accounts_account_number");
            e.HasMany(a => a.Memberships).WithOne(m => m.ConsumerAccount)
             .HasForeignKey(m => m.ConsumerAccountId).OnDelete(DeleteBehavior.Restrict);
        });

        // ── LoyaltyMembership (Loyalty Фаза 0, TASK-404) ──────────────────────
        builder.Entity<LoyaltyMembership>(e =>
        {
            e.ToTable("loyalty_memberships");
            e.HasKey(m => m.Id);
            e.Property(m => m.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(m => m.CardNumber).HasDefaultValueSql("nextval('loyalty_card_number_seq')").ValueGeneratedOnAdd();
            e.Property(m => m.TenantId).IsRequired();
            e.Property(m => m.ConsumerAccountId).IsRequired();
            e.Property(m => m.TotpSecret).HasColumnType("text").IsRequired();
            e.Property(m => m.Balance).HasColumnType("decimal(18,2)").HasDefaultValue(0m);
            e.Property(m => m.Status).HasMaxLength(20).HasDefaultValue(LoyaltyMembershipStatus.Active);
            e.Property(m => m.JoinedAt).HasDefaultValueSql("NOW()");
            // TASK-613: tier ladder — set only by the nightly tier-recompute worker job.
            e.Property(m => m.CompositeScore).HasColumnType("decimal(18,4)").HasDefaultValue(0m);
            e.Property(m => m.TierEarnedBonuses).HasColumnType("decimal(18,2)");
            e.Property(m => m.TierCashSpend).HasColumnType("decimal(18,2)");
            e.Property(m => m.TierBonusSpend).HasColumnType("decimal(18,2)");
            // TASK-414 (security review TASK-412, finding B): same xmin optimistic-concurrency
            // pattern as ProductStock above (TASK-356) — no schema change needed, xmin already
            // exists on every row. Without this, two concurrent writers to the same
            // membership's Balance (e.g. two POS sales redeeming/accruing on the same
            // membership at once, or a POS sale racing LoyaltyService.ManualAdjustAsync) both
            // read the same pre-write Balance, both pass their own "sufficient balance" check,
            // and the loser's decrement is silently overwritten — a lost update that lets a
            // customer redeem more than they actually have. Now the loser's SaveChangesAsync
            // throws DbUpdateConcurrencyException instead; callers (PosService.CreateSaleAsync,
            // LoyaltyService.ManualAdjustAsync) turn that into a clean "retry" error.
            e.Property<uint>("xmin").IsRowVersion();
            // One membership per (tenant, consumer) — also the "does this consumer already
            // have a card here" lookup used at enrollment time.
            e.HasIndex(m => new { m.TenantId, m.ConsumerAccountId })
             .IsUnique()
             .HasDatabaseName("uq_loyalty_memberships_tenant_consumer");
            e.HasIndex(m => m.TenantId)
             .HasDatabaseName("idx_loyalty_memberships_tenant");
            e.HasIndex(m => new { m.TenantId, m.CardNumber }).IsUnique()
             .HasDatabaseName("uq_loyalty_memberships_tenant_card_number");
            e.HasOne(m => m.Tenant).WithMany()
             .HasForeignKey(m => m.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(m => m.Customer).WithMany()
             .HasForeignKey(m => m.CustomerId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
            e.HasOne(m => m.LinkedUser).WithMany()
             .HasForeignKey(m => m.LinkedUserId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
            // TASK-507: same nullable-reference convention as Customer/LinkedUser above (FK +
            // SetNull + IsRequired(false)) — no navigation property needed since LoyaltyService
            // resolves the Location itself (via ITenantSessionOverride, same as its other
            // cross-tenant consumer-session reads), not through EF Include.
            e.HasOne<Location>().WithMany()
             .HasForeignKey(m => m.PreferredStoreId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
            // TASK-613: SetNull, same convention as Customer/LinkedUser/PreferredStoreId above —
            // deleting a tier definition must not drag memberships down with it.
            e.HasOne(m => m.CurrentTier).WithMany()
             .HasForeignKey(m => m.CurrentTierId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
            e.HasMany(m => m.LedgerEntries).WithOne(l => l.Membership)
             .HasForeignKey(l => l.MembershipId).OnDelete(DeleteBehavior.Restrict);
        });

        // ── LoyaltyLedgerEntry (Loyalty Фаза 0, TASK-404) ─────────────────────
        // Append-only — MembershipId uses Restrict (not Cascade) so the audit trail can
        // never be silently destroyed by deleting its parent membership, same precedent
        // as StockMovement.ProductId -> catalog_products (log rows referencing a
        // long-lived master record). In practice LoyaltyMembership is never hard-deleted
        // anyway (Status active/blocked only), matching the project's "soft delete only"
        // rule.
        builder.Entity<LoyaltyLedgerEntry>(e =>
        {
            e.ToTable("loyalty_ledger_entries");
            e.HasKey(l => l.Id);
            e.Property(l => l.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(l => l.TenantId).IsRequired();
            e.Property(l => l.MembershipId).IsRequired();
            e.Property(l => l.EntryType).HasMaxLength(20).IsRequired();
            e.Property(l => l.Amount).HasColumnType("decimal(18,2)").IsRequired();
            e.Property(l => l.BalanceAfter).HasColumnType("decimal(18,2)").IsRequired();
            e.Property(l => l.Note).HasColumnType("text");
            e.Property(l => l.CreatedAt).HasDefaultValueSql("NOW()");
            // Paginated ledger history per membership (GET /api/consumer/loyalty/{tenantId}/history).
            e.HasIndex(l => new { l.TenantId, l.MembershipId, l.CreatedAt })
             .IsDescending(false, false, true)
             .HasDatabaseName("idx_loyalty_ledger_membership_created");
            // "Loyalty entries for this sale" lookup (SaleDetailDrawer).
            e.HasIndex(l => l.PosTransactionId)
             .HasDatabaseName("idx_loyalty_ledger_pos_transaction")
             .HasFilter("\"PosTransactionId\" IS NOT NULL");
            e.HasOne(l => l.PosTransaction).WithMany()
             .HasForeignKey(l => l.PosTransactionId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
            e.HasOne(l => l.CreatedByUser).WithMany()
             .HasForeignKey(l => l.CreatedByUserId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
        });

        builder.Entity<LoyaltyBonusLot>(e =>
        {
            e.ToTable("loyalty_bonus_lots");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.OriginalAmount).HasColumnType("decimal(18,2)");
            e.Property(x => x.RemainingAmount).HasColumnType("decimal(18,2)");
            e.HasIndex(x => new { x.TenantId, x.MembershipId, x.ExpiresAt });
            e.HasOne(x => x.Membership).WithMany().HasForeignKey(x => x.MembershipId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.SourceLedgerEntry).WithMany().HasForeignKey(x => x.SourceLedgerEntryId).OnDelete(DeleteBehavior.Restrict);
        });

        // ── LoyaltyProgramSettings (Loyalty Фаза 0, TASK-404) ─────────────────
        builder.Entity<LoyaltyProgramSettings>(e =>
        {
            e.ToTable("loyalty_program_settings");
            e.HasKey(s => s.Id);
            e.Property(s => s.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(s => s.TenantId).IsRequired();
            e.Property(s => s.IsEnabled).HasDefaultValue(true);
              e.Property(s => s.AccrualRatePercent).HasColumnType("decimal(5,2)").HasDefaultValue(3.0m);
              e.Property(s => s.BonusUnitsPerCurrencyUnit).HasDefaultValue(1);
            e.Property(s => s.AnnualBonusResetMonth).HasDefaultValue(1);
            e.Property(s => s.AnnualBonusResetDay).HasDefaultValue(1);
            e.Property(s => s.AnnualBonusResetHour).HasDefaultValue(0);
            e.Property(s => s.BonusResetTimeZone).HasMaxLength(100).HasDefaultValue("Europe/Kyiv");
            e.Property(s => s.ExclusionsApplyToAccrual).HasDefaultValue(true);
            e.Property(s => s.ExclusionsApplyToRedemption).HasDefaultValue(true);
            e.Property(s => s.ExcludedCategoryIdsJson).HasColumnType("jsonb").HasDefaultValue("[]");
            e.Property(s => s.ExcludedProductIdsJson).HasColumnType("jsonb").HasDefaultValue("[]");
            e.Property(s => s.WelcomeRewardAmount).HasColumnType("decimal(18,2)").HasDefaultValue(0m);
            e.Property(s => s.FirstPurchaseRewardAmount).HasColumnType("decimal(18,2)").HasDefaultValue(0m);
            e.Property(s => s.ProfileCompletionRewardAmount).HasColumnType("decimal(18,2)").HasDefaultValue(0m);
            e.Property(s => s.ReviewRewardAmount).HasColumnType("decimal(18,2)").HasDefaultValue(0m);
            e.Property(s => s.BonusLifetimeDays).HasDefaultValue(365);
            e.Property(s => s.RedemptionCapPercent).HasColumnType("decimal(5,2)").HasDefaultValue(50.0m);
            e.Property(s => s.MinRedemptionBalance).HasColumnType("decimal(18,2)").HasDefaultValue(0m);
            e.Property(s => s.CodeTtlSeconds).HasDefaultValue(30);
            e.Property(s => s.CustomerCodeFormat).HasColumnType("varchar(20)").HasDefaultValue("barcode");
            e.Property(s => s.UpdatedAt).HasDefaultValueSql("NOW()");
            e.HasIndex(s => s.TenantId)
             .IsUnique()
             .HasDatabaseName("uq_loyalty_program_settings_tenant");
            e.HasOne(s => s.Tenant).WithMany()
             .HasForeignKey(s => s.TenantId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<MobileCatalogSettings>(e =>
        {
            e.ToTable("mobile_catalog_settings"); e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(160).IsRequired();
            e.Property(x => x.Description).HasMaxLength(1200).IsRequired();
            e.Property(x => x.BannerUrl).HasMaxLength(1000);
            e.Property(x => x.LayoutMode).HasMaxLength(20).IsRequired();
            e.Property(x => x.Status).HasMaxLength(20).IsRequired();
            e.Property(x => x.CreatedAt).HasDefaultValueSql("NOW()");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("NOW()");
            e.HasIndex(x => new { x.TenantId, x.Status, x.PublishAt }).HasDatabaseName("idx_mobile_catalog_settings_tenant_status_publish");
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<MobileCatalogItem>(e =>
        {
            e.ToTable("mobile_catalog_items"); e.HasKey(x => x.Id);
            e.Property(x => x.MobileDiscountPercent).HasPrecision(5, 2);
            e.Property(x => x.ProductNameSnapshot).HasMaxLength(300).IsRequired();
            e.Property(x => x.UnitSnapshot).HasMaxLength(30).IsRequired();
            e.Property(x => x.ImageUrlSnapshot).HasMaxLength(1000);
            e.Property(x => x.RegularPriceSnapshot).HasPrecision(18, 2);
            e.Property(x => x.MobilePriceSnapshot).HasPrecision(18, 2);
            e.HasIndex(x => new { x.SettingsId, x.ProductId }).IsUnique().HasDatabaseName("uq_mobile_catalog_items_settings_product");
            e.HasIndex(x => new { x.TenantId, x.SortOrder }).HasDatabaseName("idx_mobile_catalog_items_tenant_order");
            e.HasOne(x => x.Settings).WithMany(x => x.Items).HasForeignKey(x => x.SettingsId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<MobileCatalogLocation>(e =>
        {
            e.ToTable("mobile_catalog_locations"); e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.SettingsId, x.LocationId }).IsUnique().HasDatabaseName("uq_mobile_catalog_locations_settings_location");
            e.HasIndex(x => new { x.TenantId, x.LocationId }).HasDatabaseName("idx_mobile_catalog_locations_tenant_location");
            e.HasOne(x => x.Settings).WithMany(x => x.Locations).HasForeignKey(x => x.SettingsId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<MobileCatalogEvent>(e =>
        {
            e.ToTable("mobile_catalog_events"); e.HasKey(x => x.Id);
            e.Property(x => x.EventType).HasMaxLength(30).IsRequired();
            e.Property(x => x.SessionId).HasMaxLength(100).IsRequired();
            e.Property(x => x.OccurredAt).HasDefaultValueSql("NOW()");
            e.HasIndex(x => new { x.TenantId, x.CatalogId, x.EventType, x.OccurredAt }).HasDatabaseName("idx_mobile_catalog_events_analytics");
            e.HasIndex(x => new { x.TenantId, x.ConsumerAccountId, x.ProductId, x.OccurredAt }).HasDatabaseName("idx_mobile_catalog_events_attribution");
            e.HasOne<MobileCatalogSettings>().WithMany().HasForeignKey(x => x.CatalogId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Item>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<Location>().WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ConsumerAccount>().WithMany().HasForeignKey(x => x.ConsumerAccountId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<PromotionCampaign>(e =>
        {
            e.ToTable("promotion_campaigns");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Eyebrow).HasMaxLength(100);
            e.Property(x => x.Description).HasMaxLength(500).IsRequired();
            e.Property(x => x.Body).HasColumnType("text");
            e.Property(x => x.Terms).HasColumnType("text");
            e.Property(x => x.ImageUrl).HasMaxLength(500);
            e.Property(x => x.BackgroundColor).HasMaxLength(20);
            e.Property(x => x.AccentColor).HasMaxLength(20);
            e.Property(x => x.AudienceType).HasMaxLength(30);
            e.Property(x => x.AudienceTierIdsJson).HasColumnType("jsonb").HasDefaultValue("[]");
            e.Property(x => x.Status).HasMaxLength(20);
            e.HasIndex(x => new { x.TenantId, x.Status, x.StartsAt });
        });

        builder.Entity<PromotionCampaignLocation>(e =>
        {
            e.ToTable("promotion_campaign_locations");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.HasIndex(x => new { x.CampaignId, x.LocationId }).IsUnique();
            e.HasOne(x => x.Campaign).WithMany(x => x.Locations).HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<PromotionCampaignProduct>(e =>
        {
            e.ToTable("promotion_campaign_products");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.DiscountPercent).HasColumnType("decimal(5,2)");
            e.HasIndex(x => new { x.CampaignId, x.ProductId }).IsUnique();
            e.HasOne(x => x.Campaign).WithMany(x => x.Products).HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<PromotionCampaignEvent>(e =>
        {
            e.ToTable("promotion_campaign_events");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.EventType).HasMaxLength(20).IsRequired();
            e.Property(x => x.OccurredAt).HasDefaultValueSql("NOW()");
            e.HasIndex(x => new { x.TenantId, x.CampaignId, x.EventType, x.OccurredAt })
                .HasDatabaseName("idx_promotion_campaign_events_analytics");
            e.HasIndex(x => new { x.TenantId, x.StoreId, x.OccurredAt })
                .HasDatabaseName("idx_promotion_campaign_events_store");
            e.HasOne<PromotionCampaign>().WithMany().HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Location>().WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ConsumerAccount>().WithMany().HasForeignKey(x => x.ConsumerAccountId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<Discount>()
            .HasOne<PromotionCampaign>()
            .WithMany()
            .HasForeignKey(x => x.PromotionCampaignId)
            .OnDelete(DeleteBehavior.SetNull);

        // ── ConsumerAccountProfileChange (TASK-613) ───────────────────────────
        // NO RLS at all, same precedent as ConsumerAccount itself — see class remarks.
        builder.Entity<ConsumerAccountProfileChange>(e =>
        {
            e.ToTable("consumer_account_profile_changes");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.ConsumerAccountId).IsRequired();
            e.Property(x => x.FieldName).HasMaxLength(20).IsRequired();
            e.Property(x => x.OldValue).HasColumnType("text");
            e.Property(x => x.NewValue).HasColumnType("text");
            e.Property(x => x.ChangedAt).HasDefaultValueSql("NOW()");
            e.HasIndex(x => x.ConsumerAccountId)
             .HasDatabaseName("idx_consumer_account_profile_changes_account");
            e.HasOne(x => x.ConsumerAccount).WithMany()
             .HasForeignKey(x => x.ConsumerAccountId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── LoyaltyTierDefinition (Loyalty tier ladder, TASK-613) ─────────────
        // Tenant-scoped, canonical RLS triad only (staff config, no consumer_self_access on
        // this table itself — same posture as LoyaltyProgramSettings above).
        builder.Entity<LoyaltyTierDefinition>(e =>
        {
            e.ToTable("loyalty_tier_definitions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.TenantId).IsRequired();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.ImageUrl).HasMaxLength(500);
            e.Property(x => x.MinCompositeScore).HasColumnType("decimal(18,4)");
            e.Property(x => x.AccrualMultiplier).HasColumnType("decimal(5,2)").HasDefaultValue(1.0m);
            e.Property(x => x.DiscountPercent).HasColumnType("decimal(5,2)").HasDefaultValue(0m);
            e.Property(x => x.MinEarnedBonuses).HasColumnType("decimal(18,2)");
            e.Property(x => x.MinCashSpend).HasColumnType("decimal(18,2)");
            e.Property(x => x.MinBonusSpend).HasColumnType("decimal(18,2)");
            e.Property(x => x.CreatedAt).HasDefaultValueSql("NOW()");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("NOW()");
            e.HasIndex(x => new { x.TenantId, x.SortOrder })
             .IsUnique()
             .HasDatabaseName("uq_loyalty_tier_definitions_tenant_sort_order");
            e.HasOne(x => x.Tenant).WithMany()
             .HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        });

        // ── LoyaltyTierChangeHistory (Loyalty tier ladder, TASK-613) ──────────
        // Append-only — MembershipId uses Restrict, same precedent as LoyaltyLedgerEntry
        // above (audit trail must never be silently destroyed by deleting its parent).
        // FromTierId/ToTierId use SetNull so deleting a tier definition never breaks old
        // history rows.
        builder.Entity<LoyaltyTierChangeHistory>(e =>
        {
            e.ToTable("loyalty_tier_change_history");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.TenantId).IsRequired();
            e.Property(x => x.MembershipId).IsRequired();
            e.Property(x => x.FromScore).HasColumnType("decimal(18,4)");
            e.Property(x => x.ToScore).HasColumnType("decimal(18,4)");
            e.Property(x => x.ChangedAt).HasDefaultValueSql("NOW()");
            e.HasIndex(x => new { x.TenantId, x.MembershipId, x.ChangedAt })
             .IsDescending(false, false, true)
             .HasDatabaseName("idx_loyalty_tier_change_history_membership_changed");
            e.HasOne(x => x.Membership).WithMany()
             .HasForeignKey(x => x.MembershipId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.FromTier).WithMany()
             .HasForeignKey(x => x.FromTierId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
            e.HasOne(x => x.ToTier).WithMany()
             .HasForeignKey(x => x.ToTierId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
        });

        // ── ConsumerSupportTicket (Consumer support channel, TASK-613) ────────
        // Mirrors SupplierSupportTicket's shape (see that entity's remarks) but for
        // consumer↔tenant instead of tenant↔supplier.
        builder.Entity<ConsumerSupportTicket>(e =>
        {
            e.ToTable("consumer_support_tickets");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.TenantId).IsRequired();
            e.Property(x => x.ConsumerAccountId).IsRequired();
            e.Property(x => x.Subject).HasMaxLength(500).IsRequired();
            e.Property(x => x.Status).HasMaxLength(20).HasDefaultValue(ConsumerSupportTicketStatus.Open).IsRequired();
            e.Property(x => x.CreatedAt).HasDefaultValueSql("NOW()");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("NOW()");
            e.HasIndex(x => x.TenantId);
            e.HasIndex(x => x.ConsumerAccountId);
            e.HasOne<Tenant>().WithMany()
             .HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ConsumerAccount>().WithMany()
             .HasForeignKey(x => x.ConsumerAccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Customer>().WithMany()
             .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
            e.HasMany(x => x.Messages)
             .WithOne(x => x.Ticket)
             .HasForeignKey(x => x.TicketId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── ConsumerSupportTicketMessage (Consumer support channel, TASK-613) ─
        builder.Entity<ConsumerSupportTicketMessage>(e =>
        {
            e.ToTable("consumer_support_ticket_messages");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.TicketId).IsRequired();
            e.Property(x => x.Body).HasMaxLength(4000).IsRequired();
            e.Property(x => x.IsRead).HasDefaultValue(false);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("NOW()");
            e.HasIndex(x => x.TicketId);
            e.HasIndex(x => x.CreatedAt);
            e.HasOne<ConsumerAccount>().WithMany()
             .HasForeignKey(x => x.SenderConsumerAccountId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
            e.HasOne<User>().WithMany()
             .HasForeignKey(x => x.SenderUserId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
        });

        // ── PurchaseReview (Purchase reviews, TASK-613) ───────────────────────
        // Mirrors SupplierReview's shape (see that entity's remarks) but keyed to a
        // PosTransaction instead of a Supplier. Restrict on PosTransactionId — a sale is
        // never cascade-deleted by a review.
        builder.Entity<PurchaseReview>(e =>
        {
            e.ToTable("purchase_reviews");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.TenantId).IsRequired();
            e.Property(x => x.ConsumerAccountId).IsRequired();
            e.Property(x => x.PosTransactionId).IsRequired();
            e.Property(x => x.Rating).IsRequired();
            e.Property(x => x.Comment).HasColumnType("text");
            e.Property(x => x.CreatedAt).HasDefaultValueSql("NOW()");
            e.Property(x => x.ReplyText).HasColumnType("text");
            e.HasIndex(x => x.TenantId);
            e.HasIndex(x => x.ConsumerAccountId);
            // One review per purchase — confirmed product decision (plan §1d).
            e.HasIndex(x => x.PosTransactionId)
             .IsUnique()
             .HasDatabaseName("uq_purchase_reviews_pos_transaction");
            e.HasOne(x => x.Tenant).WithMany()
             .HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ConsumerAccount).WithMany()
             .HasForeignKey(x => x.ConsumerAccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.PosTransaction).WithMany()
             .HasForeignKey(x => x.PosTransactionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.RepliedByUser).WithMany()
             .HasForeignKey(x => x.RepliedByUserId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
        });

        // ── PriceSegmentSettings (Marketing Analytics Фаза 2, TASK-419) ───────
        // Staff-only tenant configuration, same shape as LoyaltyProgramSettings above —
        // canonical RLS triad only, no consumer_self_access (no consumer access path to
        // this table at all, unlike loyalty_memberships/loyalty_ledger_entries).
        builder.Entity<PriceSegmentSettings>(e =>
        {
            e.ToTable("price_segment_settings");
            e.HasKey(s => s.Id);
            e.Property(s => s.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(s => s.TenantId).IsRequired();
            e.Property(s => s.DefaultFrequencyDeclineThresholdPercent).HasColumnType("decimal(5,2)").HasDefaultValue(30.0m);
            e.Property(s => s.UpdatedAt).HasDefaultValueSql("NOW()");
            e.HasIndex(s => s.TenantId)
             .IsUnique()
             .HasDatabaseName("uq_price_segment_settings_tenant");
            e.HasOne(s => s.Tenant).WithMany()
             .HasForeignKey(s => s.TenantId).OnDelete(DeleteBehavior.Restrict);
        });

        // ── PostCampaignSegment (Marketing Analytics Фаза 4, TASK-471) ────────
        // Top-level tenant-scoped table — real Tenant FK, same shape as LoyaltyMembership/
        // PriceSegmentSettings above. Canonical RLS triad only, no consumer_self_access
        // (staff-only, no consumer access path — see class remarks).
        builder.Entity<PostCampaignSegment>(e =>
        {
            e.ToTable("post_campaign_segments");
            e.HasKey(s => s.Id);
            e.Property(s => s.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(s => s.TenantId).IsRequired();
            e.Property(s => s.CreatedByUserId).IsRequired();
            e.Property(s => s.Name).HasColumnType("text");
            e.Property(s => s.UploadedCount).HasDefaultValue(0);
            e.Property(s => s.MatchedCount).HasDefaultValue(0);
            e.Property(s => s.DuplicateCount).HasDefaultValue(0);
            e.Property(s => s.UnknownCount).HasDefaultValue(0);
            e.Property(s => s.InvalidCount).HasDefaultValue(0);
            // List<string> as jsonb — same pattern as Item.Barcodes (KI-013: EnableDynamicJson
            // in DependencyInjection.cs already covers List<string>/JSONB round-tripping).
            e.Property(s => s.UnknownTokensSample)
             .HasColumnType("jsonb")
             .HasDefaultValueSql("'[]'::jsonb");
            e.Property(s => s.InvalidTokensSample)
             .HasColumnType("jsonb")
             .HasDefaultValueSql("'[]'::jsonb");
            e.Property(s => s.SegmentHash).HasColumnType("text").IsRequired();
            e.Property(s => s.CreatedAt).HasDefaultValueSql("NOW()");
            // "My segments" listing.
            e.HasIndex(s => new { s.TenantId, s.CreatedByUserId })
             .HasDatabaseName("idx_post_campaign_segments_tenant_creator");
            e.HasOne(s => s.Tenant).WithMany()
             .HasForeignKey(s => s.TenantId).OnDelete(DeleteBehavior.Restrict);
            // Restrict — mirrors UserPermissionGrant.GrantedByUserId (other staff-authored,
            // non-nullable User reference in this codebase).
            e.HasOne(s => s.CreatedByUser).WithMany()
             .HasForeignKey(s => s.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(s => s.Members).WithOne(m => m.Segment)
             .HasForeignKey(m => m.SegmentId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── PostCampaignSegmentMember (Marketing Analytics Фаза 4, TASK-471) ──
        // TenantId here is a plain, denormalized, indexed column (RLS + "filter by segment
        // within tenant" query support) with NO separate FK to tenants — same treatment as
        // LoyaltyLedgerEntry.TenantId, which likewise defers to its real parent FK
        // (SegmentId -> post_campaign_segments here, MembershipId there) rather than adding a
        // redundant direct tenants FK on a child row.
        builder.Entity<PostCampaignSegmentMember>(e =>
        {
            e.ToTable("post_campaign_segment_members");
            e.HasKey(m => m.Id);
            e.Property(m => m.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(m => m.TenantId).IsRequired();
            e.Property(m => m.SegmentId).IsRequired();
            e.Property(m => m.CustomerId).IsRequired();
            // Every report query filters by segment within tenant.
            e.HasIndex(m => new { m.TenantId, m.SegmentId })
             .HasDatabaseName("idx_post_campaign_segment_members_tenant_segment");
            // A customer appears at most once per segment — import step dedups before insert,
            // this is the hard backstop.
            e.HasIndex(m => new { m.SegmentId, m.CustomerId })
             .IsUnique()
             .HasDatabaseName("uq_post_campaign_segment_members_segment_customer");
            // SegmentId FK is wired via PostCampaignSegment.HasMany above (Cascade) — members
            // die with their segment.
            e.HasOne(m => m.Customer).WithMany()
             .HasForeignKey(m => m.CustomerId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── Banner (Consumer App plan, TASK-520) ──────────────────────────────
        // Top-level tenant-scoped table — real Tenant FK (Restrict, same as Discount), optional
        // Creator FK (SetNull, same as Discount.Creator). IsActive is a manual pause switch, not
        // a workflow status — see class remarks for why "currently showing" is computed
        // (IsCurrentlyActive) rather than stored.
        builder.Entity<Banner>(e =>
        {
            e.ToTable("banners");
            e.HasKey(b => b.Id);
            e.Property(b => b.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(b => b.TenantId).IsRequired();
            e.Property(b => b.Title).HasMaxLength(255).IsRequired();
            e.Property(b => b.Eyebrow).HasMaxLength(255);
            e.Property(b => b.Description).HasColumnType("text").IsRequired();
            e.Property(b => b.Body).HasColumnType("text").IsRequired();
            e.Property(b => b.Terms).HasColumnType("text").IsRequired();
            e.Property(b => b.ImageUrl).HasColumnType("text");
            e.Property(b => b.Icon).HasMaxLength(100).IsRequired();
            e.Property(b => b.BackgroundColor).HasMaxLength(20).IsRequired();
            e.Property(b => b.AccentColor).HasMaxLength(20).IsRequired();
            e.Property(b => b.DetailMode).HasMaxLength(20).HasDefaultValue(BannerDetailMode.Internal);
            e.Property(b => b.ExternalUrl).HasColumnType("text");
            e.Property(b => b.ValidFrom).HasDefaultValueSql("NOW()");
            e.Property(b => b.IsActive).HasDefaultValue(true);
            e.Property(b => b.SortOrder).HasDefaultValue(0);
            e.Property(b => b.CreatedAt).HasDefaultValueSql("NOW()");
            // Null = draft, never published (TASK-523). Non-null = first-publish timestamp,
            // set only via Banner.Publish(), never via the general Update() edit path.
            e.Property(b => b.PublishedAt);
            // Consumer feed query: active banners for a tenant, ordered for display.
            e.HasIndex(b => new { b.TenantId, b.IsActive, b.SortOrder })
             .HasDatabaseName("idx_banners_tenant_active_sort");
            e.HasOne(b => b.Tenant).WithMany()
             .HasForeignKey(b => b.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(b => b.Creator).WithMany()
             .HasForeignKey(b => b.CreatedBy).OnDelete(DeleteBehavior.SetNull);
        });

        // ── BannerLocation (TASK-520) ──────────────────────────────────────────
        // Many-to-many join, same config shape as UserLocation above — no navigation
        // properties on either side, raw Guid FKs only.
        builder.Entity<BannerLocation>(e =>
        {
            e.ToTable("banner_locations");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.TenantId).IsRequired();
            e.Property(x => x.BannerId).IsRequired();
            e.Property(x => x.LocationId).IsRequired();
            // Prevents duplicate grants for the same banner+location pair.
            e.HasIndex(x => new { x.BannerId, x.LocationId })
             .IsUnique()
             .HasDatabaseName("uq_banner_locations_banner_location");
            // Reverse lookup: which banners target location X.
            e.HasIndex(x => new { x.TenantId, x.LocationId })
             .HasDatabaseName("idx_banner_locations_tenant_location");
            e.HasOne<Banner>().WithMany()
             .HasForeignKey(x => x.BannerId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Location>().WithMany()
             .HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── BannerProduct (TASK-520) ───────────────────────────────────────────
        builder.Entity<BannerProduct>(e =>
        {
            e.ToTable("banner_products");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.TenantId).IsRequired();
            e.Property(x => x.BannerId).IsRequired();
            e.Property(x => x.ItemId).IsRequired();
            e.Property(x => x.SortOrder).HasDefaultValue(0);
            // Prevents attaching the same product twice to one banner.
            e.HasIndex(x => new { x.BannerId, x.ItemId })
             .IsUnique()
             .HasDatabaseName("uq_banner_products_banner_item");
            e.HasIndex(x => new { x.TenantId, x.BannerId })
             .HasDatabaseName("idx_banner_products_tenant_banner");
            e.HasOne<Banner>().WithMany()
             .HasForeignKey(x => x.BannerId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Item>().WithMany()
             .HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── BannerEvent (TASK-520) ─────────────────────────────────────────────
        // Append-only view/click log. ConsumerAccountId is nullable/SetNull — anonymous
        // consumer sessions are allowed and must not block the FK.
        builder.Entity<BannerEvent>(e =>
        {
            e.ToTable("banner_events");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.TenantId).IsRequired();
            e.Property(x => x.BannerId).IsRequired();
            e.Property(x => x.StoreId).IsRequired(false);
            e.Property(x => x.EventType).HasMaxLength(20).IsRequired();
            e.Property(x => x.OccurredAt).HasDefaultValueSql("NOW()");
            // Analytics read: COUNT(...) GROUP BY EventType for one banner within a tenant.
            e.HasIndex(x => new { x.TenantId, x.BannerId, x.EventType })
             .HasDatabaseName("idx_banner_events_tenant_banner_type");
            e.HasIndex(x => new { x.TenantId, x.BannerId, x.StoreId, x.OccurredAt })
             .HasDatabaseName("idx_banner_events_analytics_store_date");
            e.HasOne<Banner>().WithMany()
             .HasForeignKey(x => x.BannerId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ConsumerAccount>().WithMany()
             .HasForeignKey(x => x.ConsumerAccountId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
            e.HasOne<Location>().WithMany()
             .HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
        });


        // ── MobileConfiguration domain (multi-tenant consumer app-builder, TASK-531) ──────
        // Root + versions with a pointer: MobileConfiguration.PublishedVersionId/DraftVersionId
        // point at MobileConfigurationVersion (Restrict — a version can't be deleted out from
        // under an active pointer), while MobileConfigurationVersion.MobileConfigurationId points
        // back (Cascade — deleting the root config deletes all of its versions). See
        // MobileConfiguration's class remarks for why this single-cascade-path shape is safe
        // under PostgreSQL despite the circular FK.
        builder.Entity<MobileConfiguration>(e =>
        {
            e.ToTable("mobile_configurations");
            e.HasKey(m => m.Id);
            e.Property(m => m.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(m => m.TenantId).IsRequired();
            e.Property(m => m.CreatedAt).HasDefaultValueSql("NOW()");
            e.Property(m => m.UpdatedAt).HasDefaultValueSql("NOW()");
            // TASK-544: same xmin optimistic-concurrency pattern as ProductStock (TASK-356) and
            // LoyaltyMembership (TASK-414) — no schema change needed, xmin already exists on every
            // row. This is the "pointer" row a publish repoints (PublishedVersionId/DraftVersionId)
            // — without a token, two concurrent PublishAsync calls for the same tenant could race a
            // last-write-wins UPDATE here, silently losing one publish's pointer update. Now the
            // loser's SaveChangesAsync throws DbUpdateConcurrencyException instead;
            // MobileConfigurationRepository translates that into ConcurrencyConflictException, and
            // MobileConfigPublishService.PublishAsync turns it into a clean "retry" error.
            e.Property<uint>("xmin").IsRowVersion();
            // One row per tenant.
            e.HasIndex(m => m.TenantId).IsUnique().HasDatabaseName("uq_mobile_configurations_tenant");
            e.HasOne(m => m.Tenant).WithMany()
             .HasForeignKey(m => m.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(m => m.PublishedVersion).WithMany()
             .HasForeignKey(m => m.PublishedVersionId).OnDelete(DeleteBehavior.Restrict).IsRequired(false);
            e.HasOne(m => m.DraftVersion).WithMany()
             .HasForeignKey(m => m.DraftVersionId).OnDelete(DeleteBehavior.Restrict).IsRequired(false);
        });

        // ── MobileConfigurationVersion (TASK-531) ──────────────────────────────────────────
        // TenantId is denormalized from the parent MobileConfiguration (not derived via join) so
        // RLS can scope this table directly — same pattern as LoyaltyLedgerEntry.TenantId.
        builder.Entity<MobileConfigurationVersion>(e =>
        {
            e.ToTable("mobile_configuration_versions");
            e.HasKey(v => v.Id);
            e.Property(v => v.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(v => v.MobileConfigurationId).IsRequired();
            e.Property(v => v.TenantId).IsRequired();
            e.Property(v => v.Version).IsRequired();
            e.Property(v => v.SchemaVersion).IsRequired();
            e.Property(v => v.Status).HasMaxLength(20).HasDefaultValue(MobileConfigurationVersionStatus.Draft);
            e.Property(v => v.ConfigurationJson).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
            e.Property(v => v.CreatedAt).HasDefaultValueSql("NOW()");
            // TASK-544: same xmin token as MobileConfiguration above — this is the row Publish
            // mutates in place (ConfigurationJson gets the composed theme, Status flips to
            // Published) before repointing MobileConfiguration's pointers. Protects against a
            // stale concurrent writer (e.g. a second racing publish that read this row before the
            // first one committed) silently overwriting already-published, supposedly-immutable
            // content with a last-write-wins UPDATE.
            e.Property<uint>("xmin").IsRowVersion();
            // One version number per config, never reused.
            e.HasIndex(v => new { v.MobileConfigurationId, v.Version })
             .IsUnique()
             .HasDatabaseName("uq_mobile_configuration_versions_config_version");
            // Draft/publish lookups scoped by tenant + status.
            e.HasIndex(v => new { v.TenantId, v.Status })
             .HasDatabaseName("idx_mobile_configuration_versions_tenant_status");
            e.HasOne(v => v.MobileConfiguration).WithMany()
             .HasForeignKey(v => v.MobileConfigurationId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(v => v.Creator).WithMany()
             .HasForeignKey(v => v.CreatedBy).OnDelete(DeleteBehavior.SetNull);
        });

        // ── MobileTheme (TASK-531) ──────────────────────────────────────────────────────────
        // One row per MobileConfiguration (i.e. per tenant), not per version — the Theme Editor's
        // directly-editable working record. See MobileTheme's class remarks for the full design
        // rationale (also documented in domain-model.md).
        builder.Entity<MobileTheme>(e =>
        {
            e.ToTable("mobile_themes");
            e.HasKey(t => t.Id);
            e.Property(t => t.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(t => t.MobileConfigurationId).IsRequired();
            e.Property(t => t.TenantId).IsRequired();
            e.Property(t => t.LogoUrl).HasColumnType("text");
            e.Property(t => t.PrimaryColor).HasMaxLength(20).IsRequired();
            e.Property(t => t.SecondaryColor).HasMaxLength(20).IsRequired();
            e.Property(t => t.BackgroundColor).HasMaxLength(20).IsRequired();
            e.Property(t => t.SurfaceColor).HasMaxLength(20).IsRequired();
            e.Property(t => t.TextPrimaryColor).HasMaxLength(20).IsRequired();
            e.Property(t => t.TextSecondaryColor).HasMaxLength(20).IsRequired();
            e.Property(t => t.ButtonRadius).IsRequired();
            e.Property(t => t.CardRadius).IsRequired();
            e.Property(t => t.SpacingPreset).HasMaxLength(20).IsRequired();
            e.Property(t => t.UpdatedAt).HasDefaultValueSql("NOW()");
            // One theme row per config.
            e.HasIndex(t => t.MobileConfigurationId).IsUnique().HasDatabaseName("uq_mobile_themes_config");
            e.HasOne(t => t.MobileConfiguration).WithOne(m => m.Theme)
             .HasForeignKey<MobileTheme>(t => t.MobileConfigurationId).OnDelete(DeleteBehavior.Cascade);
        });

    }
}
