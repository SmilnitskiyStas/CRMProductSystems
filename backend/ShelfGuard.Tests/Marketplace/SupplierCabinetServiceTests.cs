using NSubstitute;
using ShelfGuard.Application.Features.Marketplace;
using ShelfGuard.Application.Features.Marketplace.Dtos;
using ShelfGuard.Application.Features.Users;
using ShelfGuard.Application.Features.Users.Dtos;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Marketplace;

/// <summary>
/// TASK-284 (ADR-016): cabinet authorization scoping — every operation must be
/// resolved through the calling tenant's owner-managed profile and never accept
/// a foreign supplier id.
/// </summary>
public sealed class SupplierCabinetServiceTests
{
    private readonly IMarketplaceRepository _repo   = Substitute.For<IMarketplaceRepository>();
    private readonly IMarketplaceService _marketplace = Substitute.For<IMarketplaceService>();
    private readonly IUserService _userService = Substitute.For<IUserService>();
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly ISupplierRolesRepository _rolesRepo = Substitute.For<ISupplierRolesRepository>();
    private readonly ISupplierTaskRepository _taskRepo = Substitute.For<ISupplierTaskRepository>();
    private readonly SupplierCabinetService _sut;

    private readonly Guid _tenantId = Guid.NewGuid();

    public SupplierCabinetServiceTests() =>
        _sut = new SupplierCabinetService(_repo, _marketplace, _userService, _userRepo, _rolesRepo, _taskRepo);

    private (SupplierProfile Profile, Supplier Supplier) ArrangeOwnSupplier(bool isPublic = false)
    {
        var supplier = new Supplier { TenantId = _tenantId, Name = "My Supplier" };
        var profile = new SupplierProfile
        {
            SupplierId     = supplier.Id,
            TenantId       = _tenantId,
            IsOwnerManaged = true,
            IsPublic       = isPublic,
        };
        _repo.GetOwnerManagedProfileAsync(_tenantId, Arg.Any<CancellationToken>())
             .Returns((profile, supplier));
        return (profile, supplier);
    }

    private void ArrangeNoCabinet() =>
        _repo.GetOwnerManagedProfileAsync(_tenantId, Arg.Any<CancellationToken>())
             .Returns(((SupplierProfile, Supplier)?)null);

    // ── Resolve guard: no owner-managed profile → cabinet unavailable ─────────

    [Fact]
    public async Task GetProfileAsync_NoOwnerManagedProfile_ReturnsError()
    {
        ArrangeNoCabinet();

        var (profile, error) = await _sut.GetProfileAsync(_tenantId);

        Assert.Null(profile);
        Assert.Equal(SupplierCabinetService.CabinetNotAvailableError, error);
    }

