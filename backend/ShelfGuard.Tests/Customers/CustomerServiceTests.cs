using NSubstitute;
using ShelfGuard.Application.Common;
using ShelfGuard.Application.Features.ConsumerProfile;
using ShelfGuard.Application.Features.ConsumerProfile.Dtos;
using ShelfGuard.Application.Features.Customers;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Customers;

/// <summary>
/// TASK-360 (Block 9 pre-launch audit) — Customers had zero test coverage. Focused on the two
/// gaps the audit found: (1) tenant scoping was correct in practice but unverified — the
/// service always stamps the caller's own tenantId regardless of what's in the request, since
/// CreateCustomerDto carries no TenantId field at all; (2) CreateAsync/UpdateAsync only checked
/// Name non-empty + phone uniqueness — Email/Phone had no format validation whatsoever before
/// this task's fix (ValidateContactInfo).
/// </summary>
public sealed class CustomerServiceTests
{
    private readonly ICustomerRepository _repo = Substitute.For<ICustomerRepository>();
    private readonly ILoyaltyRepository _loyaltyRepo = Substitute.For<ILoyaltyRepository>();
    private readonly IConsumerSupportTicketRepository _supportRepo = Substitute.For<IConsumerSupportTicketRepository>();
    private readonly IPurchaseReviewRepository _reviewRepo = Substitute.For<IPurchaseReviewRepository>();
    private readonly IConsumerProfileService _consumerProfile = Substitute.For<IConsumerProfileService>();
    private readonly CustomerService _sut;
    private readonly Guid _tenantId = Guid.NewGuid();

    public CustomerServiceTests() =>
        _sut = new CustomerService(_repo, _loyaltyRepo, _supportRepo, _reviewRepo, _consumerProfile);

