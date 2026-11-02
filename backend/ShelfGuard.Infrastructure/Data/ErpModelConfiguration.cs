using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Entities.Erp;

namespace ShelfGuard.Infrastructure.Data;

internal static class ErpModelConfiguration
{
    internal static void Configure(ModelBuilder builder)
    {
        // Existing Id PKs already imply uniqueness. Composite keys make tenant/batch scope enforceable by FK.
        builder.Entity<Item>().HasAlternateKey(x => new { x.TenantId, x.Id });
        // Keep existing FK indexes even when a new composite key would cover their prefix.
        builder.Entity<Location>().HasIndex(x => x.TenantId);
        builder.Entity<LocationZone>().HasIndex(x => x.LocationId);
        builder.Entity<Location>().HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.Entity<LegalEntity>().HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.Entity<LocationZone>().HasAlternateKey(x => new { x.LocationId, x.Id });
        builder.Entity<ProductStock>().HasAlternateKey(x => new { x.TenantId, x.Id, x.ProductId, x.StoreId });

        var documents = Base<BusinessDocument>(builder, "business_documents");
        documents.HasAlternateKey(x => new { x.TenantId, x.Id });
        documents.HasIndex(x => new { x.TenantId, x.DocumentKey, x.Version }).IsUnique();
        documents.HasIndex(x => new { x.TenantId, x.DocumentKey }).IsUnique()
            .HasDatabaseName("UX_business_documents_active_version").HasFilter("\"Status\"='posted'");
        documents.HasIndex(x => new { x.TenantId, x.SourceSystem, x.SourceId, x.Version }).IsUnique().HasFilter("\"SourceId\" IS NOT NULL");
        documents.HasIndex(x => new { x.TenantId, x.DocumentType, x.Number });
        documents.HasIndex(x => new { x.TenantId, x.Status, x.CreatedAt });
        documents.Property(x => x.DocumentType).HasMaxLength(64).IsRequired();
        documents.Property(x => x.Number).HasMaxLength(100);
        documents.Property(x => x.Status).HasMaxLength(20).HasDefaultValue("draft");
        documents.Property(x => x.SourceSystem).HasMaxLength(64).IsRequired();
        documents.Property(x => x.SourceId).HasMaxLength(200);
        documents.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        documents.Property(x => x.ContentHash).HasMaxLength(64);
        documents.Property(x => x.CreatedAt).HasDefaultValueSql("NOW()");
        documents.Property(x => x.UpdatedAt).HasDefaultValueSql("NOW()");
        documents.Property<uint>("xmin").IsRowVersion();
        documents.HasOne<LegalEntity>().WithMany().HasForeignKey(x => new { x.TenantId, x.LegalEntityId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        DocumentRef(documents, nameof(BusinessDocument.ReversesDocumentId));
        Check(documents, "business_documents", "version", "\"Version\">0");
        Check(documents, "business_documents", "status", "\"Status\" IN ('draft','submitted','approved','posted','cancelled','reversed')");
        Check(documents, "business_documents", "posting_snapshot", "(\"Status\" IN ('posted','reversed')) = (\"PostedAt\" IS NOT NULL) AND (\"Status\" NOT IN ('posted','reversed') OR \"ContentHash\" IS NOT NULL)");
        Check(documents, "business_documents", "hash", "\"ContentHash\" IS NULL OR \"ContentHash\" ~ '^[0-9a-f]{64}$'");
        Check(documents, "business_documents", "currency", "\"Currency\" ~ '^[A-Z]{3}$'");
        Check(documents, "business_documents", "identity", "\"DocumentKey\"<>'00000000-0000-0000-0000-000000000000'::uuid AND length(btrim(\"DocumentType\"))>0 AND length(btrim(\"SourceSystem\"))>0 AND (\"ReversesDocumentId\" IS NULL OR \"ReversesDocumentId\"<>\"Id\")");

        var lines = Base<DocumentLine>(builder, "document_lines");
        lines.HasAlternateKey(x => new { x.TenantId, x.DocumentId, x.Id, x.ItemId });
        lines.HasIndex(x => new { x.TenantId, x.DocumentId, x.LineNumber }).IsUnique();
        DocumentRef(lines, nameof(DocumentLine.DocumentId));
        ItemRef(lines);
        LocationRef(lines, nameof(DocumentLine.FromLocationId));
        LocationRef(lines, nameof(DocumentLine.ToLocationId));
        foreach (var field in new[] { nameof(DocumentLine.Quantity), nameof(DocumentLine.UnitPrice), nameof(DocumentLine.UnitCost) })
            lines.Property(field).HasColumnType("numeric(18,4)");
        lines.Property(x => x.VatRate).HasColumnType("numeric(5,2)");
        lines.Property(x => x.VatAmount).HasColumnType("numeric(18,2)");
        lines.Property(x => x.Unit).HasMaxLength(24).IsRequired();
        lines.Property(x => x.ItemNameSnapshot).HasMaxLength(512).IsRequired();
        lines.Property(x => x.BarcodeSnapshot).HasMaxLength(128);
        lines.Property(x => x.BatchNumberSnapshot).HasMaxLength(100);
        lines.Property(x => x.Metadata).HasColumnType("jsonb");
        Check(lines, "document_lines", "values", "\"LineNumber\">0 AND \"Quantity\">0 AND (\"UnitPrice\" IS NULL OR \"UnitPrice\">=0) AND (\"UnitCost\" IS NULL OR \"UnitCost\">=0) AND (\"VatRate\" IS NULL OR \"VatRate\" BETWEEN 0 AND 100) AND (\"VatAmount\" IS NULL OR \"VatAmount\">=0)");
        Check(lines, "document_lines", "snapshot", "length(btrim(\"Unit\"))>0 AND length(btrim(\"ItemNameSnapshot\"))>0");

        var postings = Base<DocumentPosting>(builder, "document_postings");
        postings.HasAlternateKey(x => new { x.TenantId, x.Id });
        postings.HasAlternateKey(x => new { x.TenantId, x.DocumentId, x.Id });
        postings.HasIndex(x => new { x.TenantId, x.DocumentId }).IsUnique();
        postings.HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique();
        postings.HasIndex(x => new { x.TenantId, x.ReversesPostingId }).IsUnique().HasFilter("\"ReversesPostingId\" IS NOT NULL");
        DocumentRef(postings, nameof(DocumentPosting.DocumentId));
        postings.HasOne<DocumentPosting>().WithMany().HasForeignKey(x => new { x.TenantId, x.ReversesPostingId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        postings.Property(x => x.IdempotencyKey).HasMaxLength(160).IsRequired();
        postings.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
        postings.Property(x => x.PostedAt).HasDefaultValueSql("NOW()");
        Check(postings, "document_postings", "identity", "length(btrim(\"IdempotencyKey\"))>0 AND \"RequestHash\" ~ '^[0-9a-f]{64}$' AND (\"ReversesPostingId\" IS NULL OR \"ReversesPostingId\"<>\"Id\")");

        var links = Base<DocumentLink>(builder, "document_links");
        DocumentRef(links, nameof(DocumentLink.FromDocumentId));
        DocumentRef(links, nameof(DocumentLink.ToDocumentId));
        links.Property(x => x.RelationType).HasMaxLength(50).IsRequired();
        links.Property(x => x.CreatedAt).HasDefaultValueSql("NOW()");
        links.HasIndex(x => new { x.TenantId, x.FromDocumentId, x.ToDocumentId, x.RelationType }).IsUnique();
        Check(links, "document_links", "relation", "\"FromDocumentId\"<>\"ToDocumentId\" AND length(btrim(\"RelationType\"))>0");

        var approvals = Base<DocumentApproval>(builder, "document_approvals");
        DocumentRef(approvals, nameof(DocumentApproval.DocumentId));
        approvals.Property(x => x.Decision).HasMaxLength(20).IsRequired();
        approvals.Property(x => x.ContentHash).HasMaxLength(64).IsRequired();
        approvals.Property(x => x.Reason).HasMaxLength(2000);
        approvals.Property(x => x.CreatedAt).HasDefaultValueSql("NOW()");
        approvals.HasIndex(x => new { x.TenantId, x.DocumentId, x.Stage, x.ApproverId }).IsUnique();
        Check(approvals, "document_approvals", "decision", "\"Stage\">0 AND \"ApproverId\"<>'00000000-0000-0000-0000-000000000000'::uuid AND \"Decision\" IN ('approved','rejected') AND \"ContentHash\" ~ '^[0-9a-f]{64}$'");

        var ledger = Base<InventoryLedgerEntry>(builder, "inventory_ledger_entries");
        ledger.HasAlternateKey(x => new { x.TenantId, x.Id });
        ledger.HasAlternateKey(x => new { x.TenantId, x.BatchId, x.Id });
        ledger.HasIndex(x => new { x.TenantId, x.PostingId, x.EntryNumber }).IsUnique();
        ledger.HasIndex(x => new { x.TenantId, x.LocationId, x.ItemId, x.BatchId, x.OccurredAt });
        ledger.HasIndex(x => new { x.TenantId, x.ReversesEntryId }).IsUnique().HasFilter("\"ReversesEntryId\" IS NOT NULL");
        ledger.HasOne<DocumentPosting>().WithMany().HasForeignKey(x => new { x.TenantId, x.DocumentId, x.PostingId })
            .HasPrincipalKey(x => new { x.TenantId, x.DocumentId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        LineRef(ledger);
        ItemRef(ledger); LocationRef(ledger); BatchRef(ledger);
        ledger.HasOne<LocationZone>().WithMany().HasForeignKey(x => new { x.LocationId, x.ZoneId })
            .HasPrincipalKey(x => new { x.LocationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        ledger.HasOne<InventoryLedgerEntry>().WithMany().HasForeignKey(x => new { x.TenantId, x.ReversesEntryId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        foreach (var field in new[] { "QuantityDelta", "QuantityBefore", "QuantityAfter", "UnitCost", "ValueDelta" })
            ledger.Property(field).HasColumnType("numeric(18,4)");
        ledger.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        ledger.Property(x => x.BatchNumberSnapshot).HasMaxLength(100);
        ledger.Property(x => x.CreatedAt).HasDefaultValueSql("NOW()");
        Check(ledger, "inventory_ledger_entries", "delta", "\"EntryNumber\">0 AND \"QuantityDelta\"<>0 AND \"Currency\" ~ '^[A-Z]{3}$' AND (\"ReversesEntryId\" IS NULL OR \"ReversesEntryId\"<>\"Id\")");
        Check(ledger, "inventory_ledger_entries", "balance", "(\"QuantityBefore\" IS NULL AND \"QuantityAfter\" IS NULL) OR (\"QuantityBefore\" IS NOT NULL AND \"QuantityAfter\" IS NOT NULL AND \"QuantityBefore\">=0 AND \"QuantityAfter\">=0 AND \"QuantityAfter\"=\"QuantityBefore\"+\"QuantityDelta\")");
        Check(ledger, "inventory_ledger_entries", "value", "(\"UnitCost\" IS NULL OR \"UnitCost\">=0) AND (\"ValueDelta\" IS NULL OR (\"UnitCost\" IS NOT NULL AND \"ValueDelta\"=round(\"QuantityDelta\"*\"UnitCost\",4)))");

        var balances = Base<InventoryBalanceProjection>(builder, "inventory_balance_projections");
        ItemRef(balances); LocationRef(balances); BatchRef(balances);
        balances.HasIndex(x => new { x.TenantId, x.BatchId }).IsUnique();
        balances.HasIndex(x => new { x.TenantId, x.LocationId, x.ItemId });
        balances.HasOne<InventoryLedgerEntry>().WithMany().HasForeignKey(x => new { x.TenantId, x.BatchId, x.LastLedgerEntryId })
            .HasPrincipalKey(x => new { x.TenantId, x.BatchId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        balances.Property(x => x.Quantity).HasColumnType("numeric(18,4)");
        balances.Property(x => x.ReservedQuantity).HasColumnType("numeric(18,4)");
        balances.Property(x => x.UpdatedAt).HasDefaultValueSql("NOW()");
        balances.Property<uint>("xmin").IsRowVersion();
        Check(balances, "inventory_balance_projections", "quantities", "\"Quantity\">=0 AND \"ReservedQuantity\">=0 AND \"ReservedQuantity\"<=\"Quantity\"");

        var reservations = Base<InventoryReservation>(builder, "inventory_reservations");
        DocumentRef(reservations, nameof(InventoryReservation.DocumentId));
        LineRef(reservations); ItemRef(reservations); LocationRef(reservations); BatchRef(reservations);
        reservations.Property(x => x.IdempotencyKey).HasMaxLength(160).IsRequired();
        reservations.Property(x => x.Status).HasMaxLength(20).IsRequired();
        reservations.Property(x => x.Quantity).HasColumnType("numeric(18,4)");
        reservations.Property(x => x.RemainingQuantity).HasColumnType("numeric(18,4)");
        reservations.Property(x => x.CreatedAt).HasDefaultValueSql("NOW()");
        reservations.Property(x => x.UpdatedAt).HasDefaultValueSql("NOW()");
        reservations.Property<uint>("xmin").IsRowVersion();
        reservations.HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique();
        reservations.HasIndex(x => new { x.TenantId, x.LocationId, x.ItemId, x.Status });
        reservations.HasIndex(x => new { x.Status, x.ExpiresAt }).HasFilter("\"Status\"='active'");
        Check(reservations, "inventory_reservations", "quantities", "\"Quantity\">0 AND \"RemainingQuantity\">=0 AND \"RemainingQuantity\"<=\"Quantity\" AND length(btrim(\"IdempotencyKey\"))>0");
        Check(reservations, "inventory_reservations", "status", "(\"Status\"='active' AND \"RemainingQuantity\">0) OR (\"Status\" IN ('consumed','released','expired') AND \"RemainingQuantity\"=0)");

        var reconciliations = Base<InventoryReconciliation>(builder, "inventory_reconciliations");
        ItemRef(reconciliations); LocationRef(reconciliations); BatchRef(reconciliations);
        reconciliations.HasIndex(x => new { x.TenantId, x.RunId, x.BatchId }).IsUnique();
        reconciliations.HasIndex(x => new { x.TenantId, x.Status, x.AsOf });
        reconciliations.Property(x => x.LegacyQuantity).HasColumnType("numeric(18,4)");
        reconciliations.Property(x => x.LedgerQuantity).HasColumnType("numeric(18,4)");
        reconciliations.Property(x => x.Difference).HasColumnType("numeric(18,4)").HasComputedColumnSql("\"LegacyQuantity\"-\"LedgerQuantity\"", stored: true);
        reconciliations.Property(x => x.Status).HasMaxLength(20).IsRequired();
        reconciliations.Property(x => x.Explanation).HasMaxLength(2000);
        reconciliations.Property(x => x.CreatedAt).HasDefaultValueSql("NOW()");
        Check(reconciliations, "inventory_reconciliations", "result", "(\"Status\"='unverifiable' AND (\"LegacyQuantity\" IS NULL OR \"LedgerQuantity\" IS NULL)) OR (\"LegacyQuantity\" IS NOT NULL AND \"LedgerQuantity\" IS NOT NULL AND ((\"Status\"='matched' AND \"LegacyQuantity\"=\"LedgerQuantity\") OR (\"Status\"='discrepancy' AND \"LegacyQuantity\"<>\"LedgerQuantity\")))");

        var outbox = Base<OutboxEvent>(builder, "outbox_events");
        outbox.HasOne<DocumentPosting>().WithMany().HasForeignKey(x => new { x.TenantId, x.PostingId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        outbox.Property(x => x.EventType).HasMaxLength(128).IsRequired();
        outbox.Property(x => x.AggregateType).HasMaxLength(64).IsRequired();
        outbox.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
        outbox.Property(x => x.Status).HasMaxLength(20).HasDefaultValue("pending");
        outbox.Property(x => x.DeliveryScope).HasMaxLength(20).HasDefaultValue("live");
        outbox.Property(x => x.LastError).HasMaxLength(2000);
        outbox.Property(x => x.CreatedAt).HasDefaultValueSql("NOW()");
        outbox.Property(x => x.AvailableAt).HasDefaultValueSql("NOW()");
        outbox.Property<uint>("xmin").IsRowVersion();
        outbox.HasIndex(x => new { x.TenantId, x.AggregateType, x.AggregateId, x.AggregateVersion, x.EventOrdinal }).IsUnique();
        outbox.HasIndex(x => new { x.TenantId, x.PostingId }).IsUnique().HasFilter("\"PostingId\" IS NOT NULL");
        outbox.HasIndex(x => new { x.AvailableAt, x.CreatedAt }).HasFilter("\"Status\"='pending'");
        outbox.HasIndex(x => x.LockedUntil).HasFilter("\"Status\"='processing'");
        outbox.HasIndex(x => new { x.TenantId, x.Status, x.CreatedAt });
        Check(outbox, "outbox_events", "envelope", "\"AggregateId\"<>'00000000-0000-0000-0000-000000000000'::uuid AND \"SchemaVersion\">0 AND \"AggregateVersion\">0 AND \"EventOrdinal\">0 AND length(btrim(\"EventType\"))>0 AND length(btrim(\"AggregateType\"))>0 AND jsonb_typeof(\"Payload\")='object'");
        Check(outbox, "outbox_events", "delivery", "\"Attempts\">=0 AND ((\"DeliveryScope\"='live' AND \"Status\" IN ('pending','processing','published','dead')) OR (\"DeliveryScope\"='historical' AND \"Status\"='suppressed' AND \"Attempts\"=0)) AND ((\"Status\"='processing' AND \"LockToken\" IS NOT NULL AND \"LockedUntil\" IS NOT NULL) OR (\"Status\"<>'processing' AND \"LockToken\" IS NULL AND \"LockedUntil\" IS NULL)) AND ((\"Status\"='published')=(\"PublishedAt\" IS NOT NULL))");

        var inbox = Base<InboxDeduplication>(builder, "inbox_deduplication");
        inbox.Property(x => x.Consumer).HasMaxLength(128).IsRequired();
        inbox.Property(x => x.PayloadHash).HasMaxLength(64).IsRequired();
        inbox.Property(x => x.Result).HasColumnType("jsonb");
        inbox.Property(x => x.ProcessedAt).HasDefaultValueSql("NOW()");
        inbox.HasIndex(x => new { x.TenantId, x.Consumer, x.MessageId }).IsUnique();
        Check(inbox, "inbox_deduplication", "identity", "\"MessageId\"<>'00000000-0000-0000-0000-000000000000'::uuid AND length(btrim(\"Consumer\"))>0 AND \"PayloadHash\" ~ '^[0-9a-f]{64}$'");
    }

    private static EntityTypeBuilder<T> Base<T>(ModelBuilder builder, string table) where T : class
    {
        var entity = builder.Entity<T>();
        entity.ToTable(table); entity.HasKey("Id");
        entity.Property<Guid>("Id").HasDefaultValueSql("gen_random_uuid()");
        entity.HasOne<Tenant>().WithMany().HasForeignKey("TenantId").OnDelete(DeleteBehavior.Restrict);
        Check(entity, table, "scope", "\"Id\"<>'00000000-0000-0000-0000-000000000000'::uuid AND \"TenantId\"<>'00000000-0000-0000-0000-000000000000'::uuid");
        var numbers = typeof(T).GetProperties().Where(p => (Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType) == typeof(decimal));
        if (numbers.Any()) Check(entity, table, "finite", string.Join(" AND ", numbers.Select(p => $"(\"{p.Name}\" IS NULL OR \"{p.Name}\"<>'NaN'::numeric)")));
        return entity;
    }

    private static void Check<T>(EntityTypeBuilder<T> entity, string table, string name, string sql) where T : class =>
        entity.ToTable(table, t => t.HasCheckConstraint($"CK_{table}_{name}", sql));

    private static void DocumentRef<T>(EntityTypeBuilder<T> entity, string property) where T : class =>
        entity.HasOne<BusinessDocument>().WithMany().HasForeignKey("TenantId", property)
            .HasPrincipalKey("TenantId", "Id").OnDelete(DeleteBehavior.Restrict);

    private static void ItemRef<T>(EntityTypeBuilder<T> entity) where T : class =>
        entity.HasOne<Item>().WithMany().HasForeignKey("TenantId", "ItemId")
            .HasPrincipalKey("TenantId", "Id").OnDelete(DeleteBehavior.Restrict);

    private static void LocationRef<T>(EntityTypeBuilder<T> entity, string property = "LocationId") where T : class =>
        entity.HasOne<Location>().WithMany().HasForeignKey("TenantId", property)
            .HasPrincipalKey("TenantId", "Id").OnDelete(DeleteBehavior.Restrict);

    private static void BatchRef<T>(EntityTypeBuilder<T> entity) where T : class =>
        entity.HasOne<ProductStock>().WithMany().HasForeignKey("TenantId", "BatchId", "ItemId", "LocationId")
            .HasPrincipalKey("TenantId", "Id", "ProductId", "StoreId").OnDelete(DeleteBehavior.Restrict);

    private static void LineRef<T>(EntityTypeBuilder<T> entity) where T : class =>
        entity.HasOne<DocumentLine>().WithMany().HasForeignKey("TenantId", "DocumentId", "DocumentLineId", "ItemId")
            .HasPrincipalKey("TenantId", "DocumentId", "Id", "ItemId").OnDelete(DeleteBehavior.Restrict);
}
