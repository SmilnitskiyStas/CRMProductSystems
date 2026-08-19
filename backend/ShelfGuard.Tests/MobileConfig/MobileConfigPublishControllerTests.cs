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
/// TASK-544 — HTTP layer wrapping <see cref="IMobileConfigPublishService"/>: tenant resolution via
/// <see cref="ITenantContext"/>, the 403-when-no-tenant case, and the error-type-to-status-code
/// mapping (400 for no-draft/validation-failed, 409 for a concurrent-publish conflict). Publish
/// orchestration itself is covered by <c>MobileConfigPublishServiceTests</c> — this suite only
/// covers what the controller adds, same split <c>MobileConfigDraftControllerTests</c> already
/// established for the sibling draft controller.
/// </summary>
public sealed class MobileConfigPublishControllerTests
{
    private readonly IMobileConfigPublishService _publish = Substitute.For<IMobileConfigPublishService>();
    private readonly ITenantContext _tenantContext = Substitute.For<ITenantContext>();
    private readonly MobileConfigPublishController _controller;

    private static readonly Guid TenantA = Guid.NewGuid();

    public MobileConfigPublishControllerTests()
    {
        _controller = new MobileConfigPublishController(_publish, _tenantContext)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    private static MobileConfigPublishedDto SamplePublished() => new(
        MobileConfigurationId: Guid.NewGuid(),
        VersionId: Guid.NewGuid(),
        Version: 2,
        SchemaVersion: 1,
        ConfigurationJson: """{"schemaVersion":1,"features":{},"navigation":[],"pages":{},"theme":{}}""",
        CreatedBy: Guid.NewGuid(),
        CreatedAt: DateTime.UtcNow.AddMinutes(-5),
        PublishedAt: DateTime.UtcNow);

    [Fact]
    public async Task Publish_returns_forbid_when_tenant_context_has_no_tenant()
    {
        _tenantContext.TenantId.Returns((Guid?)null);

        var result = await _controller.Publish(default);

        Assert.IsType<ForbidResult>(result);
        await _publish.DidNotReceive().PublishAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Publish_returns_the_published_version_on_success()
    {
        var published = SamplePublished();
        _tenantContext.TenantId.Returns(TenantA);
        _publish.PublishAsync(TenantA, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns((published, (MobileConfigPublishError?)null));

        var result = await _controller.Publish(default);

        var response = Assert.IsType<MobileConfigPublishedResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(published.VersionId, response.VersionId);
        Assert.Equal(published.Version, response.Version);
        Assert.Equal(published.PublishedAt, response.PublishedAt);
    }

    [Fact]
    public async Task Publish_returns_400_with_structured_errors_when_validation_fails()
    {
        _tenantContext.TenantId.Returns(TenantA);
        IReadOnlyList<MobileConfigValidationError> errors = [new("navigation", "navigation is required.")];
        var error = new MobileConfigPublishError(MobileConfigPublishErrorType.ValidationFailed, "invalid", errors);
        _publish.PublishAsync(TenantA, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(((MobileConfigPublishedDto?)null, error));

        var result = await _controller.Publish(default);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var returnedErrors = badRequest.Value!.GetType().GetProperty("errors")!.GetValue(badRequest.Value);
        var errorList = Assert.IsAssignableFrom<IReadOnlyList<MobileConfigValidationError>>(returnedErrors);
        Assert.Single(errorList);
    }

    [Fact]
    public async Task Publish_returns_400_when_tenant_has_no_draft_to_publish()
    {
        _tenantContext.TenantId.Returns(TenantA);
        var error = new MobileConfigPublishError(MobileConfigPublishErrorType.NoDraftToPublish, "no draft", []);
        _publish.PublishAsync(TenantA, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(((MobileConfigPublishedDto?)null, error));

        var result = await _controller.Publish(default);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Publish_returns_409_on_a_concurrent_publish_conflict()
    {
        _tenantContext.TenantId.Returns(TenantA);
        var error = new MobileConfigPublishError(MobileConfigPublishErrorType.ConcurrentPublish, "retry", []);
        _publish.PublishAsync(TenantA, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(((MobileConfigPublishedDto?)null, error));

        var result = await _controller.Publish(default);

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task Publish_resolves_tenant_from_ITenantContext_never_from_a_request_value()
    {
        _tenantContext.TenantId.Returns(TenantA);
        _publish.PublishAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns((SamplePublished(), (MobileConfigPublishError?)null));

        await _controller.Publish(default);

        await _publish.Received(1).PublishAsync(TenantA, Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }
}
