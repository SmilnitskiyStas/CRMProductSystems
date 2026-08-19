using System.Text.Json;
using NSubstitute;
using ShelfGuard.Application.Features.MobileConfig;
using ShelfGuard.Application.Features.MobileConfig.Dtos;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.MobileConfig;

/// <summary>
/// TASK-534 (theme sourcing updated by TASK-544) — <c>GET /api/v1/mobile/config</c> read
/// orchestration: document shape, draft/archived versions never leaking through, tenant-not-found
/// vs no-published-config distinction, theme sourced from the published document (with a
/// hardcoded-default fallback for a document that predates TASK-544 and has no <c>theme</c> key at
/// all), and ETag behavior. Wires up real repository-independent logic with mocked
/// <see cref="IMobileConfigurationRepository"/>/<see cref="ITenantRepository"/>/
/// <see cref="ITenantSessionOverride"/> — a mocked ITenantSessionOverride can only pin "the
/// override is invoked with the right tenantId", not prove real Postgres RLS; that live-RLS proof
/// is <c>MobileConfigPublishedReadRlsIntegrationTests</c> (mirrors the
/// LoyaltyServiceTests/LoyaltyJoinRlsIntegrationTests split for the same reason).
/// </summary>
public sealed class MobileConfigPublishedReadServiceTests
{
    private readonly IMobileConfigurationRepository _repo = Substitute.For<IMobileConfigurationRepository>();
    private readonly ITenantRepository _tenants = Substitute.For<ITenantRepository>();
    private readonly ITenantSessionOverride _tenantScope = Substitute.For<ITenantSessionOverride>();
    private readonly MobileConfigPublishedReadService _sut;

    private static readonly Guid TenantId = Guid.NewGuid();

    private const string PublishedDocumentJson = """
    {
      "schemaVersion": 1,
      "features": { "loyalty": true, "news": false },
      "navigation": [
        { "type": "home", "label": "Головна", "icon": "home" },
        { "type": "profile", "label": "Профіль", "icon": "user" }
      ],
      "pages": { "home": { "blocks": [] } }
    }
    """;

    /// <summary>
    /// Post-TASK-544 shape: a real <c>MobileConfigPublishService.PublishAsync</c> call always
    /// composes a <c>"theme"</c> key into the version it publishes — this is what a published row
    /// actually looks like in production now.
    /// </summary>
    private const string PublishedDocumentJsonWithTheme = """
    {
      "schemaVersion": 1,
      "features": { "loyalty": true, "news": false },
      "navigation": [
        { "type": "home", "label": "Головна", "icon": "home" },
        { "type": "profile", "label": "Профіль", "icon": "user" }
      ],
      "pages": { "home": { "blocks": [] } },
      "theme": {
        "logoUrl": "https://cdn/theme-logo.png",
        "colors": {
          "primary": "#112233",
          "secondary": "#445566",
          "background": "#FFFFFF",
          "surface": "#F0F0F0",
          "textPrimary": "#000000",
          "textSecondary": "#333333"
        },
        "buttons": { "radius": 20 },
        "cards": { "radius": 24 },
        "spacing": "compact"
      }
    }
    """;

    public MobileConfigPublishedReadServiceTests()
    {
        _sut = new MobileConfigPublishedReadService(_repo, _tenants, _tenantScope);

        // Pure pass-through — same pattern LoyaltyServiceTests uses for ITenantSessionOverride:
        // this mock just invokes the delegate immediately instead of opening a real transaction.
        _tenantScope.ExecuteAsync(
                Arg.Any<Guid>(), Arg.Any<Func<Task<MobileConfiguration?>>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<Func<Task<MobileConfiguration?>>>()());
    }

