using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using ShelfGuard.Api.Controllers;
using ShelfGuard.Application.Features.MobileConfig;
using ShelfGuard.Application.Services;
using Xunit;

namespace ShelfGuard.Tests.MobileConfig;

/// <summary>
/// TASK-547 — HTTP layer wrapping <see cref="IMobileConfigPreviewService"/>: tenant resolution via
/// <see cref="ITenantContext"/> (never from a request value), the 403-when-no-tenant case, and
/// that the service's document is served verbatim. Document composition itself is covered by
/// <c>MobileConfigPreviewServiceTests</c> — this suite only covers what the controller adds, same
/// split <c>MobileConfigDraftControllerTests</c>/<c>MobileConfigPublishControllerTests</c> already
/// established for the sibling draft/publish controllers. The authorization POLICY itself
/// (<c>AtLeastEnterpriseAdmin</c> rejecting a consumer session) is covered end-to-end in
/// <c>MobileConfigPreviewAuthorizationTests</c>.
/// </summary>
public sealed class MobileConfigPreviewControllerTests
{
    private readonly IMobileConfigPreviewService _preview = Substitute.For<IMobileConfigPreviewService>();
    private readonly ITenantContext _tenantContext = Substitute.For<ITenantContext>();
    private readonly MobileConfigPreviewController _controller;

    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    private const string SampleDocumentJson = """
    {"hasDraft":true,"schemaVersion":1,"configVersion":2,"tenant":{"id":"x"},"theme":{},"features":{},"navigation":[],"pages":{}}
    """;

    public MobileConfigPreviewControllerTests()
    {
        _controller = new MobileConfigPreviewController(_preview, _tenantContext)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    [Fact]
    public async Task Get_returns_forbid_when_tenant_context_has_no_tenant()
    {
        _tenantContext.TenantId.Returns((Guid?)null);

        var result = await _controller.Get(default);

        Assert.IsType<ForbidResult>(result);
        await _preview.DidNotReceive().GetPreviewAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Get_returns_the_services_document_verbatim_as_application_json()
    {
        _tenantContext.TenantId.Returns(TenantA);
        _preview.GetPreviewAsync(TenantA, Arg.Any<CancellationToken>()).Returns(SampleDocumentJson);

        var result = await _controller.Get(default);

        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal(SampleDocumentJson, content.Content);
        Assert.Equal("application/json", content.ContentType);
    }

    [Fact]
    public async Task Get_resolves_tenant_from_ITenantContext_never_from_a_request_value()
    {
        _tenantContext.TenantId.Returns(TenantA);
        _preview.GetPreviewAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(SampleDocumentJson);

        await _controller.Get(default);

        await _preview.Received(1).GetPreviewAsync(TenantA, Arg.Any<CancellationToken>());
        await _preview.DidNotReceive().GetPreviewAsync(TenantB, Arg.Any<CancellationToken>());
    }
}
