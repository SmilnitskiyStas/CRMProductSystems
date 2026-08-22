using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using Xunit;

namespace ShelfGuard.Tests.Events;

/// <summary>
/// TASK-593 — <c>EventRepository</c>'s new <c>Scope == "stores"</c> support (multi-store demand
/// events via the <c>DemandEventStore</c> join table, TASK-592 schema). Uses the EF Core InMemory
/// provider against a real <c>EventRepository</c> — same convention as
/// <see cref="ShelfGuard.Tests.Catalog.ItemRepositoryGetPagedTests"/> — because the "any store
/// matches" predicate (<c>e.Stores.Any(...)</c>) and EF's navigation fixup on
/// <c>ReplaceStoresForEventAsync</c> both need a real change tracker, not an NSubstitute mock.
/// </summary>
public sealed class EventRepositoryStoresTests
{
    private static AppDbContext MakeDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"events-stores-{Guid.NewGuid()}")
            .Options);

    private static DemandEvent StoresEvent(Guid tenantId, params Guid[] storeIds)
    {
        var ev = new DemandEvent
        {
            TenantId = tenantId,
            Name = "Local fair",
            Scope = "stores",
            StartsAt = new DateOnly(2026, 1, 1),
            EndsAt = new DateOnly(2026, 12, 31),
        };
        foreach (var storeId in storeIds)
            ev.Stores.Add(new DemandEventStore { EventId = ev.Id, StoreId = storeId });
        return ev;
    }

    // ── GetAsync — "stores" scope, any-match semantics ───────────────────────

    [Fact]
    public async Task GetAsync_StoresScope_MatchesEitherTargetedStore()
    {
        await using var db = MakeDb();
        var tenantId = Guid.NewGuid();
        var storeA = Guid.NewGuid();
        var storeB = Guid.NewGuid();
        var storeC = Guid.NewGuid();
        var ev = StoresEvent(tenantId, storeA, storeB);
        db.DemandEvents.Add(ev);
        await db.SaveChangesAsync();

        var repo = new EventRepository(db);

        Assert.Single(await repo.GetAsync(null, null, [storeA]));
        Assert.Single(await repo.GetAsync(null, null, [storeB]));
        Assert.Empty(await repo.GetAsync(null, null, [storeC]));
        Assert.Single(await repo.GetAsync(null, null, [storeA, storeC])); // any-match, not all-match
    }

    [Fact]
    public async Task GetAsync_NetworkScopedEvent_AlwaysReturnedRegardlessOfStoreIds()
    {
        await using var db = MakeDb();
        var tenantId = Guid.NewGuid();
        var unrelatedStore = Guid.NewGuid();
        var networkEvent = new DemandEvent
        {
            TenantId = tenantId,
            Name = "National holiday",
            Scope = "network",
            StartsAt = new DateOnly(2026, 1, 1),
            EndsAt = new DateOnly(2026, 12, 31),
        };
        db.DemandEvents.Add(networkEvent);
        await db.SaveChangesAsync();

        var repo = new EventRepository(db);
        var result = await repo.GetAsync(null, null, [unrelatedStore]);

        Assert.Single(result);
        Assert.Equal(networkEvent.Id, result[0].Id);
    }

    [Fact]
    public async Task GetAsync_SingleStoreScope_StillMatchesOnlyThatStore_RegressionGuard()
    {
        await using var db = MakeDb();
        var tenantId = Guid.NewGuid();
        var storeA = Guid.NewGuid();
        var storeB = Guid.NewGuid();
        var ev = new DemandEvent
        {
            TenantId = tenantId,
            Name = "Store promo",
            Scope = "store",
            StoreId = storeA,
            StartsAt = new DateOnly(2026, 1, 1),
            EndsAt = new DateOnly(2026, 12, 31),
        };
        db.DemandEvents.Add(ev);
        await db.SaveChangesAsync();

        var repo = new EventRepository(db);

        Assert.Single(await repo.GetAsync(null, null, [storeA]));
        Assert.Empty(await repo.GetAsync(null, null, [storeB]));
    }

    [Fact]
    public async Task GetAsync_NullOrEmptyStoreIds_ReturnsAllStores_RegressionGuard()
    {
        await using var db = MakeDb();
        var tenantId = Guid.NewGuid();
        var storeScoped = new DemandEvent
        {
            TenantId = tenantId, Name = "A", Scope = "store", StoreId = Guid.NewGuid(),
            StartsAt = new DateOnly(2026, 1, 1), EndsAt = new DateOnly(2026, 12, 31),
        };
        var storesScoped = StoresEvent(tenantId, Guid.NewGuid());
        var networkScoped = new DemandEvent
        {
            TenantId = tenantId, Name = "C", Scope = "network",
            StartsAt = new DateOnly(2026, 1, 1), EndsAt = new DateOnly(2026, 12, 31),
        };
        db.DemandEvents.AddRange(storeScoped, storesScoped, networkScoped);
        await db.SaveChangesAsync();

        var repo = new EventRepository(db);

        Assert.Equal(3, (await repo.GetAsync(null, null, null)).Count);
        Assert.Equal(3, (await repo.GetAsync(null, null, [])).Count);
    }

    // ── GetCandidatesForDateAsync — order-calc consumer, business-critical path ──

    [Fact]
    public async Task GetCandidatesForDateAsync_IncludesStoresScopedEventForTargetedStore()
    {
        await using var db = MakeDb();
        var tenantId = Guid.NewGuid();
        var storeA = Guid.NewGuid();
        var storeB = Guid.NewGuid();
        var ev = StoresEvent(tenantId, storeA, storeB);
        ev.Coefficients.Add(new DemandEventCoefficient
        {
            EventId = ev.Id, ScopeType = "category", Coefficient = 2.0m,
        });
        db.DemandEvents.Add(ev);
        await db.SaveChangesAsync();

        var repo = new EventRepository(db);
        var date = new DateOnly(2026, 6, 15);

        var forA = await repo.GetCandidatesForDateAsync(storeA, date);
        Assert.Single(forA);
        Assert.Single(forA[0].Coefficients); // coefficients travel with the candidate

        var forB = await repo.GetCandidatesForDateAsync(storeB, date);
        Assert.Single(forB);

        var untargetedStore = Guid.NewGuid();
        Assert.Empty(await repo.GetCandidatesForDateAsync(untargetedStore, date));
    }

    // ── ReplaceStoresForEventAsync — write path ──────────────────────────────

    [Fact]
    public async Task ReplaceStoresForEventAsync_InsertsRowsAndFixesUpNavigation()
    {
        await using var db = MakeDb();
        var ev = new DemandEvent { Name = "X", Scope = "stores", StartsAt = new(2026, 1, 1), EndsAt = new(2026, 1, 2) };
        db.DemandEvents.Add(ev);
        await db.SaveChangesAsync();

        var storeA = Guid.NewGuid();
        var storeB = Guid.NewGuid();
        var repo = new EventRepository(db);
        await repo.ReplaceStoresForEventAsync(ev.Id, [storeA, storeB]);
        await repo.SaveChangesAsync();

        var reloaded = await repo.GetByIdAsync(ev.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(2, reloaded!.Stores.Count);
        Assert.Contains(reloaded.Stores, s => s.StoreId == storeA);
        Assert.Contains(reloaded.Stores, s => s.StoreId == storeB);
    }

    [Fact]
    public async Task ReplaceStoresForEventAsync_CalledAgain_ReplacesPreviousSetEntirely()
    {
        await using var db = MakeDb();
        var ev = new DemandEvent { Name = "X", Scope = "stores", StartsAt = new(2026, 1, 1), EndsAt = new(2026, 1, 2) };
        db.DemandEvents.Add(ev);
        await db.SaveChangesAsync();

        var storeA = Guid.NewGuid();
        var storeB = Guid.NewGuid();
        var storeC = Guid.NewGuid();
        var repo = new EventRepository(db);
        await repo.ReplaceStoresForEventAsync(ev.Id, [storeA, storeB]);
        await repo.SaveChangesAsync();

        await repo.ReplaceStoresForEventAsync(ev.Id, [storeC]);
        await repo.SaveChangesAsync();

        var reloaded = await repo.GetByIdAsync(ev.Id);
        Assert.Single(reloaded!.Stores);
        Assert.Equal(storeC, reloaded.Stores.Single().StoreId);
    }

    [Fact]
    public async Task ReplaceStoresForEventAsync_WithEmptySet_ClearsExistingRows()
    {
        await using var db = MakeDb();
        var ev = new DemandEvent { Name = "X", Scope = "network", StartsAt = new(2026, 1, 1), EndsAt = new(2026, 1, 2) };
        db.DemandEvents.Add(ev);
        await db.SaveChangesAsync();

        var repo = new EventRepository(db);
        await repo.ReplaceStoresForEventAsync(ev.Id, [Guid.NewGuid()]);
        await repo.SaveChangesAsync();

        await repo.ReplaceStoresForEventAsync(ev.Id, []);
        await repo.SaveChangesAsync();

        var reloaded = await repo.GetByIdAsync(ev.Id);
        Assert.Empty(reloaded!.Stores);
    }
}
