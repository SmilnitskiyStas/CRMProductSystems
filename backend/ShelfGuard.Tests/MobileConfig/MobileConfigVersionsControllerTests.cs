using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using ShelfGuard.Api.Controllers;
using ShelfGuard.Application.Features.MobileConfig;
using ShelfGuard.Application.Features.MobileConfig.Dtos;
using ShelfGuard.Application.Services;
using Xunit;

namespace ShelfGuard.Tests.MobileConfig;

/// <summary>
/// TASK-545 — HTTP layer wrapping <see cref="IMobileConfigVersionHistoryService"/> (history list)
/// and <see cref="IMobileConfigPublishService.RollbackAsync"/> (rollback trigger): tenant resolution
/// via <see cref="ITenantContext"/>, the 403-when-no-tenant case, and the rollback
/// error-type-to-status-code mapping (400/404/409). Orchestration itself is covered by
/// <c>MobileConfigVersionHistoryServiceTests</c>/<c>MobileConfigPublishServiceTests</c> — this suite
/// only covers what the controller adds, same split <c>MobileConfigPublishControllerTests</c>
/// already established for the sibling publish controller.
/// </summary>
public sealed class MobileConfigVersionsControllerTests
{
    private readonly IMobileConfigVersionHistoryService _history = Substitute.For<IMobileConfigVersionHistoryService>();
    private readonly IMobileConfigPublishService _publish = Substitute.For<IMobileConfigPublishService>();
    private readonly ITenantContext _tenantContext = Substitute.For<ITenantContext>();
    private readonly MobileConfigVersionsController _controller;

    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid VersionId = Guid.NewGuid();

    public MobileConfigVersionsControllerTests()
    {
        _controller = new MobileConfigVersionsController(_history, _publish, _tenantContext)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    private static MobileConfigPublishedDto SamplePublished() => new(
        MobileConfigurationId: Guid.NewGuid(),
        VersionId: Guid.NewGuid(),
        Version: 4,
        SchemaVersion: 1,
        ConfigurationJson: """{"schemaVersion":1,"features":{},"navigation":[],"pages":{},"theme":{}}""",
        CreatedBy: Guid.NewGuid(),
        CreatedAt: DateTime.UtcNow.AddMinutes(-5),
        PublishedAt: DateTime.UtcNow);

    // ── GET history ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetHistory_returns_forbid_when_tenant_context_has_no_tenant()
    {
        _tenantContext.TenantId.Returns((Guid?)null);

        var result = await _controller.GetHistory(default);

        Assert.IsType<ForbidResult>(result);
        await _history.DidNotReceive().GetHistoryAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetHistory_returns_the_mapped_list_on_success()
    {
        _tenantContext.TenantId.Returns(TenantA);
        IReadOnlyList<MobileConfigVersionSummaryDto> versions =
        [
            new(Guid.NewGuid(), 2, "published", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(-1), Guid.NewGuid()),
            new(Guid.NewGuid(), 1, "archived", DateTime.UtcNow.AddDays(-2), DateTime.UtcNow.AddDays(-2), null),
        ];
        _history.GetHistoryAsync(TenantA, Arg.Any<CancellationToken>()).Returns(versions);

        var result = await _controller.GetHistory(default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsAssignableFrom<IReadOnlyList<MobileConfigVersionSummaryResponse>>(ok.Value);
        Assert.Equal(2, response.Count);
        Assert.Equal(versions[0].Id, response[0].Id);
        Assert.Equal(versions[0].Version, response[0].Version);
    }

    [Fact]
    public async Task GetHistory_resolves_tenant_from_ITenantContext_never_from_a_request_value()
    {
        _tenantContext.TenantId.Returns(TenantA);
        _history.GetHistoryAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns((IReadOnlyList<MobileConfigVersionSummaryDto>)[]);

        await _controller.GetHistory(default);

        await _history.Received(1).GetHistoryAsync(TenantA, Arg.Any<CancellationToken>());
    }

    // ── POST rollback ────────────────────────────────────────────────────────

    [Fact]
    public async Task Rollback_returns_forbid_when_tenant_context_has_no_tenant()
    {
        _tenantContext.TenantId.Returns((Guid?)null);

        var result = await _controller.Rollback(VersionId, default);

        Assert.IsType<ForbidResult>(result);
        await _publish.DidNotReceive().RollbackAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rollback_returns_the_published_version_on_success()
    {
        var published = SamplePublished();
        _tenantContext.TenantId.Returns(TenantA);
        _publish.RollbackAsync(TenantA, VersionId, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns((published, (MobileConfigPublishError?)null));

        var result = await _controller.Rollback(VersionId, default);

        var response = Assert.IsType<MobileConfigPublishedResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(published.VersionId, response.VersionId);
        Assert.Equal(published.Version, response.Version);
    }

    [Fact]
    public async Task Rollback_returns_404_when_the_version_is_not_found()
    {
        _tenantContext.TenantId.Returns(TenantA);
        var error = new MobileConfigPublishError(MobileConfigPublishErrorType.VersionNotFound, "not found", []);
        _publish.RollbackAsync(TenantA, VersionId, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(((MobileConfigPublishedDto?)null, error));

        var result = await _controller.Rollback(VersionId, default);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Rollback_returns_400_when_the_target_is_already_the_current_version()
    {
        _tenantContext.TenantId.Returns(TenantA);
        var error = new MobileConfigPublishError(MobileConfigPublishErrorType.CannotRollbackToCurrentVersion, "already current", []);
        _publish.RollbackAsync(TenantA, VersionId, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(((MobileConfigPublishedDto?)null, error));

        var result = await _controller.Rollback(VersionId, default);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Rollback_returns_400_with_structured_errors_when_validation_fails()
    {
        _tenantContext.TenantId.Returns(TenantA);
        IReadOnlyList<MobileConfigValidationError> errors = [new("pages", "pages is required.")];
        var error = new MobileConfigPublishError(MobileConfigPublishErrorType.ValidationFailed, "invalid", errors);
        _publish.RollbackAsync(TenantA, VersionId, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(((MobileConfigPublishedDto?)null, error));

        var result = await _controller.Rollback(VersionId, default);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var returnedErrors = badRequest.Value!.GetType().GetProperty("errors")!.GetValue(badRequest.Value);
        var errorList = Assert.IsAssignableFrom<IReadOnlyList<MobileConfigValidationError>>(returnedErrors);
        Assert.Single(errorList);
    }

    [Fact]
    public async Task Rollback_returns_409_on_a_concurrent_publish_conflict()
    {
        _tenantContext.TenantId.Returns(TenantA);
        var error = new MobileConfigPublishError(MobileConfigPublishErrorType.ConcurrentPublish, "retry", []);
        _publish.RollbackAsync(TenantA, VersionId, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(((MobileConfigPublishedDto?)null, error));

        var result = await _controller.Rollback(VersionId, default);

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task Rollback_resolves_tenant_from_ITenantContext_never_from_a_request_value()
    {
        _tenantContext.TenantId.Returns(TenantA);
        _publish.RollbackAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns((SamplePublished(), (MobileConfigPublishError?)null));

        await _controller.Rollback(VersionId, default);

        await _publish.Received(1).RollbackAsync(TenantA, VersionId, Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }
}
