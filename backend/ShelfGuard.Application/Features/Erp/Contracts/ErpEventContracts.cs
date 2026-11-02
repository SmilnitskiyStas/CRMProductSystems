using System.Text.Json;
using System.Text.Json.Serialization;
using ShelfGuard.Domain.Entities.Erp;

namespace ShelfGuard.Application.Features.Erp.Contracts;

public sealed record InventoryLedgerDeltaV1(
    Guid EntryId, int EntryNumber, Guid ItemId, Guid LocationId, Guid BatchId,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal QuantityDelta,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal? UnitCost,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal? ValueDelta,
    string Currency, Guid? ZoneId = null, Guid? ReversesEntryId = null);

public sealed record InventoryDocumentPostedV1(
    Guid DocumentId, Guid DocumentKey, int DocumentVersion, Guid PostingId,
    IReadOnlyList<InventoryLedgerDeltaV1> Entries, Guid? ReversesPostingId = null);

public sealed record InventoryReservationChangedV1(
    Guid ReservationId, Guid DocumentId, Guid DocumentLineId, Guid ItemId, Guid LocationId, Guid? BatchId,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal Quantity,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal RemainingQuantity,
    string Status);

public sealed record ErpEventEnvelopeV1(
    Guid MessageId, Guid TenantId, string EventType, int SchemaVersion,
    string AggregateType, Guid AggregateId, int AggregateVersion, int EventOrdinal,
    DateTimeOffset OccurredAt, JsonElement Payload, Guid? CorrelationId = null, Guid? CausationId = null);

/// <summary>Versioned wire contracts only. No publisher, consumer or legacy write path is enabled by W2.</summary>
public static class ErpEventContracts
{
    public const string InventoryDocumentPosted = "inventory.document_posted";
    public const string InventoryReservationChanged = "inventory.reservation_changed";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static ErpEventEnvelopeV1 ForPosting(Guid tenantId, Guid messageId, DateTimeOffset occurredAt,
        InventoryDocumentPostedV1 data, Guid? correlationId = null, Guid? causationId = null)
    {
        var envelope = new ErpEventEnvelopeV1(messageId, tenantId, InventoryDocumentPosted, 1,
            "business_document", data.DocumentKey, data.DocumentVersion, 1, occurredAt.ToUniversalTime(),
            JsonSerializer.SerializeToElement(data, Json), correlationId, causationId);
        Validate(envelope);
        return envelope;
    }

    public static string Serialize(ErpEventEnvelopeV1 envelope)
    {
        Validate(envelope);
        return JsonSerializer.Serialize(envelope, Json);
    }

    public static ErpEventEnvelopeV1 Deserialize(string json)
    {
        if (json.Length > 1_048_576) throw new ArgumentException("ERP envelope is too large.");
        var envelope = JsonSerializer.Deserialize<ErpEventEnvelopeV1>(json, Json)
            ?? throw new ArgumentException("ERP envelope is required.");
        Validate(envelope);
        return envelope;
    }

    public static OutboxEvent ToOutbox(ErpEventEnvelopeV1 envelope, bool historical = false)
    {
        Validate(envelope);
        return new OutboxEvent
        {
            Id = envelope.MessageId, TenantId = envelope.TenantId, EventType = envelope.EventType,
            SchemaVersion = envelope.SchemaVersion, AggregateType = envelope.AggregateType,
            AggregateId = envelope.AggregateId, AggregateVersion = envelope.AggregateVersion, EventOrdinal = envelope.EventOrdinal,
            OccurredAt = envelope.OccurredAt.UtcDateTime, Payload = envelope.Payload.GetRawText(),
            PostingId = envelope.EventType == InventoryDocumentPosted ? envelope.Payload.Deserialize<InventoryDocumentPostedV1>(Json)!.PostingId : null,
            CorrelationId = envelope.CorrelationId, CausationId = envelope.CausationId,
            DeliveryScope = historical ? "historical" : "live", Status = historical ? "suppressed" : "pending",
        };
    }

