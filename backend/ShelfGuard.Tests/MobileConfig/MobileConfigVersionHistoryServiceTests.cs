using NSubstitute;
using ShelfGuard.Application.Features.MobileConfig;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.MobileConfig;

/// <summary>
/// TASK-545 — <see cref="MobileConfigVersionHistoryService"/>: pure mapping from
/// <see cref="IMobileConfigurationRepository.GetVersionsForTenantAsync"/> to
/// <c>MobileConfigVersionSummaryDto</c>. Ordering itself is the repository's job (proved at the
/// query level, not re-tested here with a mock) — this suite only proves the service passes the
/// tenant through and maps every field correctly, including nulls.
/// </summary>
public sealed class MobileConfigVersionHistoryServiceTests
{
    private readonly IMobileConfigurationRepository _repo = Substitute.For<IMobileConfigurationRepository>();
    private readonly MobileConfigVersionHistoryService _sut;

    private static readonly Guid TenantId = Guid.NewGuid();

    public MobileConfigVersionHistoryServiceTests()
    {
        _sut = new MobileConfigVersionHistoryService(_repo);
    }

    [Fact]
    public async Task GetHistoryAsync_returns_an_empty_list_when_the_tenant_has_no_versions()
    {
        _repo.GetVersionsForTenantAsync(TenantId, Arg.Any<CancellationToken>())
             .Returns((IReadOnlyList<MobileConfigurationVersion>)[]);

        var result = await _sut.GetHistoryAsync(TenantId);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetHistoryAsync_maps_every_field_including_a_draft_row_with_no_PublishedAt_or_CreatedBy()
    {
        var configId = Guid.NewGuid();
        var draft = MobileConfigurationVersion.Create(
            configId, TenantId, version: 3, schemaVersion: 1, configurationJson: "{}", createdBy: null);
        var published = MobileConfigurationVersion.Create(
            configId, TenantId, version: 2, schemaVersion: 1, configurationJson: "{}", createdBy: Guid.NewGuid());
        published.Publish(DateTime.UtcNow.AddDays(-1));

        _repo.GetVersionsForTenantAsync(TenantId, Arg.Any<CancellationToken>())
             .Returns((IReadOnlyList<MobileConfigurationVersion>)[draft, published]); // repo owns ordering

        var result = await _sut.GetHistoryAsync(TenantId);

        Assert.Equal(2, result.Count);

        var draftDto = result[0];
        Assert.Equal(draft.Id, draftDto.Id);
        Assert.Equal(3, draftDto.Version);
        Assert.Equal("draft", draftDto.Status);
        Assert.Null(draftDto.PublishedAt);
        Assert.Null(draftDto.CreatedBy);

        var publishedDto = result[1];
        Assert.Equal(published.Id, publishedDto.Id);
        Assert.Equal(2, publishedDto.Version);
        Assert.Equal("published", publishedDto.Status);
        Assert.Equal(published.PublishedAt, publishedDto.PublishedAt);
        Assert.Equal(published.CreatedBy, publishedDto.CreatedBy);
    }

    [Fact]
    public async Task GetHistoryAsync_passes_the_tenant_id_through_to_the_repository()
    {
        var otherTenant = Guid.NewGuid();
        _repo.GetVersionsForTenantAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
             .Returns((IReadOnlyList<MobileConfigurationVersion>)[]);

        await _sut.GetHistoryAsync(otherTenant);

        await _repo.Received(1).GetVersionsForTenantAsync(otherTenant, Arg.Any<CancellationToken>());
    }
}
