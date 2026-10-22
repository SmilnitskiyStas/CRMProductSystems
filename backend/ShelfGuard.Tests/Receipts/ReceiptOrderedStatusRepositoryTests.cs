using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using Xunit;

namespace ShelfGuard.Tests.Receipts;

/// <summary>TASK-757: "ordered" receipts count as in transit, are untouched by the auto job, map to unified "confirmed".</summary>
public sealed class ReceiptOrderedStatusRepositoryTests
{
    private static AppDbContext MakeDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"ordered-{Guid.NewGuid()}").Options);

    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _store = Guid.NewGuid();
    private readonly Guid _product = Guid.NewGuid();

    private StockReceipt Seed(AppDbContext db, string status, string source, Guid? takenBy, decimal qty)
    {
        var r = new StockReceipt
        {
            TenantId = _tenant, DestinationStoreId = _store, Status = status, Source = source, TakenByUserId = takenBy,
        };
        r.Items.Add(new StockReceiptItem { ReceiptId = r.Id, ProductId = _product, QuantityOrdered = qty });
        db.StockReceipts.Add(r);
        if (!db.Locations.Local.Any(l => l.Id == _store))
            db.Locations.Add(new Location { Id = _store, TenantId = _tenant, Name = "Магазин" });
        return r;
    }

    [Fact]
    public async Task InTransit_CountsDraftAndOrdered_ExcludesReceivedCancelled()
    {
        await using var db = MakeDb();
        Seed(db, "draft", ReceiptSource.Manual, null, 10);
        Seed(db, "ordered", ReceiptSource.Manual, Guid.NewGuid(), 20);
        Seed(db, "received", ReceiptSource.Manual, null, 100);
        Seed(db, "cancelled", ReceiptSource.Manual, null, 100);
        await db.SaveChangesAsync();

        var res = await new OrderCalcRepository(db).GetInTransitAsync(_store, [_product]);
        Assert.Equal(30m, res[_product]);
    }

    [Fact]
    public async Task InTransit_ExcludeAutoUntaken_OnlyDropsUntakenAutoDrafts_NotOrdered()
    {
        await using var db = MakeDb();
        Seed(db, "draft", ReceiptSource.AutoReplenishment, null, 10);             // excluded
        Seed(db, "draft", ReceiptSource.AutoReplenishment, Guid.NewGuid(), 20);   // taken -> counts
        Seed(db, "ordered", ReceiptSource.AutoReplenishment, Guid.NewGuid(), 40); // ordered -> counts
        await db.SaveChangesAsync();

        var res = await new OrderCalcRepository(db).GetInTransitAsync(_store, [_product], excludeAutoUntakenDrafts: true);
        Assert.Equal(60m, res[_product]);
    }

    [Fact]
    public async Task AutoJob_UntakenDraftLookup_IgnoresOrdered()
    {
        await using var db = MakeDb();
        var draft = Seed(db, "draft", ReceiptSource.AutoReplenishment, null, 10);
        Seed(db, "ordered", ReceiptSource.AutoReplenishment, null, 10);
        await db.SaveChangesAsync();

        var res = await new ReceiptRepository(db).GetUntakenAutoDraftsAsync(_store);
        Assert.Equal(draft.Id, Assert.Single(res).Id);
    }

    [Fact]
    public async Task Tracking_OrderedMapsToConfirmed_DraftStaysNew_AndFilterWorks()
    {
        await using var db = MakeDb();
        Seed(db, "draft", ReceiptSource.Manual, null, 1);
        var ordered = Seed(db, "ordered", ReceiptSource.Manual, Guid.NewGuid(), 1);
        await db.SaveChangesAsync();
        var repo = new OrderTrackingRepository(db);

        var summary = await repo.GetSummaryAsync(_tenant, null, DateOnly.FromDateTime(DateTime.UtcNow));
        Assert.Equal(1, summary.New);
        Assert.Equal(1, summary.Confirmed);

        var (rows, total) = await repo.GetPagedAsync(_tenant, "receipt", ["confirmed"], null, null, 1, 20);
        Assert.Equal(1, total);
        var row = Assert.Single(rows);
        Assert.Equal(ordered.Id, row.Id);
        Assert.Equal("confirmed", row.Status);
    }
}