    public static ErpEventEnvelopeV1 FromOutbox(OutboxEvent row)
    {
        if (row.OccurredAt.Kind != DateTimeKind.Utc) throw new ArgumentException("Outbox time must be UTC.");
        using var document = JsonDocument.Parse(row.Payload);
        var envelope = new ErpEventEnvelopeV1(row.Id, row.TenantId, row.EventType, row.SchemaVersion,
            row.AggregateType, row.AggregateId, row.AggregateVersion, row.EventOrdinal,
            new DateTimeOffset(row.OccurredAt), document.RootElement.Clone(),
            row.CorrelationId, row.CausationId);
        Validate(envelope);
        if (envelope.EventType == InventoryDocumentPosted
            && row.PostingId != envelope.Payload.Deserialize<InventoryDocumentPostedV1>(Json)!.PostingId)
            throw new ArgumentException("Outbox posting reference differs from its payload.");
        return envelope;
    }

    public static void Validate(ErpEventEnvelopeV1 envelope)
    {
        if (envelope.SchemaVersion != 1 || envelope.MessageId == Guid.Empty || envelope.TenantId == Guid.Empty
            || envelope.AggregateId == Guid.Empty || envelope.AggregateVersion <= 0 || envelope.EventOrdinal <= 0
            || envelope.OccurredAt == default || envelope.OccurredAt.Offset != TimeSpan.Zero
            || envelope.Payload.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Invalid ERP v1 envelope.");
        if (envelope.EventType == InventoryDocumentPosted)
        {
            var data = envelope.Payload.Deserialize<InventoryDocumentPostedV1>(Json)!;
            if (envelope.AggregateType != "business_document" || data.DocumentId == Guid.Empty || data.PostingId == Guid.Empty
                || data.DocumentKey != envelope.AggregateId || data.DocumentVersion != envelope.AggregateVersion
                || data.Entries is not { Count: > 0 }
                || data.Entries.Any(x => x is null)
                || data.Entries.Select(x => x.EntryId).Distinct().Count() != data.Entries.Count
                || data.Entries.Select(x => x.EntryNumber).Distinct().Count() != data.Entries.Count)
                throw new ArgumentException("Invalid inventory posting payload.");
            foreach (var entry in data.Entries)
                if (entry.EntryId == Guid.Empty || entry.EntryNumber <= 0 || entry.ItemId == Guid.Empty
                    || entry.LocationId == Guid.Empty || entry.BatchId == Guid.Empty || entry.QuantityDelta == 0
                    || !Amount(entry.QuantityDelta) || !Amount(entry.UnitCost) || !Amount(entry.ValueDelta)
                    || entry.UnitCost is < 0 || entry.Currency is not { Length: 3 }
                    || entry.Currency.Any(c => c is < 'A' or > 'Z')
                    || (entry.ValueDelta is decimal value && (entry.UnitCost is not decimal cost || value != Math.Round(entry.QuantityDelta * cost, 4, MidpointRounding.AwayFromZero)))
                    || data.ReversesPostingId.HasValue != entry.ReversesEntryId.HasValue)
                    throw new ArgumentException("Invalid inventory delta.");
        }
        else if (envelope.EventType == InventoryReservationChanged)
        {
            var data = envelope.Payload.Deserialize<InventoryReservationChangedV1>(Json)!;
            if (envelope.AggregateType != "inventory_reservation" || data.ReservationId != envelope.AggregateId
                || data.DocumentId == Guid.Empty || data.DocumentLineId == Guid.Empty || data.ItemId == Guid.Empty || data.LocationId == Guid.Empty
                || data.Quantity <= 0 || data.RemainingQuantity < 0 || data.RemainingQuantity > data.Quantity
                || !Amount(data.Quantity) || !Amount(data.RemainingQuantity)
                || !(data.Status == "active" && data.RemainingQuantity > 0 || data.Status is "consumed" or "released" or "expired" && data.RemainingQuantity == 0))
                throw new ArgumentException("Invalid inventory reservation payload.");
        }
        else throw new ArgumentException("Unsupported ERP event type/schema version.");
    }

    private static bool Amount(decimal? amount) => amount is null ||
        decimal.Abs(amount.Value) <= 99_999_999_999_999.9999m && decimal.Round(amount.Value, 4) == amount.Value;
}