    // ── Tenant isolation ──────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_AlwaysStampsCallersTenantId_RegardlessOfRequest()
    {
        var dto = new CreateCustomerDto("Іван Петренко", null, null, null, null);

        var (customer, error) = await _sut.CreateAsync(_tenantId, dto);

        Assert.Null(error);
        Assert.NotNull(customer);
        await _repo.Received(1).CreateAsync(
            Arg.Is<Customer>(c => c.TenantId == _tenantId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetByIdAsync_PassesCallersTenantId_ToRepository()
    {
        var id = Guid.NewGuid();
        await _sut.GetByIdAsync(id, _tenantId);

        await _repo.Received(1).GetByIdWithTransactionsAsync(id, _tenantId, Arg.Any<CancellationToken>());
    }

    // ── Contact info validation (new — previously no format check at all) ──────

    [Theory]
    [InlineData("not-a-phone")]
    [InlineData("abc")]
    [InlineData("123")] // too short
    public async Task CreateAsync_InvalidPhoneFormat_ReturnsError(string phone)
    {
        var dto = new CreateCustomerDto("Customer", phone, null, null, null);

        var (customer, error) = await _sut.CreateAsync(_tenantId, dto);

        Assert.Null(customer);
        Assert.Contains("phone", error, StringComparison.OrdinalIgnoreCase);
        await _repo.DidNotReceive().CreateAsync(Arg.Any<Customer>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("missing-at-sign.com")]
    [InlineData("no-domain@")]
    public async Task CreateAsync_InvalidEmailFormat_ReturnsError(string email)
    {
        var dto = new CreateCustomerDto("Customer", null, email, null, null);

        var (customer, error) = await _sut.CreateAsync(_tenantId, dto);

        Assert.Null(customer);
        Assert.Contains("email", error, StringComparison.OrdinalIgnoreCase);
        await _repo.DidNotReceive().CreateAsync(Arg.Any<Customer>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("+380501234567")]
    [InlineData("050 123 45 67")]
    [InlineData("(050) 123-45-67")]
    public async Task CreateAsync_ValidPhoneFormats_Succeed(string phone)
    {
        var dto = new CreateCustomerDto("Customer", phone, null, null, null);

        var (customer, error) = await _sut.CreateAsync(_tenantId, dto);

        Assert.Null(error);
        Assert.NotNull(customer);
    }

    [Fact]
    public async Task UpdateAsync_InvalidEmailFormat_ReturnsError_AndDoesNotSave()
    {
        var existing = new Customer { TenantId = _tenantId, Name = "Old" };
        _repo.GetByIdAsync(existing.Id, _tenantId, Arg.Any<CancellationToken>()).Returns(existing);

        var dto = new UpdateCustomerDto("New Name", null, "invalid-email", null, null);
        var (customer, error) = await _sut.UpdateAsync(existing.Id, _tenantId, dto);

        Assert.Null(customer);
        Assert.Contains("email", error, StringComparison.OrdinalIgnoreCase);
        await _repo.DidNotReceive().UpdateAsync(Arg.Any<Customer>(), Arg.Any<CancellationToken>());
    }

    // ── Existing behavior (phone uniqueness) — unaffected by the validation fix ──

    [Fact]
    public async Task CreateAsync_DuplicatePhone_ReturnsConflictError()
    {
        _repo.ExistsByPhoneAsync("+380501234567", _tenantId, null, Arg.Any<CancellationToken>()).Returns(true);
        var dto = new CreateCustomerDto("Customer", "+380501234567", null, null, null);

        var (customer, error) = await _sut.CreateAsync(_tenantId, dto);

        Assert.Null(customer);
        Assert.Contains("already exists", error, StringComparison.OrdinalIgnoreCase);
    }

    // ── TASK-618: tier/progress, open-ticket count, recent reviews on the detail DTO ──────────
    // Service-layer tests mock the three read repositories, so they pin CustomerService's own
    // wiring/DTO-mapping logic (including the tier-progress math it owns). The actual SQL
    // filtering behind CountOpenByCustomerIdAsync/GetRecentForCustomerAsync is pinned separately
    // by repository-level InMemory tests (ConsumerSupportTicketRepositoryTests,
    // PurchaseReviewRepositoryGetRecentForCustomerTests) since mocking the repository interface
    // here can't exercise that.

    [Fact]
    public async Task GetByIdAsync_NoLoyaltyMembership_TierFieldsAreAllNull()
    {
        var id = Guid.NewGuid();
        var customer = new Customer { Id = id, TenantId = _tenantId, Name = "Walk-in" };
        _repo.GetByIdWithTransactionsAsync(id, _tenantId, Arg.Any<CancellationToken>()).Returns(customer);
        _loyaltyRepo.GetMembershipByCustomerIdAsync(id, _tenantId, Arg.Any<CancellationToken>())
            .Returns((LoyaltyMembership?)null);

        var dto = await _sut.GetByIdAsync(id, _tenantId);

        Assert.NotNull(dto);
        Assert.Null(dto!.CurrentTierName);
        Assert.Null(dto.CompositeScore);
        Assert.Null(dto.TierProgressPercent);
    }

    [Fact]
    public async Task GetByIdAsync_MembershipWithoutTierAssignedYet_ReportsScoreButTierAndProgressStayNull()
    {
        var id = Guid.NewGuid();
        var customer = new Customer { Id = id, TenantId = _tenantId, Name = "New Member" };
        var membership = new LoyaltyMembership
        {
            TenantId = _tenantId,
            ConsumerAccountId = Guid.NewGuid(),
            CustomerId = id,
            CompositeScore = 12.5m,
            // CurrentTierId left null: not yet recomputed / hasn't cleared the lowest tier.
        };
        _repo.GetByIdWithTransactionsAsync(id, _tenantId, Arg.Any<CancellationToken>()).Returns(customer);
        _loyaltyRepo.GetMembershipByCustomerIdAsync(id, _tenantId, Arg.Any<CancellationToken>()).Returns(membership);

        var dto = await _sut.GetByIdAsync(id, _tenantId);

        Assert.Null(dto!.CurrentTierName);
        Assert.Equal(12.5m, dto.CompositeScore);
        Assert.Null(dto.TierProgressPercent);
        await _loyaltyRepo.DidNotReceive().GetTierLadderAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetByIdAsync_MembershipWithTierAssigned_ComputesProgressTowardNextTier()
    {
        var id = Guid.NewGuid();
        var bronze = new LoyaltyTierDefinition { TenantId = _tenantId, Name = "Bronze", SortOrder = 1, MinCompositeScore = 100m };
        var silver = new LoyaltyTierDefinition { TenantId = _tenantId, Name = "Silver", SortOrder = 2, MinCompositeScore = 300m };
        var membership = new LoyaltyMembership
        {
            TenantId = _tenantId,
            ConsumerAccountId = Guid.NewGuid(),
            CustomerId = id,
            CompositeScore = 150m,
            CurrentTierId = bronze.Id,
            CurrentTier = bronze,
        };
        var customer = new Customer { Id = id, TenantId = _tenantId, Name = "Regular" };
        _repo.GetByIdWithTransactionsAsync(id, _tenantId, Arg.Any<CancellationToken>()).Returns(customer);
        _loyaltyRepo.GetMembershipByCustomerIdAsync(id, _tenantId, Arg.Any<CancellationToken>()).Returns(membership);
        _loyaltyRepo.GetTierLadderAsync(_tenantId, Arg.Any<CancellationToken>()).Returns([bronze, silver]);

        var dto = await _sut.GetByIdAsync(id, _tenantId);

        Assert.Equal("Bronze", dto!.CurrentTierName);
        Assert.Equal(150m, dto.CompositeScore);
        Assert.Equal(50m, dto.TierProgressPercent); // 150 / 300 * 100
    }

    [Fact]
    public async Task GetByIdAsync_AlreadyAtTopTier_ProgressIsNull()
    {
        var id = Guid.NewGuid();
        var gold = new LoyaltyTierDefinition { TenantId = _tenantId, Name = "Gold", SortOrder = 3, MinCompositeScore = 500m };
        var membership = new LoyaltyMembership
        {
            TenantId = _tenantId,
            ConsumerAccountId = Guid.NewGuid(),
            CustomerId = id,
            CompositeScore = 600m,
            CurrentTierId = gold.Id,
            CurrentTier = gold,
        };
        var customer = new Customer { Id = id, TenantId = _tenantId, Name = "VIP" };
        _repo.GetByIdWithTransactionsAsync(id, _tenantId, Arg.Any<CancellationToken>()).Returns(customer);
        _loyaltyRepo.GetMembershipByCustomerIdAsync(id, _tenantId, Arg.Any<CancellationToken>()).Returns(membership);
        _loyaltyRepo.GetTierLadderAsync(_tenantId, Arg.Any<CancellationToken>()).Returns([gold]);

        var dto = await _sut.GetByIdAsync(id, _tenantId);

        Assert.Equal("Gold", dto!.CurrentTierName);
        Assert.Null(dto.TierProgressPercent);
    }

    [Fact]
    public async Task GetByIdAsync_OpenTicketCount_PassesThroughRepositoryCount()
    {
        var id = Guid.NewGuid();
        var customer = new Customer { Id = id, TenantId = _tenantId, Name = "Ticketed" };
        _repo.GetByIdWithTransactionsAsync(id, _tenantId, Arg.Any<CancellationToken>()).Returns(customer);
        _supportRepo.CountOpenByCustomerIdAsync(id, _tenantId, Arg.Any<CancellationToken>()).Returns(2);

        var dto = await _sut.GetByIdAsync(id, _tenantId);

        Assert.Equal(2, dto!.OpenTicketCount);
        await _supportRepo.Received(1).CountOpenByCustomerIdAsync(id, _tenantId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetByIdAsync_NoTicketsOrReviews_ReturnsZeroAndEmptyList_NotNull()
    {
        var id = Guid.NewGuid();
        var customer = new Customer { Id = id, TenantId = _tenantId, Name = "Quiet Customer" };
        _repo.GetByIdWithTransactionsAsync(id, _tenantId, Arg.Any<CancellationToken>()).Returns(customer);
        _supportRepo.CountOpenByCustomerIdAsync(id, _tenantId, Arg.Any<CancellationToken>()).Returns(0);
        _reviewRepo.GetRecentForCustomerAsync(id, _tenantId, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var dto = await _sut.GetByIdAsync(id, _tenantId);

        Assert.Equal(0, dto!.OpenTicketCount);
        Assert.NotNull(dto.RecentReviews);
        Assert.Empty(dto.RecentReviews);
    }

    [Fact]
    public async Task GetByIdAsync_RecentReviews_MapsToSummaryDtoPreservingRepositoryOrder()
    {
        var id = Guid.NewGuid();
        var customer = new Customer { Id = id, TenantId = _tenantId, Name = "Reviewer" };
        var newer = new PurchaseReview
        {
            TenantId = _tenantId, ConsumerAccountId = Guid.NewGuid(), PosTransactionId = Guid.NewGuid(),
            Rating = 5, Comment = "Great", CreatedAt = DateTimeOffset.UtcNow,
        };
        var older = new PurchaseReview
        {
            TenantId = _tenantId, ConsumerAccountId = Guid.NewGuid(), PosTransactionId = Guid.NewGuid(),
            Rating = 3, Comment = "Ok", CreatedAt = DateTimeOffset.UtcNow.AddDays(-1), ReplyText = "Thanks",
        };
        _repo.GetByIdWithTransactionsAsync(id, _tenantId, Arg.Any<CancellationToken>()).Returns(customer);
        _reviewRepo.GetRecentForCustomerAsync(id, _tenantId, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([newer, older]);

        var dto = await _sut.GetByIdAsync(id, _tenantId);

        Assert.Equal(2, dto!.RecentReviews.Count);
        Assert.Equal((short)5, dto.RecentReviews[0].Rating);
        Assert.Equal("Great", dto.RecentReviews[0].Comment);
        Assert.Equal((short)3, dto.RecentReviews[1].Rating);
        Assert.Equal("Thanks", dto.RecentReviews[1].ReplyText);
    }

    // ── TASK-621b: staff-facing profile-change history (delegates to IConsumerProfileService) ──

    [Fact]
    public async Task GetProfileChangeHistoryAsync_CustomerWithLinkedMembership_ReturnsConsumerHistory()
    {
        var customerId = Guid.NewGuid();
        var consumerAccountId = Guid.NewGuid();
        var membership = new LoyaltyMembership
        {
            TenantId = _tenantId,
            ConsumerAccountId = consumerAccountId,
            CustomerId = customerId,
        };
        var expected = new PagedResult<ConsumerProfileChangeDto>
        {
            Items =
            [
                new ConsumerProfileChangeDto(
                    ConsumerAccountProfileChangeField.Phone, "+380501234567", "+380507654321", DateTimeOffset.UtcNow),
            ],
            TotalCount = 1,
            Page = 1,
            PageSize = 50,
        };
        _loyaltyRepo.GetMembershipByCustomerIdAsync(customerId, _tenantId, Arg.Any<CancellationToken>())
            .Returns(membership);
        _consumerProfile.GetProfileChangeHistoryAsync(consumerAccountId, 1, 50, Arg.Any<CancellationToken>())
            .Returns((expected, (string?)null, (int?)null));

        var result = await _sut.GetProfileChangeHistoryAsync(customerId, _tenantId, 1, 50);

        Assert.Same(expected, result);
        await _consumerProfile.Received(1).GetProfileChangeHistoryAsync(
            consumerAccountId, 1, 50, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetProfileChangeHistoryAsync_CustomerWithNoLoyaltyMembership_ReturnsEmptyPage_NotAnError()
    {
        var customerId = Guid.NewGuid();
        _loyaltyRepo.GetMembershipByCustomerIdAsync(customerId, _tenantId, Arg.Any<CancellationToken>())
            .Returns((LoyaltyMembership?)null);

        var result = await _sut.GetProfileChangeHistoryAsync(customerId, _tenantId, 1, 50);

        Assert.NotNull(result);
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        await _consumerProfile.DidNotReceive().GetProfileChangeHistoryAsync(
            Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
