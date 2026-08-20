using NSubstitute;
using ShelfGuard.Application.Features.ConsumerContent;
using ShelfGuard.Application.Features.ConsumerContent.Dtos;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.ConsumerContent;

/// <summary>
/// TASK-572 — <see cref="ConsumerContentService.GetCatalogByIdsAsync"/>: resolves a curated
/// productIds selection (ADR-032) regardless of alphabetical position. Repository-level filtering
/// (IsActive, availability join) is covered separately in
/// <c>ConsumerContentRepositoryGetCatalogByIdsTests</c> — this file only covers what the service
/// itself adds: tenant existence, the empty-ids short-circuit, and the defense-in-depth &gt;30 clamp.
/// </summary>
public sealed class ConsumerContentServiceTests
{
    private readonly IConsumerContentRepository _repo = Substitute.For<IConsumerContentRepository>();
    private readonly ITenantRepository _tenants = Substitute.For<ITenantRepository>();
    private readonly ITenantSessionOverride _tenantScope = Substitute.For<ITenantSessionOverride>();
    private readonly ConsumerContentService _sut;

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _storeId = Guid.NewGuid();

    public ConsumerContentServiceTests()
    {
        _sut = new ConsumerContentService(_repo, _tenants, _tenantScope);

        // Same pure pass-through convention LoyaltyServiceTests already uses for
        // ITenantSessionOverride — invokes the delegate immediately instead of opening a real
        // transaction.
        _tenantScope.ExecuteAsync(
                Arg.Any<Guid>(), Arg.Any<Func<Task<IReadOnlyList<ConsumerCatalogItemDto>>>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<Func<Task<IReadOnlyList<ConsumerCatalogItemDto>>>>()());
    }

    [Fact]
    public async Task GetCatalogByIdsAsync_TenantNotFound_ReturnsError()
    {
        _tenants.GetByIdAsync(_tenantId, Arg.Any<CancellationToken>()).Returns((Tenant?)null);

        var (items, error) = await _sut.GetCatalogByIdsAsync(_tenantId, _storeId, [Guid.NewGuid()]);

        Assert.Null(items);
        Assert.Equal("Tenant not found.", error);
    }

    [Fact]
    public async Task GetCatalogByIdsAsync_EmptyIds_ReturnsEmptyArrayNotError()
    {
        _tenants.GetByIdAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(Tenant.Create("Test", "test"));

        var (items, error) = await _sut.GetCatalogByIdsAsync(_tenantId, _storeId, []);

        Assert.Null(error);
        Assert.NotNull(items);
        Assert.Empty(items);
        await _repo.DidNotReceive().GetCatalogByIdsAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetCatalogByIdsAsync_MoreThan30Ids_TruncatesToFirst30BeforeCallingRepo()
    {
        _tenants.GetByIdAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(Tenant.Create("Test", "test"));
        var ids = Enumerable.Range(0, 45).Select(_ => Guid.NewGuid()).ToList();
        _repo.GetCatalogByIdsAsync(_tenantId, _storeId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<ConsumerCatalogItemDto>)[]);

        await _sut.GetCatalogByIdsAsync(_tenantId, _storeId, ids);

        await _repo.Received(1).GetCatalogByIdsAsync(
            _tenantId, _storeId,
            Arg.Is<IReadOnlyList<Guid>>(l => l.Count == 30 && l.SequenceEqual(ids.Take(30))),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetCatalogByIdsAsync_ValidIds_ReturnsRepoResult()
    {
        _tenants.GetByIdAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(Tenant.Create("Test", "test"));
        var id = Guid.NewGuid();
        var dto = new ConsumerCatalogItemDto(id, "Молоко", null, "шт", 45m, null, null, true);
        _repo.GetCatalogByIdsAsync(_tenantId, _storeId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<ConsumerCatalogItemDto>)[dto]);

        var (items, error) = await _sut.GetCatalogByIdsAsync(_tenantId, _storeId, [id]);

        Assert.Null(error);
        Assert.Single(items!);
        Assert.Equal(id, items![0].Id);
    }
}
