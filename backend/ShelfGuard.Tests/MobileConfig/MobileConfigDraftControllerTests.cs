using System.Security.Claims;
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
/// TASK-538b — HTTP layer wrapping <see cref="IMobileConfigDraftService"/> (TASK-532's
/// <c>SaveDraftAsync</c>/<c>GetDraftAsync</c>, unit-tested against a real
/// <see cref="MobileConfigValidator"/> in <c>MobileConfigDraftServiceTests</c> and against real
/// Postgres in <c>MobileConfigDraftServiceRlsIntegrationTests</c> — neither is re-derived here).
/// This suite only covers what the controller itself adds: tenant resolution via
/// <see cref="ITenantContext"/> (never from the request body), the 403-when-no-tenant / empty-vs-
/// draft GET shape / structured-error PUT response mapping, and that
/// <c>ClaimTypes.NameIdentifier</c> is threaded through as <c>actingUserId</c> — same
/// mocked-service-plus-<see cref="ControllerContext"/> convention <c>MobileAuthControllerTests</c>
/// already uses for controller-level tests in this codebase.
/// </summary>
public sealed class MobileConfigDraftControllerTests
{
    private readonly IMobileConfigDraftService _drafts = Substitute.For<IMobileConfigDraftService>();
    private readonly ITenantContext _tenantContext = Substitute.For<ITenantContext>();
    private readonly MobileConfigDraftController _controller;

    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private const string ValidDocument = """
    {
      "schemaVersion": 1,
      "features": { "loyalty": true },
      "navigation": [
        { "type": "home", "label": "A", "icon": "home" },
        { "type": "profile", "label": "B", "icon": "user" }
      ],
      "pages": {}
    }
    """;

