using Microsoft.EntityFrameworkCore;
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
    public DbSet<Store> Stores => Set<Store>();
    public DbSet<StoreZone> StoreZones => Set<StoreZone>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<ProductSegment> ProductSegments => Set<ProductSegment>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();

    // Products (v1 tenant-aware)
    public DbSet<CatalogProduct> CatalogProducts => Set<CatalogProduct>();
    public DbSet<ProductSupplierSetting> ProductSupplierSettings => Set<ProductSupplierSetting>();

    // Stock
    public DbSet<ProductStock> ProductStocks => Set<ProductStock>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<StockEvent> StockEvents => Set<StockEvent>();

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
            e.Property(t => t.IsActive).HasDefaultValue(true);
            e.Property(t => t.CreatedAt).HasDefaultValueSql("NOW()");
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
            e.Property(u => u.IsActive).HasDefaultValue(true);
            e.Property(u => u.CreatedAt).HasDefaultValueSql("NOW()");
            e.Property(u => u.InvitedByName).HasMaxLength(255).IsRequired(false);
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

        // ── Store ───────────────────────────────────────────────────────────
        builder.Entity<Store>(e =>
        {
            e.ToTable("stores");
            e.HasKey(s => s.Id);
            e.Property(s => s.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(s => s.Name).HasMaxLength(255).IsRequired();
            e.Property(s => s.Type).HasMaxLength(50).IsRequired();
            e.Property(s => s.FloorPlan).HasColumnType("jsonb");
            e.Property(s => s.Latitude).HasColumnType("decimal(10,7)");
            e.Property(s => s.Longitude).HasColumnType("decimal(10,7)");
            e.Property(s => s.IsActive).HasDefaultValue(true);
            e.Property(s => s.CreatedAt).HasDefaultValueSql("NOW()");
            e.HasOne(s => s.Tenant).WithMany()
             .HasForeignKey(s => s.TenantId).OnDelete(DeleteBehavior.Restrict);
        });

        // ── StoreZone ───────────────────────────────────────────────────────
        builder.Entity<StoreZone>(e =>
        {
            e.ToTable("store_zones");
            e.HasKey(z => z.Id);
            e.Property(z => z.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(z => z.Name).HasMaxLength(255).IsRequired();
            e.Property(z => z.Type).HasMaxLength(50).IsRequired();
            e.Property(z => z.Position).HasColumnType("jsonb");
            e.Property(z => z.TempMin).HasColumnType("decimal(5,1)");
            e.Property(z => z.TempMax).HasColumnType("decimal(5,1)");
            e.Property(z => z.ShelvesCount).HasDefaultValue(1);
            e.Property(z => z.IsActive).HasDefaultValue(true);
            e.HasOne(z => z.Store).WithMany(s => s.Zones)
             .HasForeignKey(z => z.StoreId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── Category ────────────────────────────────────────────────────────
        builder.Entity<Category>(e =>
        {
            e.ToTable("categories");
            e.HasKey(c => c.Id);
            e.Property(c => c.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(c => c.Name).HasMaxLength(255).IsRequired();
            e.Property(c => c.IsActive).HasDefaultValue(true);
            e.HasOne(c => c.Tenant).WithMany()
             .HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Restrict);
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

        // ── CatalogProduct ──────────────────────────────────────────────────
        builder.Entity<CatalogProduct>(e =>
        {
            e.ToTable("catalog_products");
            e.HasKey(p => p.Id);
            e.Property(p => p.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(p => p.Barcode).HasMaxLength(100);
            e.Property(p => p.Name).HasMaxLength(255).IsRequired();
            e.Property(p => p.Unit).HasMaxLength(20).HasDefaultValue("шт");
            e.Property(p => p.ManagementType).HasMaxLength(10).HasDefaultValue("MTS");
            e.Property(p => p.MinStock).HasColumnType("decimal(10,2)");
            e.Property(p => p.MaxStock).HasColumnType("decimal(10,2)");
            e.Property(p => p.SafetyBuffer).HasColumnType("decimal(10,2)");
            e.Property(p => p.StorageTempMin).HasColumnType("decimal(5,1)");
            e.Property(p => p.StorageTempMax).HasColumnType("decimal(5,1)");
            e.Property(p => p.VatRate).HasColumnType("decimal(5,2)").HasDefaultValue(20m);
            e.Property(p => p.PricePurchase).HasColumnType("decimal(12,2)");
            e.Property(p => p.PriceRetail).HasColumnType("decimal(12,2)");
            e.Property(p => p.IsActive).HasDefaultValue(true);
            e.Property(p => p.CreatedAt).HasDefaultValueSql("NOW()");
            e.HasOne(p => p.Tenant).WithMany()
             .HasForeignKey(p => p.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(p => p.Category).WithMany()
             .HasForeignKey(p => p.CategoryId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
            e.HasOne(p => p.Segment).WithMany()
             .HasForeignKey(p => p.SegmentId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
            e.HasOne(p => p.DefaultSupplier).WithMany()
             .HasForeignKey(p => p.DefaultSupplierId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
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
            e.HasOne(s => s.Product).WithMany()
             .HasForeignKey(s => s.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(s => s.Store).WithMany()
             .HasForeignKey(s => s.StoreId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(s => s.Zone).WithMany()
             .HasForeignKey(s => s.ZoneId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
        });

        // ── StockMovement ───────────────────────────────────────────────────
        builder.Entity<StockMovement>(e =>
        {
            e.ToTable("stock_movements");
            e.HasKey(m => m.Id);
            e.Property(m => m.Id).HasDefaultValueSql("gen_random_uuid()");
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
            e.Property(w => w.CreatedAt).HasDefaultValueSql("NOW()");
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
            e.Property(n => n.Channel).HasMaxLength(50).IsRequired();
            e.Property(n => n.EventType).HasMaxLength(100);
            e.Property(n => n.Payload).HasColumnType("jsonb");
            e.Property(n => n.Status).HasMaxLength(20).HasDefaultValue("pending");
            e.Property(n => n.CreatedAt).HasDefaultValueSql("NOW()");
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
            e.HasIndex(d => new { d.StoreId, d.ProductId, d.Date }).IsUnique();
            e.HasIndex(d => new { d.TenantId, d.Date });
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
            e.HasOne(w => w.Category).WithMany()
             .HasForeignKey(w => w.CategoryId).OnDelete(DeleteBehavior.Cascade).IsRequired(false);
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
            e.HasIndex(s => new { s.StoreId, s.SupplierId });
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
    }
}
