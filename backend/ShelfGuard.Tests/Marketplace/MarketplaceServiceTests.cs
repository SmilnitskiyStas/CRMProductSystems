using NSubstitute;
using ShelfGuard.Application.Features.Marketplace;
using ShelfGuard.Application.Features.Marketplace.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Marketplace;

public sealed class MarketplaceServiceTests
{
    private readonly IMarketplaceRepository _repo = Substitute.For<IMarketplaceRepository>();
    private readonly MarketplaceService _sut;

    private readonly Guid _supplierIdA = Guid.NewGuid();
    private readonly Guid _supplierIdB = Guid.NewGuid();
    private readonly Guid _tenantId    = Guid.NewGuid();

    public MarketplaceServiceTests() => _sut = new MarketplaceService(_repo);

    // ── Helper builders ───────────────────────────────────────────────────────

    private static Supplier MakeSupplier(Guid id, string name) =>
        new() { Name = name };

    private SupplierProfile MakeProfile(Guid supplierId, Guid tenantId,
        bool isPublic = true, string plan = "free", string? region = null) =>
        new()
        {
            SupplierId = supplierId,
            TenantId   = tenantId,
            IsPublic   = isPublic,
            Plan       = plan,
            Region     = region,
        };

    // ── GetPublicSuppliersAsync ───────────────────────────────────────────────

    [Fact]
    public async Task GetPublicSuppliersAsync_ReturnsOnlyPublicEntries()
    {
        var profileA = MakeProfile(_supplierIdA, _tenantId, isPublic: true);
        var supplierA = MakeSupplier(_supplierIdA, "Supplier A");

        // Repository is expected to filter is_public=true itself; we simulate it
        // returning only the public supplier
        _repo.GetPublicSuppliersAsync(null, null, null, 1, 20, Arg.Any<CancellationToken>())
             .Returns(new List<(SupplierProfile, Supplier, SupplierMetrics?)>
             {
                 (profileA, supplierA, null),
             });
        _repo.CountPublicSuppliersAsync(null, null, null, Arg.Any<CancellationToken>())
             .Returns(1);

        var result = await _sut.GetPublicSuppliersAsync(null, null, null, 1, 20);

        Assert.Equal(1, result.Total);
        Assert.Single(result.Items);
        Assert.Equal("Supplier A", result.Items[0].Name);
        Assert.True(result.Items[0].IsPublic);
    }

    [Fact]
    public async Task GetPublicSuppliersAsync_EmptyWhenNoPublicEntries()
    {
        _repo.GetPublicSuppliersAsync(null, null, null, 1, 20, Arg.Any<CancellationToken>())
             .Returns(new List<(SupplierProfile, Supplier, SupplierMetrics?)>());
        _repo.CountPublicSuppliersAsync(null, null, null, Arg.Any<CancellationToken>())
             .Returns(0);

        var result = await _sut.GetPublicSuppliersAsync(null, null, null, 1, 20);

        Assert.Equal(0, result.Total);
        Assert.Empty(result.Items);
    }

    /// <summary>
    /// Sets up the repo so that _supplierIdA belongs to another tenant and the
    /// reviewer (_tenantId) is an ordinary client tenant — the happy review path.
    /// </summary>
    private Supplier ArrangeReviewableSupplier(string reviewerBusinessType = "retail")
    {
        var supplier = new Supplier { Id = _supplierIdA, TenantId = Guid.NewGuid(), Name = "Reviewed Supplier" };
        _repo.GetSupplierByRawIdAsync(_supplierIdA, Arg.Any<CancellationToken>())
             .Returns(supplier);
        _repo.GetTenantBusinessTypeAsync(_tenantId, Arg.Any<CancellationToken>())
             .Returns(reviewerBusinessType);
        _repo.GetReviewRatingsAsync(_supplierIdA, Arg.Any<CancellationToken>())
             .Returns(new List<short>());
        return supplier;
    }

