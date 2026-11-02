using System.Text.Json;
using ShelfGuard.Application.Features.Erp.Contracts;
using Xunit;

namespace ShelfGuard.Tests.Erp;

public sealed class ErpEventContractTests
{
    private static ErpEventEnvelopeV1 Envelope()
    {
        var entry = new InventoryLedgerDeltaV1(Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            12.3456m, 3.1234m, 38.5602m, "UAH");
        return ErpEventContracts.ForPosting(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow,
            new InventoryDocumentPostedV1(Guid.NewGuid(), Guid.NewGuid(), 2, Guid.NewGuid(), [entry]));
    }

    [Fact]
    public void Envelope_and_outbox_roundtrip_keep_message_identity_version_and_decimal_precision()
    {
        var original = Envelope();
        var json = ErpEventContracts.Serialize(original);
        using var parsed = JsonDocument.Parse(json);
        var entry = parsed.RootElement.GetProperty("payload").GetProperty("entries")[0];
        Assert.Equal(JsonValueKind.String, entry.GetProperty("quantityDelta").ValueKind);
        Assert.Equal("12.3456", entry.GetProperty("quantityDelta").GetString());
        Assert.Equal("3.1234", entry.GetProperty("unitCost").GetString());
        var row = ErpEventContracts.ToOutbox(ErpEventContracts.Deserialize(json));
        var restored = ErpEventContracts.FromOutbox(row);
        Assert.Equal(json, ErpEventContracts.Serialize(restored));
        Assert.Equal(original.MessageId, row.Id);
        Assert.Equal(2, row.AggregateVersion);
        Assert.NotNull(row.PostingId);
        Assert.Equal("pending", row.Status);
    }

    [Fact]
    public void Historical_backfill_produces_a_suppressed_event_without_changing_business_facts()
    {
        var envelope = Envelope();
        var row = ErpEventContracts.ToOutbox(envelope, historical: true);
        Assert.Equal("historical", row.DeliveryScope);
        Assert.Equal("suppressed", row.Status);
        Assert.Equal(0, row.Attempts);
        Assert.Null(row.PublishedAt);
        Assert.Equal(ErpEventContracts.Serialize(envelope), ErpEventContracts.Serialize(ErpEventContracts.FromOutbox(row)));
    }

    [Theory]
    [InlineData("version")]
    [InlineData("tenant")]
    [InlineData("aggregate")]
    [InlineData("type")]
    [InlineData("time")]
    [InlineData("array")]
    public void Invalid_or_unknown_envelope_is_rejected(string error)
    {
        var envelope = Envelope();
        envelope = error switch
        {
            "version" => envelope with { SchemaVersion = 2 },
            "tenant" => envelope with { TenantId = Guid.Empty },
            "aggregate" => envelope with { AggregateVersion = 3 },
            "type" => envelope with { EventType = "unknown" },
            "time" => envelope with { OccurredAt = DateTimeOffset.Now.ToOffset(TimeSpan.FromHours(2)) },
            _ => envelope with { Payload = JsonSerializer.SerializeToElement(new[] { "invalid" }) },
        };
        Assert.Throws<ArgumentException>(() => ErpEventContracts.Serialize(envelope));
    }

    [Theory]
    [InlineData("precision")]
    [InlineData("zero")]
    [InlineData("cost")]
    [InlineData("currency")]
    [InlineData("duplicates")]
    [InlineData("reverse")]
    public void Invalid_ledger_payload_is_rejected(string error)
    {
        var entry = new InventoryLedgerDeltaV1(Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            1m, null, null, "UAH");
        entry = error switch
        {
            "precision" => entry with { QuantityDelta = 0.00001m },
            "zero" => entry with { QuantityDelta = 0 },
            "cost" => entry with { UnitCost = -1 },
            "currency" => entry with { Currency = "uah" },
            "reverse" => entry with { ReversesEntryId = Guid.NewGuid() },
            _ => entry,
        };
        var entries = error == "duplicates" ? new[] { entry, entry } : new[] { entry };
        Assert.Throws<ArgumentException>(() => ErpEventContracts.ForPosting(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow,
            new InventoryDocumentPostedV1(Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), entries)));
    }

    [Fact]
    public void Unknown_historical_cost_remains_null_in_the_contract()
    {
        var entry = new InventoryLedgerDeltaV1(Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, null, null, "UAH");
        var envelope = ErpEventContracts.ForPosting(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow,
            new InventoryDocumentPostedV1(Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), [entry]));
        var row = ErpEventContracts.ToOutbox(envelope);
        Assert.Contains("\"unitCost\":null", row.Payload);
        row = new() { TenantId = row.TenantId, OccurredAt = DateTime.UtcNow, Payload = row.Payload };
        Assert.Throws<ArgumentException>(() => ErpEventContracts.FromOutbox(row));
    }

    [Theory]
    [InlineData("active", 2, true)]
    [InlineData("released", 0, true)]
    [InlineData("consumed", 1, false)]
    [InlineData("expired", 1, false)]
    [InlineData("active", 0, false)]
    public void Reservation_event_keeps_reservation_separate_from_physical_stock(string status, int remaining, bool valid)
    {
        var payload = new InventoryReservationChangedV1(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), null, 2, remaining, status);
        var envelope = new ErpEventEnvelopeV1(Guid.NewGuid(), Guid.NewGuid(), ErpEventContracts.InventoryReservationChanged, 1,
            "inventory_reservation", payload.ReservationId, 1, 1, DateTimeOffset.UtcNow,
            JsonSerializer.SerializeToElement(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        if (valid) Assert.Null(ErpEventContracts.ToOutbox(envelope).PostingId);
        else Assert.Throws<ArgumentException>(() => ErpEventContracts.Validate(envelope));
    }
}
