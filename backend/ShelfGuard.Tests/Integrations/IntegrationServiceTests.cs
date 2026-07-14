using System.Text.Json.Nodes;
using NSubstitute;
using ShelfGuard.Application.Features.Integrations;
using ShelfGuard.Application.Features.Integrations.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Integrations;

/// <summary>
/// TASK-347 (security hotfix): before this task, IntegrationService.GetByServiceAsync only
/// masked "prro"/"vchasno" secrets — every other known service (claude, telegram, resend,
/// webhook, iot) returned its secret field fully unmasked. That gap was harmless while only
/// AtLeastStoreManager+ roles could reach it, but ADR-020's "integrations.view" TenantRole
/// capability made the same endpoint reachable by a staff-rank (rank 0) capability holder.
/// GenericIntegrationSecrets closes it for the remaining services; these tests cover the
/// full IntegrationService.GetByServiceAsync/UpsertAsync round trip, not just the masking
/// helper in isolation.
/// </summary>
public sealed class IntegrationServiceTests
{
    private readonly IIntegrationRepository _repo = Substitute.For<IIntegrationRepository>();
    private readonly IntegrationService _sut;
    private readonly Guid _tenantId = Guid.NewGuid();

    public IntegrationServiceTests() => _sut = new IntegrationService(_repo);

    [Theory]
    [InlineData("claude", "api_key")]
    [InlineData("telegram", "bot_token")]
    [InlineData("resend", "api_key")]
    [InlineData("webhook", "secret")]
    [InlineData("iot", "api_key")]
    public async Task GetByServiceAsync_MasksSecretField_ForEveryGenericService(string service, string secretField)
    {
        var config = new IntegrationConfig
        {
            TenantId = _tenantId,
            Service = service,
            Config = $"{{\"{secretField}\":\"super-secret-value-1234\",\"isEnabled\":true}}",
        };
        _repo.GetByServiceAsync(_tenantId, service, Arg.Any<CancellationToken>()).Returns(config);

        var (dto, error) = await _sut.GetByServiceAsync(_tenantId, service);

        Assert.Null(error);
        Assert.NotNull(dto);
        // Last 4 chars preserved (same convention as PrroSecrets), raw secret never leaves the service.
        var maskedValue = dto!.Config![secretField]!.GetValue<string>();
        Assert.Equal(PrroSecrets.MaskToken + "1234", maskedValue);
    }

    [Fact]
    public async Task GetByServiceAsync_NotConfiguredYet_ReturnsNullConfig_NoError()
    {
        _repo.GetByServiceAsync(_tenantId, "claude", Arg.Any<CancellationToken>())
             .Returns((IntegrationConfig?)null);

        var (dto, error) = await _sut.GetByServiceAsync(_tenantId, "claude");

        Assert.Null(dto);
        Assert.Null(error); // controller maps this to 404, not a validation error
    }

    [Fact]
    public async Task GetByServiceAsync_UnknownService_ReturnsError()
    {
        var (dto, error) = await _sut.GetByServiceAsync(_tenantId, "not-a-real-service");

        Assert.Null(dto);
        Assert.Equal("Unknown service: 'not-a-real-service'.", error);
    }

    [Fact]
    public async Task UpsertAsync_MaskedPlaceholderRoundTrip_KeepsStoredSecret_ForGenericService()
    {
        var stored = new IntegrationConfig
        {
            TenantId = _tenantId,
            Service = "claude",
            Config = "{\"api_key\":\"real-secret-abcdef\"}",
        };
        _repo.GetByServiceAsync(_tenantId, "claude", Arg.Any<CancellationToken>()).Returns(stored);

        string? capturedConfigJson = null;
        _repo.UpsertAsync(_tenantId, "claude", Arg.Do<string>(c => capturedConfigJson = c), true, Arg.Any<CancellationToken>())
             .Returns(Task.CompletedTask);

        // Simulates a client round-tripping the masked GET payload back on an unrelated PUT
        // (e.g. toggling isEnabled) without ever re-entering the real key.
        var incoming = new JsonObject { ["api_key"] = PrroSecrets.MaskToken + "cdef", ["isEnabled"] = true };

        var error = await _sut.UpsertAsync(_tenantId, "claude", new UpsertIntegrationRequest(incoming, true));

        Assert.Null(error);
        Assert.NotNull(capturedConfigJson);
        Assert.Contains("real-secret-abcdef", capturedConfigJson);
        Assert.DoesNotContain(PrroSecrets.MaskToken, capturedConfigJson);
    }

    [Fact]
    public async Task UpsertAsync_NewPlaintextValue_OverwritesStoredSecret_ForGenericService()
    {
        var stored = new IntegrationConfig
        {
            TenantId = _tenantId,
            Service = "telegram",
            Config = "{\"bot_token\":\"old-token-123\"}",
        };
        _repo.GetByServiceAsync(_tenantId, "telegram", Arg.Any<CancellationToken>()).Returns(stored);

        string? capturedConfigJson = null;
        _repo.UpsertAsync(_tenantId, "telegram", Arg.Do<string>(c => capturedConfigJson = c), true, Arg.Any<CancellationToken>())
             .Returns(Task.CompletedTask);

        var incoming = new JsonObject { ["bot_token"] = "brand-new-token-456" };

        var error = await _sut.UpsertAsync(_tenantId, "telegram", new UpsertIntegrationRequest(incoming, true));

        Assert.Null(error);
        Assert.Contains("brand-new-token-456", capturedConfigJson);
        Assert.DoesNotContain("old-token-123", capturedConfigJson);
    }
}
