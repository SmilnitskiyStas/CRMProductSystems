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
    private readonly ILocationRepository _locations = Substitute.For<ILocationRepository>();
    private readonly MarketplaceService _sut;

    private readonly Guid _supplierIdA = Guid.NewGuid();
    private readonly Guid _supplierIdB = Guid.NewGuid();
    private readonly Guid _tenantId    = Guid.NewGuid();

    public MarketplaceServiceTests()
    {
        _locations.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<Location>());
        _sut = new MarketplaceService(_repo, _locations);
    }

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

    // TASK-643/KI-036 (W1): the load-or-create + save of supplier_metrics moved out of this
    // service and into IMarketplaceRepository.UpsertMetricsRatingAsync, because the row belongs
    // to the SUPPLIER tenant while the session is the reviewer's — read and write must share one
    // provider-role transaction. The service's remaining job is the average, and passing the
    // supplier's own TenantId through so the INSERT branch can stamp the right owner.
    [Fact]
    public async Task CreateReviewAsync_RecalculatesRating_DelegatesUpsertWithSupplierTenantId()
    {
        var supplier = ArrangeReviewableSupplier();
        _repo.GetReviewRatingsAsync(_supplierIdA, Arg.Any<CancellationToken>())
             .Returns(new List<short> { 4, 5, 3 });

        var (review, error, _) = await _sut.CreateReviewAsync(
            _supplierIdA, _tenantId, new SupplierReviewCreateDto(3, null));

        Assert.Null(error);
        Assert.NotNull(review);
        await _repo.Received(1).UpsertMetricsRatingAsync(
            _supplierIdA, supplier.TenantId, 4.00m, Arg.Any<CancellationToken>());

        // The service must no longer load or stage the metrics row itself — doing so would put a
        // foreign-tenant entity in the shared change tracker outside the override block.
        // (The AddMetricsAsync half of this assertion was dropped in TASK-645: the method is gone
        // from the interface entirely, so asserting it wasn't called is vacuous.)
        await _repo.DidNotReceive().GetMetricsBySupplierIdAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateReviewAsync_RecalculatesRating_AveragesAllRatings()
    {
        var supplier = ArrangeReviewableSupplier();
        _repo.GetReviewRatingsAsync(_supplierIdA, Arg.Any<CancellationToken>())
             .Returns(new List<short> { 5, 4 });

        var (_, error, _) = await _sut.CreateReviewAsync(
            _supplierIdA, _tenantId, new SupplierReviewCreateDto(4, null));

        Assert.Null(error);
        await _repo.Received(1).UpsertMetricsRatingAsync(
            _supplierIdA, supplier.TenantId, 4.50m, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateReviewAsync_NoRatings_SkipsMetricsUpsertEntirely()
    {
        // ArrangeReviewableSupplier stubs GetReviewRatingsAsync to an empty list.
        ArrangeReviewableSupplier();

        var (_, error, _) = await _sut.CreateReviewAsync(
            _supplierIdA, _tenantId, new SupplierReviewCreateDto(4, null));

        Assert.Null(error);
        await _repo.DidNotReceive().UpsertMetricsRatingAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>());
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
    public async Task SearchSuppliersAsync_FiltersByItemNameAndRegionCode()
    {
        var profileA = MakeProfile(_supplierIdA, _tenantId, region: "Київ");
        var supplierA = MakeSupplier(_supplierIdA, "Fresh Farm");

        _repo.SearchSuppliersAsync("Milk", "UA-30", Arg.Any<CancellationToken>())
             .Returns(new List<(SupplierProfile, Supplier, SupplierMetrics?)>
             {
                 (profileA, supplierA, null),
             });

        var results = await _sut.SearchSuppliersAsync(new SupplierSearchDto("Milk", "UA-30"));

        Assert.Single(results);
        Assert.Equal("Fresh Farm", results[0].Name);
    }

    [Fact]
    public async Task SearchSuppliersAsync_NoMatchReturnsEmpty()
    {
        _repo.SearchSuppliersAsync("XYZ_NonExistent", null, Arg.Any<CancellationToken>())
             .Returns(new List<(SupplierProfile, Supplier, SupplierMetrics?)>());

        var results = await _sut.SearchSuppliersAsync(new SupplierSearchDto("XYZ_NonExistent", null));

        Assert.Empty(results);
    }

    // ── TASK-651: region-code normalization before it reaches the repo ────────

    [Fact]
    public async Task SearchSuppliersAsync_NormalizesLegacyFreeTextRegionName_ToCode()
    {
        _repo.SearchSuppliersAsync("Milk", Arg.Any<string?>(), Arg.Any<CancellationToken>())
             .Returns(new List<(SupplierProfile, Supplier, SupplierMetrics?)>());

        await _sut.SearchSuppliersAsync(new SupplierSearchDto("Milk", "Київська область"));

        await _repo.Received(1).SearchSuppliersAsync("Milk", "UA-32", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SearchSuppliersAsync_DropsUnrecognizedRegionValue_PassesNullToRepo()
    {
        _repo.SearchSuppliersAsync("Milk", Arg.Any<string?>(), Arg.Any<CancellationToken>())
             .Returns(new List<(SupplierProfile, Supplier, SupplierMetrics?)>());

        await _sut.SearchSuppliersAsync(new SupplierSearchDto("Milk", "Вся Україна"));

        await _repo.Received(1).SearchSuppliersAsync("Milk", null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetPublicSuppliersAsync_NormalizesRegionCodeForBothQueries()
    {
        _repo.GetPublicSuppliersAsync("UA-46", null, null, 1, 20, Arg.Any<CancellationToken>())
             .Returns(new List<(SupplierProfile, Supplier, SupplierMetrics?)>());
        _repo.CountPublicSuppliersAsync("UA-46", null, null, Arg.Any<CancellationToken>())
             .Returns(0);

        await _sut.GetPublicSuppliersAsync("ua-46", null, null, 1, 20);

        await _repo.Received(1).GetPublicSuppliersAsync("UA-46", null, null, 1, 20, Arg.Any<CancellationToken>());
        await _repo.Received(1).CountPublicSuppliersAsync("UA-46", null, null, Arg.Any<CancellationToken>());
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

    // ── UpdateOwnProfileAsync — delivery coverage (TASK-650) ──────────────────

    [Fact]
    public async Task UpdateOwnProfileAsync_DeliveryCoverage_ValidatesSerializesAndRoundTrips()
    {
        var profile  = MakeProfile(_supplierIdA, _tenantId);
        var supplier = MakeSupplier(_supplierIdA, "My Supplier");
        _repo.GetOwnProfileAsync(_tenantId, Arg.Any<CancellationToken>())
             .Returns((profile, supplier));

        var coverage = new DeliveryCoverageDto(
            new[] { new DeliveryCoverageEntryDto("UA-32", 2, 3, 5000m, "Новою Поштою") },
            new[] { "UA-43" },
            "note");
        var request = new SupplierProfileUpdateDto(
            null, null, null, DeliveryRegions: null, null, null, null, null, DeliveryCoverage: coverage);

        var (result, error) = await _sut.UpdateOwnProfileAsync(_tenantId, request);

        Assert.Null(error);
        Assert.NotNull(result);
        // Legacy column is never written any more.
#pragma warning disable CS0618
        Assert.Null(profile.DeliveryRegions);
#pragma warning restore CS0618
        Assert.NotNull(profile.DeliveryCoverage);
        var stored = DeliveryCoverageJson.Parse(profile.DeliveryCoverage);
        Assert.Equal("UA-32", stored!.Served[0].RegionCode);
        Assert.Equal(2, stored.Served[0].DeliveryDaysMin);
        Assert.Equal(3, stored.Served[0].DeliveryDaysMax);
        Assert.Equal(5000m, stored.Served[0].MinOrderAmount);
        Assert.Equal("Новою Поштою", stored.Served[0].Note);
        Assert.Equal(new[] { "UA-43" }, stored.NotServed);
        Assert.Equal("note", stored.Note);
        // ...and it flows back through the profile DTO unconditionally.
        Assert.NotNull(result!.DeliveryCoverage);
        Assert.Equal("UA-32", result.DeliveryCoverage!.Served[0].RegionCode);
        Assert.Equal(new[] { "UA-43" }, result.DeliveryCoverage.NotServed);
    }

    [Fact]
    public async Task UpdateOwnProfileAsync_InvalidDeliveryCoverage_ReturnsError_DoesNotSave()
    {
        var profile  = MakeProfile(_supplierIdA, _tenantId);
        var supplier = MakeSupplier(_supplierIdA, "My Supplier");
        _repo.GetOwnProfileAsync(_tenantId, Arg.Any<CancellationToken>())
             .Returns((profile, supplier));

        var bad = new DeliveryCoverageDto(
            new[] { new DeliveryCoverageEntryDto("UA-32", null, null, null, null) },
            new[] { "UA-32" },                       // same code in both lists
            null);
        var request = new SupplierProfileUpdateDto(
            null, null, null, null, null, null, null, null, DeliveryCoverage: bad);

        var (result, error) = await _sut.UpdateOwnProfileAsync(_tenantId, request);

        Assert.Null(result);
        Assert.NotNull(error);
        Assert.Contains("UA-32", error);
        _repo.DidNotReceive().UpdateProfile(Arg.Any<SupplierProfile>());
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetSupplierProfileAsync_PopulatesDeliveryCoverage_ForAnonymousCaller()
    {
        var profile = MakeProfile(_supplierIdA, _tenantId, plan: "free");
        profile.DeliveryCoverage =
            """{"served":[{"regionCode":"UA-30","terms":"наступного дня"}],"notServed":["UA-43"],"note":null}""";
        var supplier = MakeSupplier(_supplierIdA, "Free Supplier");

        _repo.GetSupplierByIdAsync(_supplierIdA, Arg.Any<CancellationToken>())
             .Returns((profile, supplier, (SupplierMetrics?)null));

        var result = await _sut.GetSupplierProfileAsync(_supplierIdA, callerIsAuthenticated: false);

        Assert.NotNull(result);
        Assert.Null(result!.Website);                 // premium field still hidden
        Assert.NotNull(result.DeliveryCoverage);      // coverage is NOT premium-gated
        Assert.Equal("UA-30", result.DeliveryCoverage!.Served[0].RegionCode);
        Assert.Equal(new[] { "UA-43" }, result.DeliveryCoverage.NotServed);
    }

    [Fact]
    public async Task GetSupplierProfileAsync_MapsWorkerMetricAggregates()
    {
        var profile = MakeProfile(_supplierIdA, _tenantId, plan: "premium");
        var supplier = MakeSupplier(_supplierIdA, "Metrics Supplier");
        var computedAt = DateTimeOffset.UtcNow.AddHours(-3);
        var metrics = new SupplierMetrics
        {
            SupplierId           = _supplierIdA,
            TenantId             = _tenantId,
            AvgDeliveryDays      = 2.4m,
            DeliverySampleSize   = 17,
            ResponseSampleSize   = 9,
            AggregatesComputedAt = computedAt,
            DeliveryByRegion     =
                """[{"regionCode":"UA-32","avgDeliveryDays":2.4,"sampleSize":12},{"regionCode":"UA-30","avgDeliveryDays":1.1,"sampleSize":5}]""",
        };

        _repo.GetSupplierByIdAsync(_supplierIdA, Arg.Any<CancellationToken>())
             .Returns((profile, supplier, metrics));

        var result = await _sut.GetSupplierProfileAsync(_supplierIdA, callerIsAuthenticated: true);

        Assert.NotNull(result?.Metrics);
        var m = result!.Metrics!;
        Assert.Equal(17, m.DeliverySampleSize);
        Assert.Equal(9, m.ResponseSampleSize);
        Assert.Equal(computedAt, m.AggregatesComputedAt);
        Assert.NotNull(m.DeliveryByRegion);
        Assert.Equal(2, m.DeliveryByRegion!.Count);
        var kyivOblast = m.DeliveryByRegion.Single(r => r.RegionCode == "UA-32");
        Assert.Equal(2.4m, kyivOblast.AvgDeliveryDays);
        Assert.Equal(12, kyivOblast.SampleSize);
    }

    // ── GetSupplierCoverageForBuyerAsync (TASK-651) ──────────────────────────

    private void ArrangeCoverageSupplier(string? coverageJson, string? deliveryByRegionJson = null)
    {
        var profile = MakeProfile(_supplierIdA, _tenantId, isPublic: true);
        profile.DeliveryCoverage = coverageJson;
        var metrics = deliveryByRegionJson is null
            ? null
            : new SupplierMetrics { SupplierId = _supplierIdA, TenantId = _tenantId, DeliveryByRegion = deliveryByRegionJson };
        _repo.GetSupplierByIdAsync(_supplierIdA, Arg.Any<CancellationToken>())
             .Returns((MakeSupplierProfileTuple(profile, metrics)));
    }

    private (SupplierProfile, Supplier, SupplierMetrics?) MakeSupplierProfileTuple(
        SupplierProfile profile, SupplierMetrics? metrics) =>
        (profile, MakeSupplier(_supplierIdA, "Coverage Supplier"), metrics);

    private void ArrangePrimaryLocation(string? regionCode, bool isActive = true, DateTime? createdAt = null)
    {
        _locations.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<Location>
        {
            new() { TenantId = _tenantId, Name = "Магазин", RegionCode = regionCode, IsActive = isActive,
                    CreatedAt = createdAt ?? DateTime.UtcNow },
        });
    }

    [Fact]
    public async Task GetSupplierCoverageForBuyerAsync_MissingOrUnpublished_ReturnsNull()
    {
        _repo.GetSupplierByIdAsync(_supplierIdA, Arg.Any<CancellationToken>())
             .Returns((ValueTuple<SupplierProfile, Supplier, SupplierMetrics?>?)null);
        Assert.Null(await _sut.GetSupplierCoverageForBuyerAsync(_supplierIdA, null, _tenantId));

        var unpublished = MakeProfile(_supplierIdA, _tenantId, isPublic: false);
        _repo.GetSupplierByIdAsync(_supplierIdA, Arg.Any<CancellationToken>())
             .Returns((unpublished, MakeSupplier(_supplierIdA, "Hidden"), (SupplierMetrics?)null));
        Assert.Null(await _sut.GetSupplierCoverageForBuyerAsync(_supplierIdA, null, _tenantId));
    }

    [Fact]
    public async Task GetSupplierCoverageForBuyerAsync_OverrideCode_Served_ReturnsBuyerRegionEntry()
    {
        ArrangeCoverageSupplier(
            """{"served":[{"regionCode":"UA-32","deliveryDaysMin":2,"deliveryDaysMax":3,"minOrderAmount":5000,"note":"Новою Поштою"}],"notServed":["UA-43"],"note":"н"}""");

        var result = await _sut.GetSupplierCoverageForBuyerAsync(_supplierIdA, "UA-32", _tenantId);

        Assert.NotNull(result);
        Assert.Equal("UA-32", result!.BuyerRegionCode);
        Assert.Equal("served", result.BuyerRegionStatus);
        Assert.NotNull(result.BuyerRegionEntry);
        Assert.Equal("UA-32", result.BuyerRegionEntry!.RegionCode);
        Assert.Equal(2, result.BuyerRegionEntry.DeliveryDaysMin);
        Assert.Equal(3, result.BuyerRegionEntry.DeliveryDaysMax);
        Assert.Equal(5000m, result.BuyerRegionEntry.MinOrderAmount);
        Assert.Equal("Новою Поштою", result.BuyerRegionEntry.Note);
        // override wins — the primary-location lookup is never consulted
        await _locations.DidNotReceive().GetAllAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetSupplierCoverageForBuyerAsync_LegacyTermsField_HealsIntoBuyerRegionEntryNote()
    {
        ArrangeCoverageSupplier(
            """{"served":[{"regionCode":"UA-32","terms":"2-3 дні, від 5000 грн"}],"notServed":[],"note":null}""");

        var result = await _sut.GetSupplierCoverageForBuyerAsync(_supplierIdA, "UA-32", _tenantId);

        Assert.Equal("served", result!.BuyerRegionStatus);
        Assert.Equal("2-3 дні, від 5000 грн", result.BuyerRegionEntry!.Note);
    }

    [Fact]
    public async Task GetSupplierCoverageForBuyerAsync_OverrideCode_NotServed()
    {
        ArrangeCoverageSupplier("""{"served":[{"regionCode":"UA-32"}],"notServed":["UA-43"],"note":null}""");

        var result = await _sut.GetSupplierCoverageForBuyerAsync(_supplierIdA, "UA-43", _tenantId);

        Assert.Equal("not_served", result!.BuyerRegionStatus);
        Assert.Null(result.BuyerRegionEntry);
    }

    [Fact]
    public async Task GetSupplierCoverageForBuyerAsync_InvalidOverride_FallsBackToPrimaryLocationRegion()
    {
        ArrangeCoverageSupplier("""{"served":[{"regionCode":"UA-30","deliveryDaysMax":1}],"notServed":[],"note":null}""");
        ArrangePrimaryLocation("UA-30");

        var result = await _sut.GetSupplierCoverageForBuyerAsync(_supplierIdA, "not-a-code", _tenantId);

        Assert.Equal("UA-30", result!.BuyerRegionCode);
        Assert.Equal("served", result.BuyerRegionStatus);
        Assert.Equal(1, result.BuyerRegionEntry!.DeliveryDaysMax);
    }

    [Fact]
    public async Task GetSupplierCoverageForBuyerAsync_PicksOldestActiveLocationWithRegion()
    {
        ArrangeCoverageSupplier("""{"served":[],"notServed":["UA-63"],"note":null}""");
        _locations.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<Location>
        {
            new() { TenantId = _tenantId, Name = "Неактивний", RegionCode = "UA-46", IsActive = false,
                    CreatedAt = new DateTime(2024, 1, 1) },
            new() { TenantId = _tenantId, Name = "Без регіону", RegionCode = null, IsActive = true,
                    CreatedAt = new DateTime(2024, 2, 1) },
            new() { TenantId = _tenantId, Name = "Найстаріший з регіоном", RegionCode = "UA-63", IsActive = true,
                    CreatedAt = new DateTime(2024, 3, 1) },
            new() { TenantId = _tenantId, Name = "Новіший", RegionCode = "UA-30", IsActive = true,
                    CreatedAt = new DateTime(2024, 4, 1) },
        });

        var result = await _sut.GetSupplierCoverageForBuyerAsync(_supplierIdA, null, _tenantId);

        Assert.Equal("UA-63", result!.BuyerRegionCode);
        Assert.Equal("not_served", result.BuyerRegionStatus);
    }

    [Fact]
    public async Task GetSupplierCoverageForBuyerAsync_NoResolvableRegion_StatusUnknown()
    {
        ArrangeCoverageSupplier("""{"served":[{"regionCode":"UA-32","terms":"т"}],"notServed":[],"note":null}""");
        // base ctor stubs GetAllAsync → empty list

        var result = await _sut.GetSupplierCoverageForBuyerAsync(_supplierIdA, null, _tenantId);

        Assert.NotNull(result);
        Assert.Null(result!.BuyerRegionCode);
        Assert.Equal("unknown", result.BuyerRegionStatus);
        Assert.Single(result.Coverage.Served);
    }

    [Fact]
    public async Task GetSupplierCoverageForBuyerAsync_ServedButRegionOutsideBothLists_StatusUnknown()
    {
        ArrangeCoverageSupplier("""{"served":[{"regionCode":"UA-32","terms":"т"}],"notServed":["UA-43"],"note":null}""");

        var result = await _sut.GetSupplierCoverageForBuyerAsync(_supplierIdA, "UA-46", _tenantId);

        Assert.Equal("UA-46", result!.BuyerRegionCode);
        Assert.Equal("unknown", result.BuyerRegionStatus);
    }

    [Fact]
    public async Task GetSupplierCoverageForBuyerAsync_NullCoverage_ReturnsEmptyCoverageDto()
    {
        ArrangeCoverageSupplier(coverageJson: null);
        ArrangePrimaryLocation("UA-30");

        var result = await _sut.GetSupplierCoverageForBuyerAsync(_supplierIdA, null, _tenantId);

        Assert.NotNull(result);
        Assert.Empty(result!.Coverage.Served);
        Assert.Empty(result.Coverage.NotServed);
        Assert.Equal("unknown", result.BuyerRegionStatus);
    }

    [Fact]
    public async Task GetSupplierCoverageForBuyerAsync_MeasuredDaysLookup_MatchesBuyerRegion()
    {
        ArrangeCoverageSupplier(
            """{"served":[{"regionCode":"UA-32","terms":"2 дні"}],"notServed":[],"note":null}""",
            deliveryByRegionJson:
                """[{"regionCode":"UA-30","avgDeliveryDays":1.1,"sampleSize":4},{"regionCode":"UA-32","avgDeliveryDays":2.7,"sampleSize":11}]""");

        var result = await _sut.GetSupplierCoverageForBuyerAsync(_supplierIdA, "UA-32", _tenantId);

        Assert.Equal(2.7m, result!.MeasuredAvgDeliveryDaysToBuyerRegion);
        Assert.Equal(11, result.MeasuredSampleSize);
    }

    [Fact]
    public async Task GetSupplierCoverageForBuyerAsync_MeasuredDaysLookup_NoRowForRegion_ReturnsNulls()
    {
        ArrangeCoverageSupplier(
            """{"served":[{"regionCode":"UA-32","terms":null}],"notServed":[],"note":null}""",
            deliveryByRegionJson: """[{"regionCode":"UA-30","avgDeliveryDays":1.1,"sampleSize":4}]""");

        var result = await _sut.GetSupplierCoverageForBuyerAsync(_supplierIdA, "UA-32", _tenantId);

        Assert.Null(result!.MeasuredAvgDeliveryDaysToBuyerRegion);
        Assert.Null(result.MeasuredSampleSize);
    }


    // ── GetSupplierMetricsHistoryAsync (TASK-671) ───────────────────────────

    private void ArrangeMetricsHistorySupplier(bool isPublic = true)
    {
        _repo.GetSupplierByIdAsync(_supplierIdA, Arg.Any<CancellationToken>())
             .Returns((MakeProfile(_supplierIdA, _tenantId, isPublic: isPublic),
                       MakeSupplier(_supplierIdA, "History Supplier"),
                       (SupplierMetrics?)null));
        _repo.GetMetricsHistoryAsync(_supplierIdA, Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns(new List<SupplierMetricsSnapshot>());
    }

    [Fact]
    public async Task GetSupplierMetricsHistoryAsync_UnknownSupplier_ReturnsNull_DoesNotQueryHistory()
    {
        _repo.GetSupplierByIdAsync(_supplierIdA, Arg.Any<CancellationToken>())
             .Returns((ValueTuple<SupplierProfile, Supplier, SupplierMetrics?>?)null);

        Assert.Null(await _sut.GetSupplierMetricsHistoryAsync(_supplierIdA, 90));
        await _repo.DidNotReceive().GetMetricsHistoryAsync(
            Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetSupplierMetricsHistoryAsync_UnpublishedSupplier_ReturnsNull_DoesNotQueryHistory()
    {
        ArrangeMetricsHistorySupplier(isPublic: false);

        Assert.Null(await _sut.GetSupplierMetricsHistoryAsync(_supplierIdA, 90));
        await _repo.DidNotReceive().GetMetricsHistoryAsync(
            Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0, 7)]
    [InlineData(-5, 7)]
    [InlineData(3, 7)]
    [InlineData(7, 7)]
    [InlineData(30, 30)]
    [InlineData(90, 90)]
    [InlineData(365, 365)]
    [InlineData(999, 365)]
    [InlineData(100_000, 365)]
    public async Task GetSupplierMetricsHistoryAsync_ClampsDaysTo7To365(int requested, int expected)
    {
        ArrangeMetricsHistorySupplier();

        await _sut.GetSupplierMetricsHistoryAsync(_supplierIdA, requested);

        await _repo.Received(1).GetMetricsHistoryAsync(
            _supplierIdA, expected, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetSupplierMetricsHistoryAsync_PublishedNoSnapshots_ReturnsEmptyList()
    {
        ArrangeMetricsHistorySupplier();

        var result = await _sut.GetSupplierMetricsHistoryAsync(_supplierIdA, 90);

        Assert.NotNull(result);
        Assert.Empty(result!);
    }

    [Fact]
    public async Task GetSupplierMetricsHistoryAsync_MapsSnapshotFields_PreservesRepoOrder()
    {
        _repo.GetSupplierByIdAsync(_supplierIdA, Arg.Any<CancellationToken>())
             .Returns((MakeProfile(_supplierIdA, _tenantId, isPublic: true),
                       MakeSupplier(_supplierIdA, "History Supplier"),
                       (SupplierMetrics?)null));

        var d1 = new DateOnly(2026, 8, 1);
        var d2 = new DateOnly(2026, 8, 15);
        _repo.GetMetricsHistoryAsync(_supplierIdA, Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns(new List<SupplierMetricsSnapshot>
             {
                 new()
                 {
                     SupplierId = _supplierIdA, TenantId = _tenantId, SnapshotDate = d1,
                     Rating = 4.50m, AvgDeliveryDays = 2.40m, OrderAccuracy = 0.9800m,
                     QualityScore = null, CancellationRate = 0.0100m, ResponseTimeHours = 5.50m,
                     DeliverySampleSize = 12, ResponseSampleSize = 4,
                 },
                 new()
                 {
                     SupplierId = _supplierIdA, TenantId = _tenantId, SnapshotDate = d2,
                     Rating = 4.60m, AvgDeliveryDays = 2.10m,
                 },
             });

        var result = await _sut.GetSupplierMetricsHistoryAsync(_supplierIdA, 90);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Count);

        Assert.Equal(d1, result[0].Date);
        Assert.Equal(4.50m, result[0].Rating);
        Assert.Equal(2.40m, result[0].AvgDeliveryDays);
        Assert.Equal(0.9800m, result[0].OrderAccuracy);
        Assert.Null(result[0].QualityScore);
        Assert.Equal(0.0100m, result[0].CancellationRate);
        Assert.Equal(5.50m, result[0].ResponseTimeHours);
        Assert.Equal(12, result[0].DeliverySampleSize);
        Assert.Equal(4, result[0].ResponseSampleSize);

        Assert.Equal(d2, result[1].Date);
        Assert.Equal(4.60m, result[1].Rating);
        Assert.Null(result[1].OrderAccuracy);
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

    // ── MaxQty >= MinQty validation (TASK-299) ────────────────────────────────

    [Fact]
    public async Task AdminAddSupplierItemAsync_MaxQtyLessThanMinQty_ReturnsError()
    {
        var supplier = MakeSupplier(_supplierIdA, "Supplier A");
        _repo.GetSupplierByRawIdAsync(_supplierIdA, Arg.Any<CancellationToken>())
             .Returns(supplier);

        var (item, error) = await _sut.AdminAddSupplierItemAsync(
            _supplierIdA,
            new AdminAddSupplierItemDto("Milk 1L", 30m, MinQty: 10, "pcs", true, MaxQty: 5));

        Assert.Null(item);
        Assert.NotNull(error);
        Assert.Contains("MaxQty", error);
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdminAddSupplierItemAsync_MaxQtyGreaterThanOrEqualMinQty_Succeeds()
    {
        var supplier = MakeSupplier(_supplierIdA, "Supplier A");
        _repo.GetSupplierByRawIdAsync(_supplierIdA, Arg.Any<CancellationToken>())
             .Returns(supplier);

        var (item, error) = await _sut.AdminAddSupplierItemAsync(
            _supplierIdA,
            new AdminAddSupplierItemDto("Milk 1L", 30m, MinQty: 10, "pcs", true, MaxQty: 10));

        Assert.Null(error);
        Assert.NotNull(item);
        Assert.Equal(10, item!.MaxQty);
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdminUpdateSupplierItemAsync_MaxQtyLessThanMinQty_ReturnsError()
    {
        var item = new SupplierItem
        {
            SupplierId = _supplierIdA,
            CustomName = "Milk 1L",
            MinQty     = 10,
            IsAvailable = true,
        };
        _repo.GetSupplierItemByIdAsync(_supplierIdA, item.Id, Arg.Any<CancellationToken>())
             .Returns(item);

        var (dto, error) = await _sut.AdminUpdateSupplierItemAsync(
            _supplierIdA, item.Id,
            new AdminUpdateSupplierItemDto(null, null, null, null, null, MaxQty: 5));

        Assert.Null(dto);
        Assert.NotNull(error);
        Assert.Contains("MaxQty", error);
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdminUpdateSupplierItemAsync_MaxQtyAgainstExistingMinQty_UsesEffectiveValues()
    {
        var item = new SupplierItem
        {
            SupplierId = _supplierIdA,
            CustomName = "Milk 1L",
            MinQty     = 10,
            MaxQty     = 20,
            IsAvailable = true,
        };
        _repo.GetSupplierItemByIdAsync(_supplierIdA, item.Id, Arg.Any<CancellationToken>())
             .Returns(item);

        // Patches only MinQty to 25 — effective MaxQty (20, unchanged) is now < MinQty (25)
        var (dto, error) = await _sut.AdminUpdateSupplierItemAsync(
            _supplierIdA, item.Id,
            new AdminUpdateSupplierItemDto(null, null, MinQty: 25, null, null));

        Assert.Null(dto);
        Assert.NotNull(error);
        Assert.Contains("MaxQty", error);
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── Barcodes / Images replacement (TASK-299) ──────────────────────────────

    [Fact]
    public async Task AdminAddSupplierItemAsync_Barcodes_FirstIsPrimaryRestAlternate_SkipsBlankAndDuplicate()
    {
        var supplier = MakeSupplier(_supplierIdA, "Supplier A");
        _repo.GetSupplierByRawIdAsync(_supplierIdA, Arg.Any<CancellationToken>())
             .Returns(supplier);

        var (item, error) = await _sut.AdminAddSupplierItemAsync(
            _supplierIdA,
            new AdminAddSupplierItemDto("Milk 1L", 30m, null, "pcs", true,
                Barcodes: new List<string> { "111", "", "  ", "222", "111" }));

        Assert.Null(error);
        Assert.NotNull(item);
        Assert.Equal(new[] { "111", "222" }, item!.Barcodes);
    }

    [Fact]
    public async Task AdminAddSupplierItemAsync_ImageUrls_FirstIsMainRestGallery_OrderedBySortOrder()
    {
        var supplier = MakeSupplier(_supplierIdA, "Supplier A");
        _repo.GetSupplierByRawIdAsync(_supplierIdA, Arg.Any<CancellationToken>())
             .Returns(supplier);

        var (item, error) = await _sut.AdminAddSupplierItemAsync(
            _supplierIdA,
            new AdminAddSupplierItemDto("Milk 1L", 30m, null, "pcs", true,
                ImageUrls: new List<string> { "https://a/1.jpg", "", "https://a/2.jpg" }));

        Assert.Null(error);
        Assert.NotNull(item);
        Assert.Equal(2, item!.Images.Count);
        Assert.Equal("https://a/1.jpg", item.Images[0].Url);
        Assert.Equal("main", item.Images[0].Kind);
        Assert.Equal("https://a/2.jpg", item.Images[1].Url);
        Assert.Equal("gallery", item.Images[1].Kind);
        Assert.Equal(1, item.Images[1].SortOrder);
    }

    [Fact]
    public async Task AdminUpdateSupplierItemAsync_BarcodesNull_LeavesExistingBarcodesUntouched()
    {
        var item = new SupplierItem { SupplierId = _supplierIdA, CustomName = "Milk 1L", IsAvailable = true };
        item.Barcodes.Add(new SupplierItemBarcode { SupplierItemId = item.Id, Barcode = "999", Kind = "primary" });
        _repo.GetSupplierItemByIdAsync(_supplierIdA, item.Id, Arg.Any<CancellationToken>())
             .Returns(item);

        var (dto, error) = await _sut.AdminUpdateSupplierItemAsync(
            _supplierIdA, item.Id,
            new AdminUpdateSupplierItemDto("Milk 2L", null, null, null, null));

        Assert.Null(error);
        Assert.NotNull(dto);
        Assert.Single(item.Barcodes);
        Assert.Equal("999", item.Barcodes.Single().Barcode);
    }

    [Fact]
    public async Task AdminUpdateSupplierItemAsync_BarcodesProvided_ReplacesExisting()
    {
        var item = new SupplierItem { SupplierId = _supplierIdA, CustomName = "Milk 1L", IsAvailable = true };
        item.Barcodes.Add(new SupplierItemBarcode { SupplierItemId = item.Id, Barcode = "999", Kind = "primary" });
        _repo.GetSupplierItemByIdAsync(_supplierIdA, item.Id, Arg.Any<CancellationToken>())
             .Returns(item);

        var (dto, error) = await _sut.AdminUpdateSupplierItemAsync(
            _supplierIdA, item.Id,
            new AdminUpdateSupplierItemDto(null, null, null, null, null,
                Barcodes: new List<string> { "111", "222" }));

        Assert.Null(error);
        Assert.NotNull(dto);
        Assert.Equal(new[] { "111", "222" }, item.Barcodes.Select(b => b.Barcode));
    }

    /// <summary>
    /// BUG-018 regression: updating an item that starts with ZERO images/barcodes and
    /// supplying a non-empty ImageUrls/Barcodes list crashed in production with
    /// DbUpdateConcurrencyException ("expected to affect 1 row(s), but actually affected
    /// 0 row(s)") because the old code mutated item.Barcodes/item.Images navigation
    /// collections in place on an already-tracked entity, and EF's change tracker
    /// misjudged the new children as pre-existing UPDATE targets rather than INSERTs.
    /// The fix routes replacement through explicit repo.ReplaceItemBarcodes/
    /// ReplaceItemImages (RemoveRange/AddRange) instead. This test asserts both the
    /// resulting DTO state and that the repo replace methods were invoked with an empty
    /// "old" collection and the correct new rows — i.e. no UPDATE-shaped assumption.
    /// </summary>
    [Fact]
    public async Task AdminUpdateSupplierItemAsync_ZeroExistingBarcodesAndImages_PopulatesFirstTimeViaReplace()
    {
        var item = new SupplierItem { SupplierId = _supplierIdA, CustomName = "Milk 1L", IsAvailable = true };
        Assert.Empty(item.Barcodes);
        Assert.Empty(item.Images);
        _repo.GetSupplierItemByIdAsync(_supplierIdA, item.Id, Arg.Any<CancellationToken>())
             .Returns(item);

        var (dto, error) = await _sut.AdminUpdateSupplierItemAsync(
            _supplierIdA, item.Id,
            new AdminUpdateSupplierItemDto(null, null, null, null, null,
                Barcodes: new List<string> { "111", "222" },
                ImageUrls: new List<string> { "https://a/1.jpg", "https://a/2.jpg" }));

        Assert.Null(error);
        Assert.NotNull(dto);
        Assert.Equal(new[] { "111", "222" }, item.Barcodes.Select(b => b.Barcode));
        Assert.Equal(new[] { "https://a/1.jpg", "https://a/2.jpg" }, item.Images.Select(i => i.Url));

        // Old collection passed to the repo must have been empty (nothing to remove),
        // and the new rows must carry the item's Id/TenantId — proving this went through
        // the explicit replace path rather than an in-place navigation-collection mutation.
        _repo.Received(1).ReplaceItemBarcodes(item,
            Arg.Is<IReadOnlyList<SupplierItemBarcode>>(list =>
                list.Count == 2 &&
                list[0].Barcode == "111" && list[0].Kind == "primary" &&
                list[1].Barcode == "222" && list[1].Kind == "alternate" &&
                list.All(b => b.SupplierItemId == item.Id)));

        _repo.Received(1).ReplaceItemImages(item,
            Arg.Is<IReadOnlyList<SupplierItemImage>>(list =>
                list.Count == 2 &&
                list[0].Url == "https://a/1.jpg" && list[0].Kind == "main" &&
                list[1].Url == "https://a/2.jpg" && list[1].Kind == "gallery" &&
                list.All(i => i.SupplierItemId == item.Id)));
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
