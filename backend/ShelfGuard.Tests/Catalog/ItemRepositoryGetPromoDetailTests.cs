using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using Xunit;

namespace ShelfGuard.Tests.Catalog;

/// <summary>
/// TASK-691 Slice 5 — <c>ItemRepository.GetPromoDetailAsync</c>, the single-product-page banner's
/// promo/forecast query. Kept as its own EF InMemory-backed test class (same convention as
/// <see cref="ItemRepositoryGetPagedTests"/>) because the active/upcoming resolution and the
/// applied-cannibalization join are non-trivial LINQ that a service-level NSubstitute mock can't
/// exercise.
/// </summary>
public sealed class ItemRepositoryGetPromoDetailTests
{
    private static AppDbContext MakeDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"promo-detail-{Guid.NewGuid()}")
            .Options);

    [Fact]
    public async Task GetPromoDetailAsync_NoDiscounts_ReturnsNull()
    {
        await using var db = MakeDb();
        var product = new Item { TenantId = Guid.NewGuid(), Name = "Plain", ManagementType = "MTS" };
        db.Items.Add(product);
        await db.SaveChangesAsync();

        var result = await new ItemRepository(db).GetPromoDetailAsync(product.Id, upcomingWithinDays: 14);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetPromoDetailAsync_ActiveDiscount_ReturnsActiveStateNoCoefficient()
    {
        await using var db = MakeDb();
        var tenantId = Guid.NewGuid();
        var product = new Item { TenantId = tenantId, Name = "On sale", ManagementType = "MTS" };
        db.Items.Add(product);

        var discount = Discount.Create(tenantId, product.Id, Guid.NewGuid(), 15m, DiscountReason.Promo,
            validFrom: DateTime.UtcNow.AddDays(-1), validUntil: DateTime.UtcNow.AddDays(5));
        discount.Approve(Guid.NewGuid());
        db.Discounts.Add(discount);
        await db.SaveChangesAsync();

        var result = await new ItemRepository(db).GetPromoDetailAsync(product.Id, upcomingWithinDays: 14);

        Assert.NotNull(result);
        Assert.Equal("active", result!.State);
        Assert.Null(result.StartsAt);
        Assert.Equal(15m, result.DiscountPercent);
        Assert.Null(result.OrderCoefficient); // no cannibalization suggestion generated/applied yet
    }

    [Fact]
    public async Task GetPromoDetailAsync_UpcomingDiscountOnly_ReturnsUpcomingWithStartDate()
    {
        await using var db = MakeDb();
        var tenantId = Guid.NewGuid();
        var product = new Item { TenantId = tenantId, Name = "Coming soon", ManagementType = "MTS" };
        db.Items.Add(product);

        var startsAt = DateTime.UtcNow.AddDays(3);
        var discount = Discount.Create(tenantId, product.Id, Guid.NewGuid(), 10m, DiscountReason.Promo,
            validFrom: startsAt, validUntil: startsAt.AddDays(7));
        discount.Approve(Guid.NewGuid());
        db.Discounts.Add(discount);
        await db.SaveChangesAsync();

        var result = await new ItemRepository(db).GetPromoDetailAsync(product.Id, upcomingWithinDays: 14);

        Assert.NotNull(result);
        Assert.Equal("upcoming", result!.State);
        Assert.Equal(startsAt, result.StartsAt);
        Assert.Equal(10m, result.DiscountPercent);
    }

    [Fact]
    public async Task GetPromoDetailAsync_ActiveAndUpcomingAcrossStores_PicksActive()
    {
        await using var db = MakeDb();
        var tenantId = Guid.NewGuid();
        var product = new Item { TenantId = tenantId, Name = "Multi-store", ManagementType = "MTS" };
        db.Items.Add(product);

        var active = Discount.Create(tenantId, product.Id, Guid.NewGuid(), 20m, DiscountReason.Promo,
            validFrom: DateTime.UtcNow.AddDays(-1), validUntil: DateTime.UtcNow.AddDays(5));
        active.Approve(Guid.NewGuid());
        var upcoming = Discount.Create(tenantId, product.Id, Guid.NewGuid(), 30m, DiscountReason.Promo,
            validFrom: DateTime.UtcNow.AddDays(2), validUntil: DateTime.UtcNow.AddDays(9));
        upcoming.Approve(Guid.NewGuid());
        db.Discounts.AddRange(active, upcoming);
        await db.SaveChangesAsync();

        var result = await new ItemRepository(db).GetPromoDetailAsync(product.Id, upcomingWithinDays: 14);

        Assert.NotNull(result);
        Assert.Equal("active", result!.State);
        Assert.Equal(20m, result.DiscountPercent);
    }

    [Fact]
    public async Task GetPromoDetailAsync_AppliedCannibalizationForWinningDiscount_ReturnsCoefficient()
    {
        await using var db = MakeDb();
        var tenantId = Guid.NewGuid();
        var product = new Item { TenantId = tenantId, Name = "On sale", ManagementType = "MTS" };
        db.Items.Add(product);

        var discount = Discount.Create(tenantId, product.Id, Guid.NewGuid(), 15m, DiscountReason.Promo,
            validFrom: DateTime.UtcNow.AddDays(-1), validUntil: DateTime.UtcNow.AddDays(5));
        discount.Approve(Guid.NewGuid());
        db.Discounts.Add(discount);
        db.PromoCannibalizations.Add(new PromoCannibalization
        {
            TenantId = tenantId,
            DiscountId = discount.Id,
            AffectedProductId = product.Id,
            OrderCoefficient = 2.0m,
            IsApplied = true,
        });
        await db.SaveChangesAsync();

        var result = await new ItemRepository(db).GetPromoDetailAsync(product.Id, upcomingWithinDays: 14);

        Assert.NotNull(result);
        Assert.Equal(2.0m, result!.OrderCoefficient);
    }

    [Fact]
    public async Task GetPromoDetailAsync_UnappliedCannibalization_LeavesCoefficientNull()
    {
        await using var db = MakeDb();
        var tenantId = Guid.NewGuid();
        var product = new Item { TenantId = tenantId, Name = "On sale", ManagementType = "MTS" };
        db.Items.Add(product);

        var discount = Discount.Create(tenantId, product.Id, Guid.NewGuid(), 15m, DiscountReason.Promo,
            validFrom: DateTime.UtcNow.AddDays(-1), validUntil: DateTime.UtcNow.AddDays(5));
        discount.Approve(Guid.NewGuid());
        db.Discounts.Add(discount);
        db.PromoCannibalizations.Add(new PromoCannibalization
        {
            TenantId = tenantId,
            DiscountId = discount.Id,
            AffectedProductId = product.Id,
            OrderCoefficient = 2.0m,
            IsApplied = false, // AI-suggested but never approved by a manager
        });
        await db.SaveChangesAsync();

        var result = await new ItemRepository(db).GetPromoDetailAsync(product.Id, upcomingWithinDays: 14);

        Assert.NotNull(result);
        Assert.Null(result!.OrderCoefficient);
    }

    [Fact]
    public async Task GetPromoDetailAsync_AppliedCannibalizationForUnrelatedDiscount_LeavesCoefficientNull()
    {
        await using var db = MakeDb();
        var tenantId = Guid.NewGuid();
        var product = new Item { TenantId = tenantId, Name = "On sale", ManagementType = "MTS" };
        db.Items.Add(product);

        var currentDiscount = Discount.Create(tenantId, product.Id, Guid.NewGuid(), 15m, DiscountReason.Promo,
            validFrom: DateTime.UtcNow.AddDays(-1), validUntil: DateTime.UtcNow.AddDays(5));
        currentDiscount.Approve(Guid.NewGuid());
        db.Discounts.Add(currentDiscount);
        // Coefficient applied for a DIFFERENT (unrelated) discount id — must not leak in.
        db.PromoCannibalizations.Add(new PromoCannibalization
        {
            TenantId = tenantId,
            DiscountId = Guid.NewGuid(),
            AffectedProductId = product.Id,
            OrderCoefficient = 0.7m,
            IsApplied = true,
        });
        await db.SaveChangesAsync();

        var result = await new ItemRepository(db).GetPromoDetailAsync(product.Id, upcomingWithinDays: 14);

        Assert.NotNull(result);
        Assert.Null(result!.OrderCoefficient);
    }
}