    // ── CreateReviewAsync — 409 on duplicate ──────────────────────────────────

    [Fact]
    public async Task CreateReviewAsync_DuplicateReview_Returns409Flag()
    {
        ArrangeReviewableSupplier();
        _repo.ReviewExistsAsync(_supplierIdA, _tenantId, Arg.Any<CancellationToken>())
             .Returns(true);

        var (review, error, isDuplicate) = await _sut.CreateReviewAsync(
            _supplierIdA, _tenantId,
            new SupplierReviewCreateDto(5, "Great!"));

        Assert.Null(review);
        Assert.NotNull(error);
        Assert.True(isDuplicate);
        await _repo.DidNotReceive().AddReviewAsync(Arg.Any<SupplierReview>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateReviewAsync_ValidReview_Saves()
    {
        ArrangeReviewableSupplier();
        _repo.ReviewExistsAsync(_supplierIdA, _tenantId, Arg.Any<CancellationToken>())
             .Returns(false);

        var (review, error, isDuplicate) = await _sut.CreateReviewAsync(
            _supplierIdA, _tenantId,
            new SupplierReviewCreateDto(4, "Good"));

        Assert.Null(error);
        Assert.False(isDuplicate);
        Assert.NotNull(review);
        Assert.Equal(4, review.Rating);
        await _repo.Received(1).AddReviewAsync(
            Arg.Is<SupplierReview>(r => r.SupplierId == _supplierIdA && r.Rating == 4),
            Arg.Any<CancellationToken>());
        await _repo.Received().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── CreateReviewAsync — v4.1 hardening guards (TASK-285) ──────────────────

    [Fact]
    public async Task CreateReviewAsync_SupplierNotFound_ReturnsError()
    {
        _repo.GetSupplierByRawIdAsync(_supplierIdA, Arg.Any<CancellationToken>())
             .Returns((Supplier?)null);

        var (review, error, isDuplicate) = await _sut.CreateReviewAsync(
            _supplierIdA, _tenantId, new SupplierReviewCreateDto(5, null));

        Assert.Null(review);
        Assert.Equal("Supplier not found.", error);
        Assert.False(isDuplicate);
        await _repo.DidNotReceive().AddReviewAsync(Arg.Any<SupplierReview>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateReviewAsync_SelfReview_ReturnsError()
    {
        // Supplier belongs to the SAME tenant as the reviewer
        var supplier = new Supplier { TenantId = _tenantId, Name = "My Own Supplier" };
        _repo.GetSupplierByRawIdAsync(_supplierIdA, Arg.Any<CancellationToken>())
             .Returns(supplier);

        var (review, error, isDuplicate) = await _sut.CreateReviewAsync(
            _supplierIdA, _tenantId, new SupplierReviewCreateDto(5, "Great, me!"));

        Assert.Null(review);
        Assert.NotNull(error);
        Assert.Contains("own supplier", error, StringComparison.OrdinalIgnoreCase);
        Assert.False(isDuplicate);
        await _repo.DidNotReceive().AddReviewAsync(Arg.Any<SupplierReview>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateReviewAsync_SupplierTenantReviewer_ReturnsError()
    {
        ArrangeReviewableSupplier(reviewerBusinessType: "supplier");

        var (review, error, isDuplicate) = await _sut.CreateReviewAsync(
            _supplierIdA, _tenantId, new SupplierReviewCreateDto(1, "Competitor sabotage"));

        Assert.Null(review);
        Assert.NotNull(error);
        Assert.Contains("supplier tenants", error, StringComparison.OrdinalIgnoreCase);
        Assert.False(isDuplicate);
        await _repo.DidNotReceive().AddReviewAsync(Arg.Any<SupplierReview>(), Arg.Any<CancellationToken>());
    }

    // ── CreateReviewAsync — rating recalc (TASK-285) ──────────────────────────

    [Fact]
    public async Task CreateReviewAsync_RecalculatesRating_CreatesMetricsRowWhenAbsent()
    {
        var supplier = ArrangeReviewableSupplier();
        _repo.GetReviewRatingsAsync(_supplierIdA, Arg.Any<CancellationToken>())
             .Returns(new List<short> { 4, 5, 3 });
        _repo.GetMetricsBySupplierIdAsync(_supplierIdA, Arg.Any<CancellationToken>())
             .Returns((SupplierMetrics?)null);

        var (review, error, _) = await _sut.CreateReviewAsync(
            _supplierIdA, _tenantId, new SupplierReviewCreateDto(3, null));

        Assert.Null(error);
        Assert.NotNull(review);
        await _repo.Received(1).AddMetricsAsync(
            Arg.Is<SupplierMetrics>(m =>
                m.SupplierId == _supplierIdA &&
                m.TenantId == supplier.TenantId &&
                m.Rating == 4.00m),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateReviewAsync_RecalculatesRating_UpdatesExistingMetricsRow()
    {
        ArrangeReviewableSupplier();
        _repo.GetReviewRatingsAsync(_supplierIdA, Arg.Any<CancellationToken>())
             .Returns(new List<short> { 5, 4 });

        var metrics = new SupplierMetrics { SupplierId = _supplierIdA, Rating = 1.00m };
        _repo.GetMetricsBySupplierIdAsync(_supplierIdA, Arg.Any<CancellationToken>())
             .Returns(metrics);

        var (_, error, _) = await _sut.CreateReviewAsync(
            _supplierIdA, _tenantId, new SupplierReviewCreateDto(4, null));

        Assert.Null(error);
        Assert.Equal(4.50m, metrics.Rating);
        await _repo.DidNotReceive().AddMetricsAsync(Arg.Any<SupplierMetrics>(), Arg.Any<CancellationToken>());
    }

    // ── GetSupplierReviewsAsync — public reviews (TASK-285) ───────────────────

    [Fact]
    public async Task GetSupplierReviewsAsync_MapsReviewerDisplayName()
    {
        var review = new SupplierReview
        {
            SupplierId = _supplierIdA,
            TenantId   = _tenantId,
            Rating     = 5,
            Comment    = "Top!",
        };
        _repo.GetSupplierByIdAsync(_supplierIdA, Arg.Any<CancellationToken>())
             .Returns((MakeProfile(_supplierIdA, _tenantId, isPublic: true),
                       MakeSupplier(_supplierIdA, "Supplier A"),
                       (SupplierMetrics?)null));
        _repo.GetReviewsBySupplierAsync(_supplierIdA, 1, 20, Arg.Any<CancellationToken>())
             .Returns(new List<(SupplierReview, string)> { (review, "Client Shop") });
        _repo.CountReviewsBySupplierAsync(_supplierIdA, Arg.Any<CancellationToken>())
             .Returns(1);

        var result = await _sut.GetSupplierReviewsAsync(_supplierIdA, 1, 20);

        Assert.NotNull(result);
        Assert.Equal(1, result!.Total);
        var dto = Assert.Single(result.Items);
        Assert.Equal(5, dto.Rating);
        Assert.Equal("Top!", dto.Comment);
        Assert.Equal("Client Shop", dto.ReviewerName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public async Task CreateReviewAsync_InvalidRating_ReturnsValidationError(int rating)
    {
        var (review, error, isDuplicate) = await _sut.CreateReviewAsync(
            _supplierIdA, _tenantId,
            new SupplierReviewCreateDto(rating, null));

        Assert.Null(review);
        Assert.NotNull(error);
        Assert.False(isDuplicate);
        await _repo.DidNotReceive().AddReviewAsync(Arg.Any<SupplierReview>(), Arg.Any<CancellationToken>());
    }

    // ── SearchSuppliersAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task SearchSuppliersAsync_FiltersByItemNameAndRegion()
    {
        var profileA = MakeProfile(_supplierIdA, _tenantId, region: "Kyiv");
        var supplierA = MakeSupplier(_supplierIdA, "Fresh Farm");

        _repo.SearchSuppliersAsync("Milk", "Kyiv", Arg.Any<CancellationToken>())
             .Returns(new List<(SupplierProfile, Supplier, SupplierMetrics?)>
             {
                 (profileA, supplierA, null),
             });

        var results = await _sut.SearchSuppliersAsync(new SupplierSearchDto("Milk", "Kyiv"));

        Assert.Single(results);
        Assert.Equal("Fresh Farm", results[0].Name);
        Assert.Equal("Kyiv", results[0].Region);
    }

    [Fact]
    public async Task SearchSuppliersAsync_NoMatchReturnsEmpty()
    {
        _repo.SearchSuppliersAsync("XYZ_NonExistent", null, Arg.Any<CancellationToken>())
             .Returns(new List<(SupplierProfile, Supplier, SupplierMetrics?)>());

        var results = await _sut.SearchSuppliersAsync(new SupplierSearchDto("XYZ_NonExistent", null));

        Assert.Empty(results);
    }

    // ── UpdateOwnProfileAsync — patch semantics ───────────────────────────────

    [Fact]
    public async Task UpdateOwnProfileAsync_UpdatesOnlyProvidedFields()
    {
        var profile  = MakeProfile(_supplierIdA, _tenantId, plan: "free", region: "Lviv");
        var supplier = MakeSupplier(_supplierIdA, "My Supplier");

        _repo.GetOwnProfileAsync(_tenantId, Arg.Any<CancellationToken>())
             .Returns((profile, supplier));

        var request = new SupplierProfileUpdateDto(
            Region: "Kyiv",        // should update
            Categories: null,      // not provided — should stay unchanged
            Website: null,
            DeliveryRegions: null,
            WorkingHours: null,
            PaymentTerms: null,
            IsPublic: true,        // should update
            Plan: null             // not provided — should stay "free"
        );

        var (result, error) = await _sut.UpdateOwnProfileAsync(_tenantId, request);

        Assert.Null(error);
        Assert.NotNull(result);
        // Region updated
        Assert.Equal("Kyiv", profile.Region);
        // Plan unchanged (not provided)
        Assert.Equal("free", profile.Plan);
        // IsPublic updated
        Assert.True(profile.IsPublic);

        _repo.Received(1).UpdateProfile(profile);
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateOwnProfileAsync_InvalidPlan_ReturnsError()
    {
        var request = new SupplierProfileUpdateDto(null, null, null, null, null, null, null, "enterprise");

        var (result, error) = await _sut.UpdateOwnProfileAsync(_tenantId, request);

        Assert.Null(result);
        Assert.NotNull(error);
        Assert.Contains("free", error, StringComparison.OrdinalIgnoreCase);
        _repo.DidNotReceive().UpdateProfile(Arg.Any<SupplierProfile>());
    }

    [Fact]
    public async Task UpdateOwnProfileAsync_NotFound_ReturnsError()
    {
        _repo.GetOwnProfileAsync(_tenantId, Arg.Any<CancellationToken>())
             .Returns((ValueTuple<SupplierProfile?, Supplier?>?)null);

        var request = new SupplierProfileUpdateDto(null, null, null, null, null, null, null, null);

        var (result, error) = await _sut.UpdateOwnProfileAsync(_tenantId, request);

        Assert.Null(result);
        Assert.NotNull(error);
        Assert.Contains("not found", error, StringComparison.OrdinalIgnoreCase);
    }

    // ── AdminCreateSupplierAsync — platform tenant FK fix (BUG-012) ───────────

    [Fact]
    public async Task AdminCreateSupplierAsync_UsesPlatformTenant_NotGuidEmpty()
    {
        var platformTenantId = Guid.NewGuid();
        _repo.GetOrCreatePlatformTenantIdAsync(Arg.Any<CancellationToken>())
             .Returns(platformTenantId);

        var (dto, error) = await _sut.AdminCreateSupplierAsync(
            new AdminCreateSupplierDto("Platform Supplier", "Kyiv", null, null, null, null, null, false, "free"));

        Assert.Null(error);
        Assert.NotNull(dto);
        await _repo.Received(1).GetOrCreatePlatformTenantIdAsync(Arg.Any<CancellationToken>());
        await _repo.Received(1).AddSupplierAsync(
            Arg.Is<Supplier>(s => s.TenantId == platformTenantId),
            Arg.Any<CancellationToken>());
        // Profile also bound to the platform tenant and NOT owner-managed —
        // the supplier cabinet (IsOwnerManaged filter) must never resolve it.
        await _repo.Received(1).AddSupplierProfileAsync(
            Arg.Is<SupplierProfile>(p => p.TenantId == platformTenantId && !p.IsOwnerManaged),
            Arg.Any<CancellationToken>());
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdminCreateSupplierAsync_InvalidInput_DoesNotTouchTenantOrSave()
    {
        var (_, missingName) = await _sut.AdminCreateSupplierAsync(
            new AdminCreateSupplierDto("  ", null, null, null, null, null, null, false, "free"));
        var (_, badPlan) = await _sut.AdminCreateSupplierAsync(
            new AdminCreateSupplierDto("Supplier", null, null, null, null, null, null, false, "enterprise"));

        Assert.NotNull(missingName);
        Assert.NotNull(badPlan);
        await _repo.DidNotReceive().GetOrCreatePlatformTenantIdAsync(Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── AdminUpdateSupplierItemAsync (TASK-284) ───────────────────────────────

    [Fact]
    public async Task AdminUpdateSupplierItemAsync_PatchesOnlyProvidedFields()
    {
        var item = new SupplierItem
        {
            SupplierId  = _supplierIdA,
            CustomName  = "Milk 1L",
            Price       = 30m,
            MinQty      = 10,
            Unit        = "pcs",
            IsAvailable = true,
        };
        _repo.GetSupplierItemByIdAsync(_supplierIdA, item.Id, Arg.Any<CancellationToken>())
             .Returns(item);

        var (dto, error) = await _sut.AdminUpdateSupplierItemAsync(
            _supplierIdA, item.Id,
            new AdminUpdateSupplierItemDto(null, 35m, null, null, false));

        Assert.Null(error);
        Assert.NotNull(dto);
        Assert.Equal("Milk 1L", item.CustomName);   // unchanged
        Assert.Equal(35m, item.Price);              // updated
        Assert.Equal(10, item.MinQty);              // unchanged
        Assert.False(item.IsAvailable);             // updated
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdminUpdateSupplierItemAsync_WrongSupplierScope_ReturnsNotFound()
    {
        // Repo scopes by (supplierId, itemId) — item of another supplier resolves to null
        _repo.GetSupplierItemByIdAsync(_supplierIdB, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
             .Returns((SupplierItem?)null);

        var (dto, error) = await _sut.AdminUpdateSupplierItemAsync(
            _supplierIdB, Guid.NewGuid(),
            new AdminUpdateSupplierItemDto("Hijack", null, null, null, null));

        Assert.Null(dto);
        Assert.Equal("Item not found.", error);
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── GetSupplierProfileAsync — premium field gating ────────────────────────

    [Fact]
    public async Task GetSupplierProfileAsync_FreePlan_HidesPremiumFieldsWhenNotAuthenticated()
    {
        var profile = MakeProfile(_supplierIdA, _tenantId, plan: "free");
        profile.Website = "https://example.com";
        profile.WorkingHours = "9-18";
        var supplier = MakeSupplier(_supplierIdA, "Free Supplier");

        _repo.GetSupplierByIdAsync(_supplierIdA, Arg.Any<CancellationToken>())
             .Returns((profile, supplier, (SupplierMetrics?)null));

        var result = await _sut.GetSupplierProfileAsync(_supplierIdA, callerIsAuthenticated: false);

        Assert.NotNull(result);
        Assert.Null(result!.Website);
        Assert.Null(result.WorkingHours);
    }

    [Fact]
    public async Task GetSupplierProfileAsync_FreePlan_ShowsPremiumFieldsWhenAuthenticated()
    {
        var profile = MakeProfile(_supplierIdA, _tenantId, plan: "free");
        profile.Website = "https://example.com";
        var supplier = MakeSupplier(_supplierIdA, "Free Supplier");

        _repo.GetSupplierByIdAsync(_supplierIdA, Arg.Any<CancellationToken>())
             .Returns((profile, supplier, (SupplierMetrics?)null));

        var result = await _sut.GetSupplierProfileAsync(_supplierIdA, callerIsAuthenticated: true);

        Assert.NotNull(result);
        Assert.Equal("https://example.com", result!.Website);
    }

    // ── GetSupplierProfileAsync — unpublished profile leak (BUG-010) ─────────

    [Fact]
    public async Task GetSupplierProfileAsync_Unpublished_ReturnsNull()
    {
        var profile = MakeProfile(_supplierIdA, _tenantId, isPublic: false);
        var supplier = MakeSupplier(_supplierIdA, "Hidden Supplier");

        _repo.GetSupplierByIdAsync(_supplierIdA, Arg.Any<CancellationToken>())
             .Returns((profile, supplier, (SupplierMetrics?)null));

        // Both anonymous and authenticated tenant callers get 404-equivalent null
        Assert.Null(await _sut.GetSupplierProfileAsync(_supplierIdA, callerIsAuthenticated: false));
        Assert.Null(await _sut.GetSupplierProfileAsync(_supplierIdA, callerIsAuthenticated: true));
    }

    [Fact]
    public async Task GetSupplierProfileAsync_Published_ReturnsProfile()
    {
        var profile = MakeProfile(_supplierIdA, _tenantId, isPublic: true);
        var supplier = MakeSupplier(_supplierIdA, "Visible Supplier");

        _repo.GetSupplierByIdAsync(_supplierIdA, Arg.Any<CancellationToken>())
             .Returns((profile, supplier, (SupplierMetrics?)null));

        var result = await _sut.GetSupplierProfileAsync(_supplierIdA, callerIsAuthenticated: true);

        Assert.NotNull(result);
        Assert.True(result!.IsPublic);
    }

    [Fact]
    public async Task GetSupplierProfileAsync_NotFound_ReturnsNull()
    {
        _repo.GetSupplierByIdAsync(Guid.NewGuid(), Arg.Any<CancellationToken>())
             .Returns((ValueTuple<SupplierProfile, Supplier, SupplierMetrics?>?)null);

        var result = await _sut.GetSupplierProfileAsync(Guid.NewGuid(), false);

        Assert.Null(result);
    }

    // ── Items/reviews of unpublished supplier must be hidden too (BUG-010) ───

    [Fact]
    public async Task GetSupplierItemsAsync_Unpublished_ReturnsNull()
    {
        var profile = MakeProfile(_supplierIdA, _tenantId, isPublic: false);
        var supplier = MakeSupplier(_supplierIdA, "Hidden Supplier");

        _repo.GetSupplierByIdAsync(_supplierIdA, Arg.Any<CancellationToken>())
             .Returns((profile, supplier, (SupplierMetrics?)null));

        Assert.Null(await _sut.GetSupplierItemsAsync(_supplierIdA));
        await _repo.DidNotReceive().GetSupplierItemsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetSupplierItemsAsync_Published_ReturnsItems()
    {
        var profile = MakeProfile(_supplierIdA, _tenantId, isPublic: true);
        var supplier = MakeSupplier(_supplierIdA, "Visible Supplier");

        _repo.GetSupplierByIdAsync(_supplierIdA, Arg.Any<CancellationToken>())
             .Returns((profile, supplier, (SupplierMetrics?)null));
        _repo.GetSupplierItemsAsync(_supplierIdA, Arg.Any<CancellationToken>())
             .Returns(new List<SupplierItem>());

        Assert.NotNull(await _sut.GetSupplierItemsAsync(_supplierIdA));
    }

    [Fact]
    public async Task GetSupplierReviewsAsync_Unpublished_ReturnsNull()
    {
        var profile = MakeProfile(_supplierIdA, _tenantId, isPublic: false);
        var supplier = MakeSupplier(_supplierIdA, "Hidden Supplier");

        _repo.GetSupplierByIdAsync(_supplierIdA, Arg.Any<CancellationToken>())
             .Returns((profile, supplier, (SupplierMetrics?)null));

        Assert.Null(await _sut.GetSupplierReviewsAsync(_supplierIdA, page: 1, pageSize: 20));
        await _repo.DidNotReceive().GetReviewsBySupplierAsync(
            Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetSupplierReviewsAsync_Published_ReturnsPage()
    {
        var profile = MakeProfile(_supplierIdA, _tenantId, isPublic: true);
        var supplier = MakeSupplier(_supplierIdA, "Visible Supplier");

        _repo.GetSupplierByIdAsync(_supplierIdA, Arg.Any<CancellationToken>())
             .Returns((profile, supplier, (SupplierMetrics?)null));
        _repo.GetReviewsBySupplierAsync(_supplierIdA, 1, 20, Arg.Any<CancellationToken>())
             .Returns(new List<(SupplierReview Review, string ReviewerName)>());
        _repo.CountReviewsBySupplierAsync(_supplierIdA, Arg.Any<CancellationToken>())
             .Returns(0);

        Assert.NotNull(await _sut.GetSupplierReviewsAsync(_supplierIdA, page: 1, pageSize: 20));
    }

    // ── Category/attributes validation (TASK-295, ADR-017 §5) ─────────────────

    [Fact]
    public async Task AdminAddSupplierItemAsync_CategoryMedical_MissingExpiryDate_ReturnsError()
    {
        var supplier = MakeSupplier(_supplierIdA, "Supplier A");
        _repo.GetSupplierByRawIdAsync(_supplierIdA, Arg.Any<CancellationToken>())
             .Returns(supplier);

        var (item, error) = await _sut.AdminAddSupplierItemAsync(
            _supplierIdA,
            new AdminAddSupplierItemDto(
                "Paracetamol", 50m, 1, "box", true,
                Category: "medical",
                Attributes: new Dictionary<string, object?> { ["dosage"] = "500 мг" }));

        Assert.Null(item);
        Assert.NotNull(error);
        Assert.Contains("Термін придатності", error);
        Assert.Contains("Рецептурний статус", error);
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdminAddSupplierItemAsync_CategoryMedical_AllRequiredFieldsPresent_Succeeds()
    {
        var supplier = MakeSupplier(_supplierIdA, "Supplier A");
        _repo.GetSupplierByRawIdAsync(_supplierIdA, Arg.Any<CancellationToken>())
             .Returns(supplier);

        var (item, error) = await _sut.AdminAddSupplierItemAsync(
            _supplierIdA,
            new AdminAddSupplierItemDto(
                "Paracetamol", 50m, 1, "box", true,
                Category: "medical",
                Attributes: new Dictionary<string, object?>
                {
                    ["dosage"] = "500 мг",
                    ["expiry_date"] = "2027-01-01",
                    ["prescription_status"] = "ОТС",
                }));

        Assert.Null(error);
        Assert.NotNull(item);
        Assert.Equal("medical", item!.Category);
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdminAddSupplierItemAsync_NoCategory_SucceedsRegardlessOfAttributes()
    {
        var supplier = MakeSupplier(_supplierIdA, "Supplier A");
        _repo.GetSupplierByRawIdAsync(_supplierIdA, Arg.Any<CancellationToken>())
             .Returns(supplier);

        var (item, error) = await _sut.AdminAddSupplierItemAsync(
            _supplierIdA,
            new AdminAddSupplierItemDto("Milk 1L", 30m, 10, "pcs", true));

        Assert.Null(error);
        Assert.NotNull(item);
        Assert.Null(item!.Category);
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdminUpdateSupplierItemAsync_CategoryMedical_MissingExpiryDate_ReturnsError()
    {
        var item = new SupplierItem
        {
            SupplierId = _supplierIdA,
            CustomName = "Ibuprofen",
            IsAvailable = true,
        };
        _repo.GetSupplierItemByIdAsync(_supplierIdA, item.Id, Arg.Any<CancellationToken>())
             .Returns(item);

        var (dto, error) = await _sut.AdminUpdateSupplierItemAsync(
            _supplierIdA, item.Id,
            new AdminUpdateSupplierItemDto(
                null, null, null, null, null,
                Category: "medical",
                Attributes: new Dictionary<string, object?> { ["dosage"] = "200 мг" }));

        Assert.Null(dto);
        Assert.NotNull(error);
        Assert.Contains("Термін придатності", error);
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdminUpdateSupplierItemAsync_CategoryMedical_AllRequiredFieldsPresent_Succeeds()
    {
        var item = new SupplierItem
        {
            SupplierId = _supplierIdA,
            CustomName = "Ibuprofen",
            IsAvailable = true,
        };
        _repo.GetSupplierItemByIdAsync(_supplierIdA, item.Id, Arg.Any<CancellationToken>())
             .Returns(item);

        var (dto, error) = await _sut.AdminUpdateSupplierItemAsync(
            _supplierIdA, item.Id,
            new AdminUpdateSupplierItemDto(
                null, null, null, null, null,
                Category: "medical",
                Attributes: new Dictionary<string, object?>
                {
                    ["dosage"] = "200 мг",
                    ["expiry_date"] = "2027-06-01",
                    ["prescription_status"] = "рецептурний",
                }));

        Assert.Null(error);
        Assert.NotNull(dto);
        Assert.Equal("medical", item.Category);
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdminUpdateSupplierItemAsync_NoCategory_SucceedsRegardlessOfAttributes()
    {
        var item = new SupplierItem
        {
            SupplierId = _supplierIdA,
            CustomName = "Milk 1L",
            IsAvailable = true,
        };
        _repo.GetSupplierItemByIdAsync(_supplierIdA, item.Id, Arg.Any<CancellationToken>())
             .Returns(item);

        var (dto, error) = await _sut.AdminUpdateSupplierItemAsync(
            _supplierIdA, item.Id,
            new AdminUpdateSupplierItemDto("Milk 2L", null, null, null, null));

        Assert.Null(error);
        Assert.NotNull(dto);
        Assert.Null(item.Category);
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── GetItemCategories (TASK-294) ───────────────────────────────────────────

    [Fact]
    public void GetItemCategories_ReturnsAllFourCategoriesWithFieldCounts()
    {
        var categories = _sut.GetItemCategories();

        Assert.Equal(4, categories.Count);

        var food = categories.Single(c => c.Key == "food");
        Assert.Equal(3, food.Fields.Count);
        Assert.Contains(food.Fields, f => f.Key == "expiry_date" && f.Required);

        var medical = categories.Single(c => c.Key == "medical");
        Assert.Equal(4, medical.Fields.Count);
        var prescriptionField = medical.Fields.Single(f => f.Key == "prescription_status");
        Assert.Equal("select", prescriptionField.Type);
        Assert.NotNull(prescriptionField.Options);
        Assert.Contains("ОТС", prescriptionField.Options!);

        var autoParts = categories.Single(c => c.Key == "auto_parts");
        Assert.Equal(3, autoParts.Fields.Count);

        var construction = categories.Single(c => c.Key == "construction");
        Assert.Equal(3, construction.Fields.Count);
    }
}