    public MobileConfigDraftControllerTests()
    {
        _controller = new MobileConfigDraftController(_drafts, _tenantContext)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    private static MobileConfigDraftDto SampleDraft(Guid tenantVersionId) => new(
        MobileConfigurationId: Guid.NewGuid(),
        VersionId: tenantVersionId,
        Version: 1,
        SchemaVersion: 1,
        Status: "draft",
        ConfigurationJson: ValidDocument,
        CreatedBy: UserId,
        CreatedAt: DateTime.UtcNow);

    private void AuthenticateAs(Guid userId)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        _controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    // ── GET ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_returns_forbid_when_tenant_context_has_no_tenant()
    {
        _tenantContext.TenantId.Returns((Guid?)null);

        var result = await _controller.Get(default);

        Assert.IsType<ForbidResult>(result);
        await _drafts.DidNotReceive().GetDraftAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Get_returns_empty_response_when_tenant_has_no_draft_yet()
    {
        _tenantContext.TenantId.Returns(TenantA);
        _drafts.GetDraftAsync(TenantA, Arg.Any<CancellationToken>()).Returns((MobileConfigDraftDto?)null);

        var result = await _controller.Get(default);

        var response = Assert.IsType<MobileConfigDraftResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.False(response.HasDraft);
        Assert.Null(response.VersionId);
        Assert.Null(response.ConfigurationJson);
        Assert.Equal(MobileConfigWhitelists.CurrentSchemaVersion, response.SchemaVersion);
    }

    [Fact]
    public async Task Get_returns_the_tenants_draft_when_one_exists()
    {
        var draft = SampleDraft(Guid.NewGuid());
        _tenantContext.TenantId.Returns(TenantA);
        _drafts.GetDraftAsync(TenantA, Arg.Any<CancellationToken>()).Returns(draft);

        var result = await _controller.Get(default);

        var response = Assert.IsType<MobileConfigDraftResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.True(response.HasDraft);
        Assert.Equal(draft.VersionId, response.VersionId);
        Assert.Equal(draft.ConfigurationJson, response.ConfigurationJson);
        Assert.Equal(draft.CreatedBy, response.CreatedBy);
    }

    [Fact]
    public async Task Get_resolves_tenant_from_ITenantContext_never_from_a_request_value()
    {
        _tenantContext.TenantId.Returns(TenantA);
        _drafts.GetDraftAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((MobileConfigDraftDto?)null);

        await _controller.Get(default);

        await _drafts.Received(1).GetDraftAsync(TenantA, Arg.Any<CancellationToken>());
        await _drafts.DidNotReceive().GetDraftAsync(TenantB, Arg.Any<CancellationToken>());
    }

    // ── PUT ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Save_returns_forbid_when_tenant_context_has_no_tenant()
    {
        _tenantContext.TenantId.Returns((Guid?)null);

        var result = await _controller.Save(new SaveMobileConfigDraftRequest(ValidDocument), default);

        Assert.IsType<ForbidResult>(result);
        await _drafts.DidNotReceive().SaveDraftAsync(
            Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Save_returns_structured_field_errors_on_validation_failure()
    {
        _tenantContext.TenantId.Returns(TenantA);
        IReadOnlyList<MobileConfigValidationError> errors = [new("navigation", "navigation is required.")];
        _drafts.SaveDraftAsync(TenantA, Arg.Any<Guid?>(), "{ not json", Arg.Any<CancellationToken>())
            .Returns(((MobileConfigDraftDto?)null, errors));

        var result = await _controller.Save(new SaveMobileConfigDraftRequest("{ not json"), default);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var returnedErrors = badRequest.Value!.GetType().GetProperty("errors")!.GetValue(badRequest.Value);
        var errorList = Assert.IsAssignableFrom<IReadOnlyList<MobileConfigValidationError>>(returnedErrors);
        Assert.Single(errorList);
        Assert.Equal("navigation", errorList[0].Field);
    }

    [Fact]
    public async Task Save_returns_the_saved_draft_and_threads_the_authenticated_user_as_actingUserId()
    {
        AuthenticateAs(UserId);
        var saved = SampleDraft(Guid.NewGuid());
        _tenantContext.TenantId.Returns(TenantA);
        _drafts.SaveDraftAsync(TenantA, UserId, ValidDocument, Arg.Any<CancellationToken>())
            .Returns((saved, (IReadOnlyList<MobileConfigValidationError>)[]));

        var result = await _controller.Save(new SaveMobileConfigDraftRequest(ValidDocument), default);

        var response = Assert.IsType<MobileConfigDraftResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.True(response.HasDraft);
        Assert.Equal(saved.VersionId, response.VersionId);
        await _drafts.Received(1).SaveDraftAsync(TenantA, UserId, ValidDocument, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Save_passes_a_null_actingUserId_when_no_authenticated_user_claim_is_present()
    {
        _tenantContext.TenantId.Returns(TenantA);
        var saved = SampleDraft(Guid.NewGuid());
        _drafts.SaveDraftAsync(TenantA, null, ValidDocument, Arg.Any<CancellationToken>())
            .Returns((saved, (IReadOnlyList<MobileConfigValidationError>)[]));

        await _controller.Save(new SaveMobileConfigDraftRequest(ValidDocument), default);

        await _drafts.Received(1).SaveDraftAsync(TenantA, null, ValidDocument, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Tenant_isolation_saving_one_tenants_draft_never_targets_another_tenant()
    {
        AuthenticateAs(UserId);
        var savedA = SampleDraft(Guid.NewGuid());
        _tenantContext.TenantId.Returns(TenantA);
        _drafts.SaveDraftAsync(TenantA, UserId, ValidDocument, Arg.Any<CancellationToken>())
            .Returns((savedA, (IReadOnlyList<MobileConfigValidationError>)[]));

        await _controller.Save(new SaveMobileConfigDraftRequest(ValidDocument), default);

        await _drafts.Received(1).SaveDraftAsync(TenantA, UserId, ValidDocument, Arg.Any<CancellationToken>());
        await _drafts.DidNotReceive().SaveDraftAsync(
            TenantB, Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
