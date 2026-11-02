using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Entities.Erp;
namespace ShelfGuard.Infrastructure.Data;

internal static class PricingModelConfiguration
{
    internal static void Configure(ModelBuilder b)
    {
        var a = b.Entity<PosPricingSnapshot>();
        a.ToTable("pos_pricing_snapshots", t => {
            t.HasCheckConstraint("CK_pos_pricing_snapshot_protocol", "\"Protocol\"='pos.pricing.archive.v1' AND \"Channel\" IN ('pos-online','pos-offline-legacy','pos-offline-pr1')");
            t.HasCheckConstraint("CK_pos_pricing_snapshot_hash", "\"ContentHash\" ~ '^[0-9a-f]{64}$'");
        });
        a.HasKey(x => x.TransactionId);
        a.Property(x => x.OriginTransactionId).HasDefaultValueSql("pg_current_xact_id()::text::bigint");
        a.HasIndex(x => new { x.TenantId, x.StoreId, x.EvaluatedAt });
        a.HasOne<PosTransaction>().WithOne(x => x.PricingSnapshot).HasForeignKey<PosPricingSnapshot>(x => new { x.TenantId, x.TransactionId })
            .HasPrincipalKey<PosTransaction>(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        a.HasOne<Location>().WithMany().HasForeignKey(x => new { x.TenantId, x.StoreId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        a.Property(x => x.Protocol).HasMaxLength(40).IsRequired(); a.Property(x => x.Channel).HasMaxLength(30).IsRequired();
        a.Property(x => x.ContentHash).HasMaxLength(64).IsRequired(); a.Property(x => x.Payload).HasColumnType("json").IsRequired();
        b.Entity<PosTransaction>().Property(x => x.PricingArchiveRequired).HasDefaultValue(false);
        var p = b.Entity<PriceList>(); p.ToTable("price_lists", t => {
            t.HasCheckConstraint("CK_price_lists_version", "\"Version\">0 AND \"Priority\" BETWEEN 0 AND 10000");
            t.HasCheckConstraint("CK_price_lists_validity", "\"ValidUntil\" IS NULL OR \"ValidUntil\">\"ValidFrom\"");
            t.HasCheckConstraint("CK_price_lists_currency", "\"Currency\"='UAH'");
            t.HasCheckConstraint("CK_price_lists_hash", "\"Fingerprint\" ~ '^[0-9a-f]{64}$'");
        });
        p.HasKey(x => x.Id); p.HasAlternateKey(x => new { x.TenantId, x.Id });
        p.HasIndex(x => new { x.TenantId, x.ListKey, x.Version }).IsUnique(); p.HasIndex(x => new { x.TenantId, x.DocumentId }).IsUnique();
        p.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        p.HasOne<BusinessDocument>().WithMany().HasForeignKey(x => new { x.TenantId, x.DocumentId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        p.HasOne<Location>().WithMany().HasForeignKey(x => new { x.TenantId, x.LocationId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        p.Property(x => x.Name).HasMaxLength(200).IsRequired(); p.Property(x => x.Currency).HasMaxLength(3).IsRequired(); p.Property(x => x.Fingerprint).HasMaxLength(64).IsRequired();
        var l = b.Entity<PriceListItem>(); l.ToTable("price_list_items", t => {
            t.HasCheckConstraint("CK_price_list_items_values", "\"Price\">=0 AND \"VatRate\" BETWEEN 0 AND 100 AND length(btrim(\"Unit\"))>0");
            t.HasCheckConstraint("CK_price_list_items_hash", "\"ItemFingerprint\" ~ '^[0-9a-f]{64}$'");
        }); l.HasKey(x => x.Id); l.HasIndex(x => new { x.TenantId, x.PriceListId, x.ItemId }).IsUnique();
        l.HasOne<PriceList>().WithMany().HasForeignKey(x => new { x.TenantId, x.PriceListId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        l.HasOne<Item>().WithMany().HasForeignKey(x => new { x.TenantId, x.ItemId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        l.Property(x => x.Price).HasColumnType("numeric(12,2)"); l.Property(x => x.VatRate).HasColumnType("numeric(5,2)"); l.Property(x => x.Unit).HasMaxLength(20).IsRequired(); l.Property(x => x.ItemName).HasMaxLength(255).IsRequired(); l.Property(x => x.ItemFingerprint).HasMaxLength(64).IsRequired();
        var s = b.Entity<PriceResolutionSnapshot>(); s.ToTable("price_resolution_snapshots", t => t.HasCheckConstraint("CK_price_resolution_snapshots_hash", "\"ContentHash\" ~ '^[0-9a-f]{64}$' AND \"RequestHash\" ~ '^[0-9a-f]{64}$'")); s.HasKey(x => x.Id);
        s.HasIndex(x => new { x.TenantId, x.CreatedBy, x.CreatedAt });
        s.HasOne<Item>().WithMany().HasForeignKey(x => new { x.TenantId, x.ItemId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        s.HasOne<Location>().WithMany().HasForeignKey(x => new { x.TenantId, x.LocationId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        s.Property(x => x.RequestHash).HasMaxLength(64).IsRequired(); s.Property(x => x.ContentHash).HasMaxLength(64).IsRequired(); s.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
    }
}