    [Fact]
    public async Task Returns_TenantNotFound_when_tenant_does_not_exist()
    {
        _tenants.GetByIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns((Tenant?)null);

        var (documentJson, etag, error) = await _sut.GetPublishedConfigAsync(TenantId);

        Assert.Null(documentJson);
        Assert.Null(etag);
        Assert.NotNull(error);
        Assert.Equal(MobileConfigReadErrorType.TenantNotFound, error!.Type);

        // Tenant existence is checked before any RLS-scoped read is attempted.
        await _repo.DidNotReceive().GetPublishedByTenantIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Returns_NoPublishedConfiguration_when_tenant_has_no_MobileConfiguration_row_yet()
    {
        var tenant = SeedTenant();
        _tenants.GetByIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(tenant);
        _repo.GetPublishedByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns((MobileConfiguration?)null);

        var (documentJson, etag, error) = await _sut.GetPublishedConfigAsync(TenantId);

        Assert.Null(documentJson);
        Assert.Null(etag);
        Assert.Equal(MobileConfigReadErrorType.NoPublishedConfiguration, error!.Type);
    }

    [Fact]
    public async Task Returns_NoPublishedConfiguration_when_only_a_draft_exists_and_never_leaks_the_draft_content()
    {
        var tenant = SeedTenant();
        _tenants.GetByIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(tenant);

        // A config with only a draft — PublishedVersionId/PublishedVersion stay null, exactly the
        // shape GetPublishedByTenantIdAsync's real EF Include would produce for a tenant that has
        // saved a draft but never published (TASK-532's world, before TASK-544 exists).
        var config = MobileConfiguration.Create(TenantId);
        var draft = MobileConfigurationVersion.Create(
            config.Id, TenantId, version: 1, schemaVersion: 1,
            configurationJson: """{"schemaVersion":1,"features":{},"navigation":[],"pages":{},"secretDraftMarker":"draft-only"}""");
        config.SetDraftVersion(draft.Id);
        // Deliberately NOT wiring PublishedVersion navigation — GetPublishedByTenantIdAsync never
        // even Includes DraftVersion in the real repository, so PublishedVersion is the only
        // thing this service ever looks at.
        _repo.GetPublishedByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(config);

        var (documentJson, etag, error) = await _sut.GetPublishedConfigAsync(TenantId);

        Assert.Null(documentJson);
        Assert.Null(etag);
        Assert.Equal(MobileConfigReadErrorType.NoPublishedConfiguration, error!.Type);
    }

    [Fact]
    public async Task Returns_NoPublishedConfiguration_when_the_published_pointer_targets_a_non_Published_version()
    {
        // Defensive-only path: PublishedVersionId should always point at a Status==Published row
        // once a real publish flow exists, but this service does not trust that invariant blindly.
        var tenant = SeedTenant();
        _tenants.GetByIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(tenant);

        var config = MobileConfiguration.Create(TenantId);
        var version = MobileConfigurationVersion.Create(
            config.Id, TenantId, version: 1, schemaVersion: 1, configurationJson: PublishedDocumentJson);
        // version.Status stays "draft" — never call Publish() — while still wiring it as if it
        // were the published pointer target.
        config.SetPublishedVersion(version.Id);
        SetNavigation(config, nameof(MobileConfiguration.PublishedVersion), version);
        _repo.GetPublishedByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(config);

        var (documentJson, _, error) = await _sut.GetPublishedConfigAsync(TenantId);

        Assert.Null(documentJson);
        Assert.Equal(MobileConfigReadErrorType.NoPublishedConfiguration, error!.Type);
    }

    [Fact]
    public async Task Returns_full_document_matching_the_contract_shape_sourcing_theme_from_the_published_document()
    {
        var tenant = SeedTenant(name: "Аврора Маркет", slug: "aurora-market", logoUrl: "https://cdn/tenant-logo.png");
        var (config, version) = SeedPublishedConfig(tenant.Id, PublishedDocumentJsonWithTheme);

        _tenants.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        _repo.GetPublishedByTenantIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(config);

        var (documentJson, etag, error) = await _sut.GetPublishedConfigAsync(tenant.Id);

        Assert.Null(error);
        Assert.NotNull(documentJson);
        Assert.NotNull(etag);
        Assert.Matches("^\"[0-9A-F]{64}\"$", etag!);

        using var doc = JsonDocument.Parse(documentJson!);
        var root = doc.RootElement;

        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(version.Version, root.GetProperty("configVersion").GetInt32());

        var tenantEl = root.GetProperty("tenant");
        Assert.Equal(tenant.Id.ToString(), tenantEl.GetProperty("id").GetString());
        Assert.Equal("aurora-market", tenantEl.GetProperty("slug").GetString());
        Assert.Equal("Аврора Маркет", tenantEl.GetProperty("name").GetString());
        Assert.Equal("https://cdn/tenant-logo.png", tenantEl.GetProperty("logoUrl").GetString());

        var themeEl = root.GetProperty("theme");
        Assert.Equal("https://cdn/theme-logo.png", themeEl.GetProperty("logoUrl").GetString());
        Assert.Equal("#112233", themeEl.GetProperty("colors").GetProperty("primary").GetString());
        Assert.Equal("#445566", themeEl.GetProperty("colors").GetProperty("secondary").GetString());
        Assert.Equal(20, themeEl.GetProperty("buttons").GetProperty("radius").GetInt32());
        Assert.Equal(24, themeEl.GetProperty("cards").GetProperty("radius").GetInt32());
        Assert.Equal("compact", themeEl.GetProperty("spacing").GetString());

        var features = root.GetProperty("features");
        Assert.True(features.GetProperty("loyalty").GetBoolean());
        Assert.False(features.GetProperty("news").GetBoolean());

        Assert.Equal(2, root.GetProperty("navigation").GetArrayLength());
        Assert.True(root.GetProperty("pages").TryGetProperty("home", out _));
    }

    [Fact]
    public async Task Falls_back_to_MobileTheme_defaults_when_the_published_document_has_no_theme_key()
    {
        // Defensive compatibility path only: a published row that predates the TASK-544
        // reconciliation (or, as here, was seeded directly by a test) has no "theme" key at all —
        // GetPublishedByTenantIdAsync no longer includes MobileTheme, so this must fall back to
        // MobileTheme.CreateDefault's hardcoded values, not a live join.
        var tenant = SeedTenant();
        var (config, _) = SeedPublishedConfig(tenant.Id, PublishedDocumentJson); // no "theme" key

        _tenants.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        _repo.GetPublishedByTenantIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(config);

        var (documentJson, _, error) = await _sut.GetPublishedConfigAsync(tenant.Id);

        Assert.Null(error);
        using var doc = JsonDocument.Parse(documentJson!);
        var themeEl = doc.RootElement.GetProperty("theme");

        var expectedDefault = MobileTheme.CreateDefault(config.Id, tenant.Id);
        Assert.Equal(expectedDefault.PrimaryColor, themeEl.GetProperty("colors").GetProperty("primary").GetString());
        Assert.Equal(expectedDefault.SpacingPreset, themeEl.GetProperty("spacing").GetString());
        Assert.Equal(JsonValueKind.Null, themeEl.GetProperty("logoUrl").ValueKind);
    }

    [Fact]
    public async Task Returns_the_published_documents_own_theme_key_verbatim_when_one_is_present()
    {
        var tenant = SeedTenant();
        var (config, _) = SeedPublishedConfig(tenant.Id, PublishedDocumentJsonWithTheme);

        _tenants.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        _repo.GetPublishedByTenantIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(config);

        var (documentJson, _, error) = await _sut.GetPublishedConfigAsync(tenant.Id);

        Assert.Null(error);
        using var doc = JsonDocument.Parse(documentJson!);
        var themeEl = doc.RootElement.GetProperty("theme");
        Assert.Equal("https://cdn/theme-logo.png", themeEl.GetProperty("logoUrl").GetString());
        Assert.Equal("#112233", themeEl.GetProperty("colors").GetProperty("primary").GetString());
    }

    [Fact]
    public async Task ETag_is_stable_across_repeated_calls_and_differs_only_when_the_published_document_changes()
    {
        var tenant = SeedTenant();
        var (config, _) = SeedPublishedConfig(tenant.Id, PublishedDocumentJson);

        _tenants.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        _repo.GetPublishedByTenantIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(config);

        var (_, etag1, _) = await _sut.GetPublishedConfigAsync(tenant.Id);
        var (_, etag2, _) = await _sut.GetPublishedConfigAsync(tenant.Id);
        Assert.Equal(etag1, etag2);

        // A new publish (a different ConfigurationJson.theme, as MobileConfigPublishService would
        // produce) changes the served document, and therefore the ETag.
        var (configWithTheme, _) = SeedPublishedConfig(tenant.Id, PublishedDocumentJsonWithTheme);
        _repo.GetPublishedByTenantIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(configWithTheme);

        var (_, etag3, _) = await _sut.GetPublishedConfigAsync(tenant.Id);
        Assert.NotEqual(etag1, etag3);
    }

    [Fact]
    public async Task Resolves_the_read_through_ITenantSessionOverride_with_the_requested_tenantId()
    {
        var tenant = SeedTenant();
        var (config, _) = SeedPublishedConfig(tenant.Id, PublishedDocumentJson);
        _tenants.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        _repo.GetPublishedByTenantIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(config);

        await _sut.GetPublishedConfigAsync(tenant.Id);

        await _tenantScope.Received(1).ExecuteAsync(
            tenant.Id, Arg.Any<Func<Task<MobileConfiguration?>>>(), Arg.Any<CancellationToken>());
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static Tenant SeedTenant(
        string name = "Test Tenant", string slug = "test-tenant", string? logoUrl = null)
    {
        var tenant = Tenant.Create(name, slug);
        if (logoUrl is not null) tenant.UpdateLogoUrl(logoUrl);
        return tenant;
    }

    private static (MobileConfiguration Config, MobileConfigurationVersion Version) SeedPublishedConfig(
        Guid tenantId, string configurationJson)
    {
        var config = MobileConfiguration.Create(tenantId);
        var version = MobileConfigurationVersion.Create(
            config.Id, tenantId, version: 3, schemaVersion: 1, configurationJson: configurationJson);
        version.Publish(DateTime.UtcNow);
        config.SetPublishedVersion(version.Id);
        SetNavigation(config, nameof(MobileConfiguration.PublishedVersion), version);
        return (config, version);
    }

    // MobileConfiguration.PublishedVersion/Theme have private setters (only fixed up by EF Core's
    // real Include() during materialization) — tests simulate that same post-load shape via
    // reflection, same approach MobileConfigDraftServiceTests uses for DraftVersion.
    private static void SetNavigation(MobileConfiguration config, string propertyName, object? value)
    {
        typeof(MobileConfiguration).GetProperty(propertyName)!.SetValue(config, value);
    }
}
