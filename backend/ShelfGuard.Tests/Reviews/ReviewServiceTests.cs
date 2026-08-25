using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NSubstitute.ReturnsExtensions;
using ShelfGuard.Application.Features.Reviews;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Exceptions;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Reviews;

public sealed class ReviewServiceTests
{
    private readonly IPurchaseReviewRepository _reviews = Substitute.For<IPurchaseReviewRepository>();
    private readonly IConsumerAccountRepository _consumerAccounts = Substitute.For<IConsumerAccountRepository>();
    private readonly ITenantRepository _tenants = Substitute.For<ITenantRepository>();
    private readonly ILoyaltyRepository _loyalty = Substitute.For<ILoyaltyRepository>();
    private readonly ReviewService _sut;

    public ReviewServiceTests()
    {
        _sut = new ReviewService(
            _reviews, _consumerAccounts, _tenants, _loyalty, NullLogger<ReviewService>.Instance);
    }

    private static ConsumerAccount MakeConsumer(
        Guid? id = null, string phone = "+380501234567", string fullName = "Тест Тестенко", bool isActive = true) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Phone = phone,
        PasswordHash = "hash",
        FullName = fullName,
        IsActive = isActive,
    };

    private static Tenant MakeTenant() => Tenant.Create("Acme", "acme");

    private static LoyaltyMembership MakeMembership(Guid tenantId, Guid consumerAccountId, Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        TenantId = tenantId,
        ConsumerAccountId = consumerAccountId,
        TotpSecret = "secret",
    };

    private static LoyaltyLedgerEntry MakeLedgerEntry(Guid tenantId, Guid membershipId, Guid posTransactionId) => new()
    {
        TenantId = tenantId,
        MembershipId = membershipId,
        PosTransactionId = posTransactionId,
        EntryType = LoyaltyEntryType.Accrual,
        Amount = 10m,
        BalanceAfter = 10m,
    };

    /// <summary>Wires up the full "this is genuinely the caller's own purchase" chain: a
    /// matching ledger entry, resolving to a membership owned by <paramref name="consumerId"/>.</summary>
    private void SetUpOwnedPurchase(Guid tenantId, Guid consumerId, Guid posTransactionId)
    {
        var membership = MakeMembership(tenantId, consumerId);
        var entry = MakeLedgerEntry(tenantId, membership.Id, posTransactionId);
        _loyalty.GetLedgerEntriesForTransactionsAsync(
                tenantId, Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(posTransactionId)), default)
            .Returns(new List<LoyaltyLedgerEntry> { entry });
        _loyalty.GetMembershipByIdAsync(membership.Id, tenantId, default).Returns(membership);
    }

    // ── CreateReviewAsync: ownership resolution ────────────────────────────

    [Fact]
    public async Task CreateReviewAsync_legitimately_owned_transaction_succeeds()
    {
        var consumerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var posTransactionId = Guid.NewGuid();
        _consumerAccounts.GetByIdAsync(consumerId, default).Returns(MakeConsumer(consumerId));
        _tenants.GetByIdAsync(tenantId, default).Returns(MakeTenant());
        SetUpOwnedPurchase(tenantId, consumerId, posTransactionId);
        _reviews.GetByTransactionAsync(posTransactionId, default).ReturnsNull();

        var (review, error, statusCode) = await _sut.CreateReviewAsync(
            consumerId, tenantId, posTransactionId, 5, "Чудово!");

        Assert.Null(error);
        Assert.NotNull(review);
        Assert.Equal(5, review!.Rating);
        Assert.Equal("Чудово!", review.Comment);
        Assert.Equal(posTransactionId, review.PosTransactionId);
        await _reviews.Received(1).AddAsync(
            Arg.Is<PurchaseReview>(r =>
                r.TenantId == tenantId && r.ConsumerAccountId == consumerId &&
                r.PosTransactionId == posTransactionId && r.Rating == 5),
            default);
        await _reviews.Received(1).SaveChangesAsync(default);
    }

    [Fact]
    public async Task CreateReviewAsync_transaction_belongs_to_different_consumer_returns_403()
    {
        var consumerId = Guid.NewGuid();
        var strangerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var posTransactionId = Guid.NewGuid();
        _consumerAccounts.GetByIdAsync(consumerId, default).Returns(MakeConsumer(consumerId));
        _tenants.GetByIdAsync(tenantId, default).Returns(MakeTenant());
        // The transaction's ledger entry resolves to a membership owned by someone else.
        SetUpOwnedPurchase(tenantId, strangerId, posTransactionId);

        var (review, error, statusCode) = await _sut.CreateReviewAsync(
            consumerId, tenantId, posTransactionId, 5, null);

        Assert.Null(review);
        Assert.Equal(403, statusCode);
        await _reviews.DidNotReceive().AddAsync(Arg.Any<PurchaseReview>(), default);
    }

    [Fact]
    public async Task CreateReviewAsync_transaction_with_no_loyalty_link_returns_403()
    {
        var consumerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var posTransactionId = Guid.NewGuid();
        _consumerAccounts.GetByIdAsync(consumerId, default).Returns(MakeConsumer(consumerId));
        _tenants.GetByIdAsync(tenantId, default).Returns(MakeTenant());
        // Walk-in purchase: no LoyaltyLedgerEntry references this PosTransactionId at all.
        _loyalty.GetLedgerEntriesForTransactionsAsync(
                tenantId, Arg.Any<IReadOnlyCollection<Guid>>(), default)
            .Returns(new List<LoyaltyLedgerEntry>());

        var (review, error, statusCode) = await _sut.CreateReviewAsync(
            consumerId, tenantId, posTransactionId, 4, null);

        Assert.Null(review);
        Assert.Equal(403, statusCode);
        await _reviews.DidNotReceive().AddAsync(Arg.Any<PurchaseReview>(), default);
    }

    // ── CreateReviewAsync: duplicate guard ─────────────────────────────────

    [Fact]
    public async Task CreateReviewAsync_existing_review_on_transaction_returns_409_without_insert()
    {
        var consumerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var posTransactionId = Guid.NewGuid();
        _consumerAccounts.GetByIdAsync(consumerId, default).Returns(MakeConsumer(consumerId));
        _tenants.GetByIdAsync(tenantId, default).Returns(MakeTenant());
        SetUpOwnedPurchase(tenantId, consumerId, posTransactionId);
        _reviews.GetByTransactionAsync(posTransactionId, default)
            .Returns(new PurchaseReview { TenantId = tenantId, ConsumerAccountId = consumerId, PosTransactionId = posTransactionId, Rating = 3 });

        var (review, error, statusCode) = await _sut.CreateReviewAsync(
            consumerId, tenantId, posTransactionId, 5, null);

        Assert.Null(review);
        Assert.Equal(409, statusCode);
        await _reviews.DidNotReceive().AddAsync(Arg.Any<PurchaseReview>(), default);
    }

    [Fact]
    public async Task CreateReviewAsync_db_unique_violation_race_returns_409()
    {
        var consumerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var posTransactionId = Guid.NewGuid();
        _consumerAccounts.GetByIdAsync(consumerId, default).Returns(MakeConsumer(consumerId));
        _tenants.GetByIdAsync(tenantId, default).Returns(MakeTenant());
        SetUpOwnedPurchase(tenantId, consumerId, posTransactionId);
        // Pre-check finds nothing (no race won yet from this request's point of view)...
        _reviews.GetByTransactionAsync(posTransactionId, default).ReturnsNull();
        // ...but a concurrent request already committed first by the time SaveChanges runs —
        // the DB-level backstop the unique index provides.
        _reviews.SaveChangesAsync(default).Throws(new DuplicateReviewException("dup"));

        var (review, error, statusCode) = await _sut.CreateReviewAsync(
            consumerId, tenantId, posTransactionId, 5, null);

        Assert.Null(review);
        Assert.Equal(409, statusCode);
    }

    // ── CreateReviewAsync: rating validation ───────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public async Task CreateReviewAsync_out_of_range_rating_returns_400_without_lookup(int rating)
    {
        var consumerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _consumerAccounts.GetByIdAsync(consumerId, default).Returns(MakeConsumer(consumerId));
        _tenants.GetByIdAsync(tenantId, default).Returns(MakeTenant());

        var (review, error, statusCode) = await _sut.CreateReviewAsync(
            consumerId, tenantId, Guid.NewGuid(), rating, null);

        Assert.Null(review);
        Assert.Equal(400, statusCode);
        await _loyalty.DidNotReceive().GetLedgerEntriesForTransactionsAsync(
            Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), default);
        await _reviews.DidNotReceive().AddAsync(Arg.Any<PurchaseReview>(), default);
    }

    [Fact]
    public async Task CreateReviewAsync_unknown_consumer_returns_404()
    {
        var consumerId = Guid.NewGuid();
        _consumerAccounts.GetByIdAsync(consumerId, default).ReturnsNull();

        var (review, error, statusCode) = await _sut.CreateReviewAsync(
            consumerId, Guid.NewGuid(), Guid.NewGuid(), 5, null);

        Assert.Null(review);
        Assert.Equal(404, statusCode);
    }

    [Fact]
    public async Task CreateReviewAsync_unknown_tenant_returns_404()
    {
        var consumerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _consumerAccounts.GetByIdAsync(consumerId, default).Returns(MakeConsumer(consumerId));
        _tenants.GetByIdAsync(tenantId, default).ReturnsNull();

        var (review, error, statusCode) = await _sut.CreateReviewAsync(
            consumerId, tenantId, Guid.NewGuid(), 5, null);

        Assert.Null(review);
        Assert.Equal(404, statusCode);
    }

    // ── ReplyAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ReplyAsync_first_reply_succeeds()
    {
        var tenantId = Guid.NewGuid();
        var staffUserId = Guid.NewGuid();
        var review = new PurchaseReview
        {
            TenantId = tenantId, ConsumerAccountId = Guid.NewGuid(), PosTransactionId = Guid.NewGuid(), Rating = 4,
        };
        _reviews.GetByIdAsync(review.Id, default).Returns(review);
        _consumerAccounts.GetByIdAsync(review.ConsumerAccountId, default).Returns(MakeConsumer(review.ConsumerAccountId));

        var (result, error, statusCode) = await _sut.ReplyAsync(tenantId, review.Id, staffUserId, "Дякуємо!");

        Assert.Null(error);
        Assert.NotNull(result);
        Assert.Equal("Дякуємо!", result!.ReplyText);
        Assert.Equal(staffUserId, result.RepliedByUserId);
        Assert.NotNull(review.RepliedAt);
        _reviews.Received(1).Update(review);
        await _reviews.Received(1).SaveChangesAsync(default);
    }

    [Fact]
    public async Task ReplyAsync_second_attempt_rejected_with_409()
    {
        var tenantId = Guid.NewGuid();
        var staffUserId = Guid.NewGuid();
        var review = new PurchaseReview
        {
            TenantId = tenantId,
            ConsumerAccountId = Guid.NewGuid(),
            PosTransactionId = Guid.NewGuid(),
            Rating = 4,
            ReplyText = "Вже відповіли",
            RepliedAt = DateTimeOffset.UtcNow,
            RepliedByUserId = Guid.NewGuid(),
        };
        _reviews.GetByIdAsync(review.Id, default).Returns(review);

        var (result, error, statusCode) = await _sut.ReplyAsync(tenantId, review.Id, staffUserId, "Ще одна відповідь");

        Assert.Null(result);
        Assert.Equal(409, statusCode);
        _reviews.DidNotReceive().Update(Arg.Any<PurchaseReview>());
        await _reviews.DidNotReceive().SaveChangesAsync(default);
    }

    [Fact]
    public async Task ReplyAsync_wrong_tenant_returns_404()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var staffUserId = Guid.NewGuid();
        var review = new PurchaseReview
        {
            TenantId = otherTenantId, ConsumerAccountId = Guid.NewGuid(), PosTransactionId = Guid.NewGuid(), Rating = 4,
        };
        _reviews.GetByIdAsync(review.Id, default).Returns(review);

        var (result, error, statusCode) = await _sut.ReplyAsync(tenantId, review.Id, staffUserId, "Відповідь");

        Assert.Null(result);
        Assert.Equal(404, statusCode);
    }

    [Fact]
    public async Task ReplyAsync_blank_reply_returns_400_without_lookup()
    {
        var tenantId = Guid.NewGuid();
        var staffUserId = Guid.NewGuid();

        var (result, error, statusCode) = await _sut.ReplyAsync(tenantId, Guid.NewGuid(), staffUserId, "   ");

        Assert.Null(result);
        Assert.Equal(400, statusCode);
        await _reviews.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), default);
    }
}