    [Fact]
    public async Task AddItemAsync_NoOwnerManagedProfile_DoesNotTouchMarketplaceService()
    {
        ArrangeNoCabinet();

        var (item, error) = await _sut.AddItemAsync(
            _tenantId, new AdminAddSupplierItemDto("X", null, null, null, true));

        Assert.Null(item);
        Assert.Equal(SupplierCabinetService.CabinetNotAvailableError, error);
        await _marketplace.DidNotReceive().AdminAddSupplierItemAsync(
            Arg.Any<Guid>(), Arg.Any<AdminAddSupplierItemDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteItemAsync_NoOwnerManagedProfile_ReturnsError()
    {
        ArrangeNoCabinet();

        var error = await _sut.DeleteItemAsync(_tenantId, Guid.NewGuid());

        Assert.Equal(SupplierCabinetService.CabinetNotAvailableError, error);
        await _marketplace.DidNotReceive().AdminDeleteSupplierItemAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // ── Items delegate to Admin* methods with the RESOLVED supplier id ────────

    [Fact]
    public async Task AddItemAsync_DelegatesWithResolvedSupplierId()
    {
        var (_, supplier) = ArrangeOwnSupplier();
        var request = new AdminAddSupplierItemDto("Milk", 30m, 5, "pcs", true);
        _marketplace.AdminAddSupplierItemAsync(supplier.Id, request, Arg.Any<CancellationToken>())
                    .Returns((new SupplierItemDto(Guid.NewGuid(), null, "Milk", null, 30m, 5, "pcs", true), (string?)null));

        var (item, error) = await _sut.AddItemAsync(_tenantId, request);

        Assert.Null(error);
        Assert.NotNull(item);
        await _marketplace.Received(1).AdminAddSupplierItemAsync(
            supplier.Id, request, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateItemAsync_DelegatesWithResolvedSupplierId()
    {
        var (_, supplier) = ArrangeOwnSupplier();
        var itemId  = Guid.NewGuid();
        var request = new AdminUpdateSupplierItemDto(null, 42m, null, null, null);
        _marketplace.AdminUpdateSupplierItemAsync(supplier.Id, itemId, request, Arg.Any<CancellationToken>())
                    .Returns((new SupplierItemDto(itemId, null, "Milk", null, 42m, null, null, true), (string?)null));

        var (item, error) = await _sut.UpdateItemAsync(_tenantId, itemId, request);

        Assert.Null(error);
        Assert.NotNull(item);
        await _marketplace.Received(1).AdminUpdateSupplierItemAsync(
            supplier.Id, itemId, request, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteItemAsync_DelegatesWithResolvedSupplierId()
    {
        var (_, supplier) = ArrangeOwnSupplier();
        var itemId = Guid.NewGuid();
        _marketplace.AdminDeleteSupplierItemAsync(supplier.Id, itemId, Arg.Any<CancellationToken>())
                    .Returns((string?)null);

        var error = await _sut.DeleteItemAsync(_tenantId, itemId);

        Assert.Null(error);
        await _marketplace.Received(1).AdminDeleteSupplierItemAsync(
            supplier.Id, itemId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetItemsAsync_ReadsItemsOfResolvedSupplierOnly()
    {
        var (_, supplier) = ArrangeOwnSupplier();
        _repo.GetSupplierItemsForOwnerAsync(supplier.Id, Arg.Any<CancellationToken>())
             .Returns(new List<SupplierItem>
             {
                 new() { SupplierId = supplier.Id, CustomName = "Draft item", IsAvailable = false },
             });

        var (items, error) = await _sut.GetItemsAsync(_tenantId);

        Assert.Null(error);
        var item = Assert.Single(items!);
        Assert.Equal("Draft item", item.CustomName);
        // Cabinet sees unavailable (draft) items too
        Assert.False(item.IsAvailable);
        await _repo.Received(1).GetSupplierItemsForOwnerAsync(supplier.Id, Arg.Any<CancellationToken>());
    }

    // ── Profile update / publish toggle ───────────────────────────────────────

    [Fact]
    public async Task UpdateProfileAsync_PatchesFields_NeverTouchesPublishOrPlan()
    {
        var (profile, _) = ArrangeOwnSupplier(isPublic: false);
        profile.Plan = "free";

        var (dto, error) = await _sut.UpdateProfileAsync(_tenantId, new CabinetProfileUpdateDto(
            Region:          "Kyiv",
            Categories:      ["dairy", "bakery"],
            Website:         null,
            DeliveryRegions: null,
            WorkingHours:    "9-18",
            PaymentTerms:    null));

        Assert.Null(error);
        Assert.NotNull(dto);
        Assert.Equal("Kyiv", profile.Region);
        Assert.Equal("9-18", profile.WorkingHours);
        Assert.False(profile.IsPublic);   // publish is a separate toggle
        Assert.Equal("free", profile.Plan);
        _repo.Received(1).UpdateProfile(profile);
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TogglePublishAsync_FlipsIsPublic()
    {
        var (profile, _) = ArrangeOwnSupplier(isPublic: false);

        var (dto, error) = await _sut.TogglePublishAsync(_tenantId);

        Assert.Null(error);
        Assert.True(profile.IsPublic);
        Assert.True(dto!.IsPublic);
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        // Toggle back
        var (dto2, _) = await _sut.TogglePublishAsync(_tenantId);
        Assert.False(profile.IsPublic);
        Assert.False(dto2!.IsPublic);
    }

    // ── Reviews / metrics read-only ───────────────────────────────────────────

    [Fact]
    public async Task GetReviewsAsync_DelegatesWithResolvedSupplierId()
    {
        var (_, supplier) = ArrangeOwnSupplier();
        var review = new SupplierReview { SupplierId = supplier.Id, Rating = 5, Comment = "Great" };
        _repo.GetReviewsBySupplierAsync(supplier.Id, 1, 20, Arg.Any<CancellationToken>())
             .Returns(new List<(SupplierReview, string)> { (review, "Reviewer Co") });
        _repo.CountReviewsBySupplierAsync(supplier.Id, Arg.Any<CancellationToken>())
             .Returns(1);

        var (reviews, error) = await _sut.GetReviewsAsync(_tenantId, 1, 20);

        Assert.Null(error);
        Assert.NotNull(reviews);
        Assert.Equal(1, reviews!.Total);
        var item = Assert.Single(reviews.Items);
        Assert.Equal("Reviewer Co", item.ReviewerName);
        await _repo.Received(1).GetReviewsBySupplierAsync(supplier.Id, 1, 20, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetReviewsAsync_UnpublishedSupplier_ReturnsEmptyPagedResult_NotNull()
    {
        // Bug fix: cabinet reviews must not go through the public IsPublic gate.
        // An unpublished supplier's own cabinet should see an empty paged result
        // (never null) when it has zero reviews.
        var (_, supplier) = ArrangeOwnSupplier(isPublic: false);
        _repo.GetReviewsBySupplierAsync(supplier.Id, 1, 20, Arg.Any<CancellationToken>())
             .Returns(new List<(SupplierReview, string)>());
        _repo.CountReviewsBySupplierAsync(supplier.Id, Arg.Any<CancellationToken>())
             .Returns(0);

        var (reviews, error) = await _sut.GetReviewsAsync(_tenantId, 1, 20);

        Assert.Null(error);
        Assert.NotNull(reviews);
        Assert.Empty(reviews!.Items);
        Assert.Equal(0, reviews.Total);
        await _marketplace.DidNotReceive().GetSupplierReviewsAsync(
            Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetMetricsAsync_NoMetricsRow_ReturnsEmptyDto()
    {
        var (_, supplier) = ArrangeOwnSupplier();
        _repo.GetMetricsBySupplierIdAsync(supplier.Id, Arg.Any<CancellationToken>())
             .Returns((SupplierMetrics?)null);

        var (metrics, error) = await _sut.GetMetricsAsync(_tenantId);

        Assert.Null(error);
        Assert.NotNull(metrics);
        Assert.Null(metrics!.Rating);
    }

    // ── Lazy backfill (TASK-289): self-heal a supplier tenant with no profile yet ──

    [Fact]
    public async Task GetProfileAsync_SupplierTenantMissingProfile_LazilyCreatesIt()
    {
        ArrangeNoCabinet();
        _repo.GetTenantOnboardingInfoAsync(_tenantId, Arg.Any<CancellationToken>())
             .Returns(("supplier", "Fresh Foods Ltd"));

        Supplier? createdSupplier = null;
        SupplierProfile? createdProfile = null;
        _repo.GetOrCreateOwnerManagedProfileAsync(
                Arg.Any<Supplier>(), Arg.Any<SupplierProfile>(), Arg.Any<CancellationToken>())
             .Returns(ci =>
             {
                 createdSupplier = ci.Arg<Supplier>();
                 createdProfile  = ci.Arg<SupplierProfile>();
                 return (createdProfile, createdSupplier);
             });

        var (profile, error) = await _sut.GetProfileAsync(_tenantId);

        Assert.Null(error);
        Assert.NotNull(profile);
        Assert.NotNull(createdSupplier);
        Assert.Equal(_tenantId, createdSupplier!.TenantId);
        Assert.Equal("Fresh Foods Ltd", createdSupplier.Name);
        Assert.True(createdProfile!.IsOwnerManaged);
        Assert.False(createdProfile.IsPublic);
        await _repo.Received(1).GetOrCreateOwnerManagedProfileAsync(
            Arg.Any<Supplier>(), Arg.Any<SupplierProfile>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetProfileAsync_NonSupplierTenantMissingProfile_DoesNotBackfill()
    {
        ArrangeNoCabinet();
        _repo.GetTenantOnboardingInfoAsync(_tenantId, Arg.Any<CancellationToken>())
             .Returns(("retail", "Some Retail Tenant"));

        var (profile, error) = await _sut.GetProfileAsync(_tenantId);

        Assert.Null(profile);
        Assert.Equal(SupplierCabinetService.CabinetNotAvailableError, error);
        await _repo.DidNotReceive().GetOrCreateOwnerManagedProfileAsync(
            Arg.Any<Supplier>(), Arg.Any<SupplierProfile>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetProfileAsync_ProfileAlreadyExists_DoesNotBackfillAgain()
    {
        // Existing owner-managed profile is found on the first lookup — no create attempt.
        ArrangeOwnSupplier();

        var (profile, error) = await _sut.GetProfileAsync(_tenantId);

        Assert.Null(error);
        Assert.NotNull(profile);
        await _repo.DidNotReceive().GetTenantOnboardingInfoAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().GetOrCreateOwnerManagedProfileAsync(
            Arg.Any<Supplier>(), Arg.Any<SupplierProfile>(), Arg.Any<CancellationToken>());
    }

    // ── Staff management (self-service) ───────────────────────────────────────

    private static UserDto MakeUserDto(Guid? id = null, string role = "supplier_admin") => new(
        id ?? Guid.NewGuid(), "teammate@example.com", "Teammate", null, role,
        null, true, false, DateTime.UtcNow, null);

    [Fact]
    public async Task GetStaffAsync_DelegatesToUserServiceWithTenantId()
    {
        var users = new List<UserDto> { MakeUserDto() };
        _userService.GetAllAsync(_tenantId, null, null, Arg.Any<CancellationToken>()).Returns(users);

        var result = await _sut.GetStaffAsync(_tenantId);

        Assert.Same(users, result);
        await _userService.Received(1).GetAllAsync(_tenantId, null, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InviteStaffAsync_AlwaysForcesSupplierAdminRole()
    {
        var actingUserId = Guid.NewGuid();
        InviteUserRequest? captured = null;
        _userService.InviteAsync(_tenantId, actingUserId, Arg.Do<InviteUserRequest>(r => captured = r), "Owner", Arg.Any<CancellationToken>())
                     .Returns((MakeUserDto(), (string?)null));

        var request = new CabinetInviteStaffDto("new@example.com", "New Teammate", "Password123");
        var (user, error) = await _sut.InviteStaffAsync(_tenantId, actingUserId, request, "Owner");

        Assert.Null(error);
        Assert.NotNull(user);
        Assert.NotNull(captured);
        Assert.Equal("supplier_admin", captured!.Role);
        Assert.Equal("new@example.com", captured.Email);
        Assert.Equal("New Teammate", captured.FullName);
        Assert.Null(captured.StoreId);
    }

    [Fact]
    public async Task InviteStaffAsync_NoSupplierRoleId_KeepsFullAccess_DoesNotTouchUserRepo()
    {
        var actingUserId = Guid.NewGuid();
        _userService.InviteAsync(_tenantId, actingUserId, Arg.Any<InviteUserRequest>(), "Owner", Arg.Any<CancellationToken>())
                     .Returns((MakeUserDto(), (string?)null));

        var request = new CabinetInviteStaffDto("new@example.com", "New Teammate", "Password123");
        var (user, error) = await _sut.InviteStaffAsync(_tenantId, actingUserId, request, "Owner");

        Assert.Null(error);
        Assert.NotNull(user);
        Assert.Null(user!.Permissions);
        await _userRepo.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _userRepo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InviteStaffAsync_UnknownSupplierRoleId_ReturnsError_DoesNotInvite()
    {
        var roleId = Guid.NewGuid();
        _rolesRepo.GetByIdAsync(_tenantId, roleId, Arg.Any<CancellationToken>())
                  .Returns((SupplierRole?)null);

        var request = new CabinetInviteStaffDto("new@example.com", "New Teammate", "Password123", roleId);
        var (user, error) = await _sut.InviteStaffAsync(_tenantId, Guid.NewGuid(), request, "Owner");

        Assert.Null(user);
        Assert.Equal("Role not found.", error);
        await _userService.DidNotReceive().InviteAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<InviteUserRequest>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InviteStaffAsync_WithSupplierRoleId_ResolvesPermissionsAndAssignsRole()
    {
        var actingUserId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var role = SupplierRole.Create(_tenantId, "Support", AppRoles.SupplierAdmin,
            [SupplierPermissions.ClientReviews, SupplierPermissions.TaskBoard]);
        _rolesRepo.GetByIdAsync(_tenantId, roleId, Arg.Any<CancellationToken>())
                  .Returns(role);

        var createdUser = MakeUserDto();
        _userService.InviteAsync(_tenantId, actingUserId, Arg.Any<InviteUserRequest>(), "Owner", Arg.Any<CancellationToken>())
                     .Returns((createdUser, (string?)null));

        var trackedUser = User.Create(_tenantId, "new@example.com", "New Teammate", "hash", "supplier_admin");
        _userRepo.GetByIdAsync(createdUser.Id, Arg.Any<CancellationToken>())
                 .Returns(trackedUser);

        var request = new CabinetInviteStaffDto("new@example.com", "New Teammate", "Password123", roleId);
        var (user, error) = await _sut.InviteStaffAsync(_tenantId, actingUserId, request, "Owner");

        Assert.Null(error);
        Assert.NotNull(user);
        Assert.NotNull(user!.Permissions);
        Assert.True(user.Permissions![SupplierPermissions.ClientReviews]);
        Assert.True(user.Permissions![SupplierPermissions.TaskBoard]);
        Assert.Equal(role.Id, trackedUser.SupplierRoleId);
        _userRepo.Received(1).Update(trackedUser);
        await _userRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeactivateStaffAsync_UserBelongsToOwnTenant_Deactivates()
    {
        var actingUserId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _userService.GetByIdAsync(_tenantId, userId, Arg.Any<CancellationToken>())
                     .Returns((MakeUserDto(userId), (string?)null));
        _userService.DeactivateAsync(_tenantId, actingUserId, userId, Arg.Any<CancellationToken>())
                     .Returns((string?)null);

        var error = await _sut.DeactivateStaffAsync(_tenantId, actingUserId, userId);

        Assert.Null(error);
        await _userService.Received(1).DeactivateAsync(_tenantId, actingUserId, userId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeactivateStaffAsync_UserFromAnotherTenant_ReturnsError_DoesNotDeactivate()
    {
        var actingUserId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        // GetByIdAsync is tenant-scoped in UserService — a foreign user resolves to "not found".
        _userService.GetByIdAsync(_tenantId, userId, Arg.Any<CancellationToken>())
                     .Returns(((UserDto?)null, "User not found."));

        var error = await _sut.DeactivateStaffAsync(_tenantId, actingUserId, userId);

        Assert.Equal("User not found.", error);
        await _userService.DidNotReceive().DeactivateAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // ── ReplyToReviewAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task ReplyToReviewAsync_OwnReview_SetsReplyAndReturnsDto()
    {
        var (_, supplier) = ArrangeOwnSupplier();
        var reviewId = Guid.NewGuid();
        var review = new SupplierReview
        {
            Id = reviewId, SupplierId = supplier.Id, Rating = 5, Comment = "Great",
            Tenant = Tenant.Create("Reviewer Co", "reviewer-co"),
        };
        // TASK-643/KI-036 (W2): the lookup + reply write + flush moved into
        // IMarketplaceRepository.SetReviewReplyAsync so they share one provider-role transaction
        // (the review row belongs to the REVIEWER tenant, not this supplier's). The stub mirrors
        // what the real repository does with the reply text/timestamp it is handed.
        _repo.SetReviewReplyAsync(supplier.Id, reviewId, Arg.Any<string>(),
                Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
             .Returns(ci =>
             {
                 review.ReplyText = ci.ArgAt<string>(2);
                 review.RepliedAt = ci.ArgAt<DateTimeOffset>(3);
                 return review;
             });

        var (dto, error) = await _sut.ReplyToReviewAsync(_tenantId, reviewId, "Thank you!");

        Assert.Null(error);
        Assert.NotNull(dto);
        Assert.Equal("Thank you!", dto!.ReplyText);
        Assert.NotNull(dto.RepliedAt);
        Assert.Equal("Thank you!", review.ReplyText);
        Assert.NotNull(review.RepliedAt);

        // Trimmed reply text, and the supplier id resolved from the caller's own cabinet — never
        // a supplier id taken from the request.
        await _repo.Received(1).SetReviewReplyAsync(
            supplier.Id, reviewId, "Thank you!", Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
        // The service must NOT flush on its own any more — that would run outside the override.
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReplyToReviewAsync_ReviewBelongsToAnotherSupplier_ReturnsNotFound_DoesNotSave()
    {
        var (_, supplier) = ArrangeOwnSupplier();
        var reviewId = Guid.NewGuid();
        // Repo filters on SupplierId inside its own override block: a review of a different
        // supplier resolves to null and is never written to (TASK-643 W2).
        _repo.SetReviewReplyAsync(supplier.Id, reviewId, Arg.Any<string>(),
                Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
             .Returns((SupplierReview?)null);

        var (dto, error) = await _sut.ReplyToReviewAsync(_tenantId, reviewId, "Thanks!");

        Assert.Null(dto);
        Assert.Equal(SupplierCabinetService.ReviewNotFoundError, error);
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReplyToReviewAsync_BlankReplyText_ReturnsValidationError_DoesNotLookUpReview()
    {
        ArrangeOwnSupplier();

        var (dto, error) = await _sut.ReplyToReviewAsync(_tenantId, Guid.NewGuid(), "   ");

        Assert.Null(dto);
        Assert.NotNull(error);
        await _repo.DidNotReceive().SetReviewReplyAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReplyToReviewAsync_NoOwnerManagedProfile_ReturnsCabinetNotAvailable()
    {
        ArrangeNoCabinet();

        var (dto, error) = await _sut.ReplyToReviewAsync(_tenantId, Guid.NewGuid(), "Thanks!");

        Assert.Null(dto);
        Assert.Equal(SupplierCabinetService.CabinetNotAvailableError, error);
    }

    // ── GetReviewStatsAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task GetReviewStatsAsync_BucketsRatingsCorrectly()
    {
        var (_, supplier) = ArrangeOwnSupplier();
        _repo.GetReviewRatingsAsync(supplier.Id, Arg.Any<CancellationToken>())
             .Returns(new List<short> { 5, 4, 3, 2, 1, 5 });

        var (stats, error) = await _sut.GetReviewStatsAsync(_tenantId);

        Assert.Null(error);
        Assert.NotNull(stats);
        Assert.Equal(3, stats!.Positive);  // 5, 4, 5
        Assert.Equal(1, stats.Neutral);    // 3
        Assert.Equal(2, stats.Negative);   // 2, 1
        Assert.Equal(6, stats.Total);
        Assert.Equal(3.33m, stats.AverageRating);
    }

    [Fact]
    public async Task GetReviewStatsAsync_NoReviews_ReturnsAllZeros()
    {
        var (_, supplier) = ArrangeOwnSupplier();
        _repo.GetReviewRatingsAsync(supplier.Id, Arg.Any<CancellationToken>())
             .Returns(new List<short>());

        var (stats, error) = await _sut.GetReviewStatsAsync(_tenantId);

        Assert.Null(error);
        Assert.NotNull(stats);
        Assert.Equal(0, stats!.Positive);
        Assert.Equal(0, stats.Neutral);
        Assert.Equal(0, stats.Negative);
        Assert.Equal(0, stats.Total);
        Assert.Null(stats.AverageRating);
    }

    // ── GetClientsAsync (TASK-313) ────────────────────────────────────────────

    [Fact]
    public async Task GetClientsAsync_MergesReviewAndTaskSources_ByTenant()
    {
        var (_, supplier) = ArrangeOwnSupplier();
        var clientTenantId = Guid.NewGuid(); // reviewed AND has tasks
        var reviewOnlyTenantId = Guid.NewGuid();
        var taskOnlyTenantId = Guid.NewGuid();

        var reviewCreatedAt = DateTimeOffset.UtcNow.AddDays(-2);
        var taskCreatedAt = DateTime.UtcNow.AddDays(-1); // more recent than the review

        _repo.CountReviewsBySupplierAsync(supplier.Id, Arg.Any<CancellationToken>()).Returns(3);
        _repo.GetReviewsBySupplierAsync(supplier.Id, 1, 3, Arg.Any<CancellationToken>())
             .Returns(new List<(SupplierReview, string)>
             {
                 (new SupplierReview { SupplierId = supplier.Id, TenantId = clientTenantId, Rating = 4, CreatedAt = reviewCreatedAt },
                  "Shared Client"),
                 (new SupplierReview { SupplierId = supplier.Id, TenantId = clientTenantId, Rating = 5, CreatedAt = reviewCreatedAt.AddHours(1) },
                  "Shared Client"),
                 (new SupplierReview { SupplierId = supplier.Id, TenantId = reviewOnlyTenantId, Rating = 3, CreatedAt = reviewCreatedAt },
                  "Review Only Co"),
             });

        _taskRepo.GetDistinctClientTenantsAsync(_tenantId, Arg.Any<CancellationToken>())
                 .Returns(new List<(Guid, string?, int, DateTime)>
                 {
                     (clientTenantId, "Shared Client", 2, taskCreatedAt),
                     (taskOnlyTenantId, "Task Only Co", 1, taskCreatedAt),
                 });

        var (clients, error) = await _sut.GetClientsAsync(_tenantId);

        Assert.Null(error);
        Assert.NotNull(clients);
        Assert.Equal(3, clients!.Count);

        var shared = clients.Single(c => c.TenantId == clientTenantId);
        Assert.Equal("Shared Client", shared.TenantName);
        Assert.Equal(2, shared.ReviewCount);
        Assert.Equal(4.5m, shared.AvgRating);
        Assert.Equal(2, shared.TaskCount);
        Assert.Equal(new DateTimeOffset(DateTime.SpecifyKind(taskCreatedAt, DateTimeKind.Utc)), shared.LastInteractionAt);

        var reviewOnly = clients.Single(c => c.TenantId == reviewOnlyTenantId);
        Assert.Equal(1, reviewOnly.ReviewCount);
        Assert.Equal(3m, reviewOnly.AvgRating);
        Assert.Equal(0, reviewOnly.TaskCount);

        var taskOnly = clients.Single(c => c.TenantId == taskOnlyTenantId);
        Assert.Equal(0, taskOnly.ReviewCount);
        Assert.Null(taskOnly.AvgRating);
        Assert.Equal(1, taskOnly.TaskCount);
    }

    [Fact]
    public async Task GetClientsAsync_NoOwnerManagedProfile_ReturnsError()
    {
        ArrangeNoCabinet();

        var (clients, error) = await _sut.GetClientsAsync(_tenantId);

        Assert.Null(clients);
        Assert.Equal(SupplierCabinetService.CabinetNotAvailableError, error);
        await _taskRepo.DidNotReceive().GetDistinctClientTenantsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
