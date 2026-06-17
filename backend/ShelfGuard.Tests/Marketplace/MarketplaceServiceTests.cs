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

    // ── CreateReviewAsync — 409 on duplicate ──────────────────────────────────

    [Fact]
    public async Task CreateReviewAsync_DuplicateReview_Returns409Flag()
    {
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
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
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

    [Fact]
    public async Task GetSupplierProfileAsync_NotFound_ReturnsNull()
    {
        _repo.GetSupplierByIdAsync(Guid.NewGuid(), Arg.Any<CancellationToken>())
             .Returns((ValueTuple<SupplierProfile, Supplier, SupplierMetrics?>?)null);

        var result = await _sut.GetSupplierProfileAsync(Guid.NewGuid(), false);

        Assert.Null(result);
    }
}
