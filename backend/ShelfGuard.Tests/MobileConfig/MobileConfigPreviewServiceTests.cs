using System.Text.Json;
using NSubstitute;
using ShelfGuard.Application.Features.MobileConfig;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.MobileConfig;

/// <summary>
/// TASK-547 — <c>MobileConfigPreviewService</c>: draft-body composition, live theme composition
/// (reusing <see cref="MobileThemeJson"/>, the same helper <c>MobileConfigPublishService</c> uses
/// at real publish time — not a re-implementation), the "no draft yet" empty/default shape, and
/// that this service never mutates anything (no <c>SaveChangesAsync</c>, no entity mutation call).
/// </summary>
public sealed class MobileConfigPreviewServiceTests
{
    private readonly IMobileConfigurationRepository _repo = Substitute.For<IMobileConfigurationRepository>();
    private readonly ITenantRepository _tenants = Substitute.For<ITenantRepository>();
    private readonly MobileConfigPreviewService _sut;

    private static readonly Guid TenantId = Guid.NewGuid();

    private const string DraftDocumentJson = """
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

    public MobileConfigPreviewServiceTests() => _sut = new MobileConfigPreviewService(_repo, _tenants);

    [Fact]
    public async Task Returns_empty_default_document_when_tenant_has_no_MobileConfiguration_row_yet()
    {
        var tenant = SeedTenant();
        _tenants.GetByIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(tenant);
        _repo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns((MobileConfiguration?)null);

        var json = await _sut.GetPreviewAsync(TenantId);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.False(root.GetProperty("hasDraft").GetBoolean());
        Assert.Equal(0, root.GetProperty("configVersion").GetInt32());
        Assert.Equal(MobileConfigWhitelists.CurrentSchemaVersion, root.GetProperty("schemaVersion").GetInt32());
        Assert.Empty(root.GetProperty("features").EnumerateObject());
        Assert.Equal(0, root.GetProperty("navigation").GetArrayLength());
        Assert.Empty(root.GetProperty("pages").EnumerateObject());

        // Theme is still live-composed even with no draft/config row at all — falls back to
        // MobileTheme.CreateDefault's hardcoded values, same as MobileConfigPublishService would.
        var themeEl = root.GetProperty("theme");
        var expectedDefault = MobileTheme.CreateDefault(Guid.Empty, TenantId);
        Assert.Equal(expectedDefault.PrimaryColor, themeEl.GetProperty("colors").GetProperty("primary").GetString());
        Assert.Equal(expectedDefault.SpacingPreset, themeEl.GetProperty("spacing").GetString());
    }

    [Fact]
    public async Task Returns_empty_default_document_when_config_exists_but_has_no_draft()
    {
        var tenant = SeedTenant();
        _tenants.GetByIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(tenant);

        var config = MobileConfiguration.Create(TenantId);
        // DraftVersionId/DraftVersion deliberately left null.
        _repo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(config);

        var json = await _sut.GetPreviewAsync(TenantId);

        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("hasDraft").GetBoolean());
        Assert.Equal(0, doc.RootElement.GetProperty("configVersion").GetInt32());
    }

    [Fact]
    public async Task Returns_the_drafts_own_body_with_hasDraft_true_when_a_draft_exists()
    {
        var tenant = SeedTenant(name: "Аврора Маркет", slug: "aurora-market", logoUrl: "https://cdn/tenant-logo.png");
        var config = SeedDraftConfig(tenant.Id, DraftDocumentJson, version: 4);
        _tenants.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        _repo.GetByTenantIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(config);

        var json = await _sut.GetPreviewAsync(tenant.Id);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("hasDraft").GetBoolean());
        Assert.Equal(4, root.GetProperty("configVersion").GetInt32());
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());

        var tenantEl = root.GetProperty("tenant");
        Assert.Equal(tenant.Id.ToString(), tenantEl.GetProperty("id").GetString());
        Assert.Equal("aurora-market", tenantEl.GetProperty("slug").GetString());
        Assert.Equal("Аврора Маркет", tenantEl.GetProperty("name").GetString());
        Assert.Equal("https://cdn/tenant-logo.png", tenantEl.GetProperty("logoUrl").GetString());

        var features = root.GetProperty("features");
        Assert.True(features.GetProperty("loyalty").GetBoolean());
        Assert.False(features.GetProperty("news").GetBoolean());
        Assert.Equal(2, root.GetProperty("navigation").GetArrayLength());
        Assert.True(root.GetProperty("pages").TryGetProperty("home", out _));
    }

    [Fact]
    public async Task A_drafts_own_ConfigurationJson_never_carries_a_theme_key_but_the_response_still_has_one()
    {
        // TASK-532's invariant: SaveDraftAsync never writes a "theme" key into the draft body.
        // This service must still produce a "theme" key in its response by composing it live —
        // asserting the source document truly has none pins that this isn't accidentally just
        // passing a pre-existing key through.
        using (var draftDoc = JsonDocument.Parse(DraftDocumentJson))
            Assert.False(draftDoc.RootElement.TryGetProperty("theme", out _));

        var tenant = SeedTenant();
        var config = SeedDraftConfig(tenant.Id, DraftDocumentJson, version: 1);
        _tenants.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        _repo.GetByTenantIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(config);

        var json = await _sut.GetPreviewAsync(tenant.Id);

        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("theme", out _));
    }

    [Fact]
    public async Task Theme_is_composed_live_from_the_tenants_current_MobileTheme_row_via_MobileThemeJson()
    {
        var tenant = SeedTenant();
        var config = SeedDraftConfig(tenant.Id, DraftDocumentJson, version: 1);
        var theme = MobileTheme.CreateDefault(config.Id, tenant.Id);
        theme.Update(
            logoUrl: "https://cdn/live-theme-logo.png",
            primaryColor: "#ABCDEF",
            secondaryColor: "#123456",
            backgroundColor: "#FFFFFF",
            surfaceColor: "#F0F0F0",
            textPrimaryColor: "#000000",
            textSecondaryColor: "#333333",
            buttonRadius: 30,
            cardRadius: 40,
            spacingPreset: "spacious");
        SetNavigation(config, nameof(MobileConfiguration.Theme), theme);

        _tenants.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        _repo.GetByTenantIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(config);

        var json = await _sut.GetPreviewAsync(tenant.Id);

        using var doc = JsonDocument.Parse(json);
        var themeEl = doc.RootElement.GetProperty("theme");

        // Exact shape MobileThemeJson.ToJsonObject produces — pins that the service reuses that
        // helper rather than hand-rolling its own theme JSON.
        Assert.Equal("https://cdn/live-theme-logo.png", themeEl.GetProperty("logoUrl").GetString());
        Assert.Equal("#ABCDEF", themeEl.GetProperty("colors").GetProperty("primary").GetString());
        Assert.Equal("#123456", themeEl.GetProperty("colors").GetProperty("secondary").GetString());
        Assert.Equal(30, themeEl.GetProperty("buttons").GetProperty("radius").GetInt32());
        Assert.Equal(40, themeEl.GetProperty("cards").GetProperty("radius").GetInt32());
        Assert.Equal("spacious", themeEl.GetProperty("spacing").GetString());
    }

    [Fact]
    public async Task Never_calls_SaveChangesAsync_or_any_mutation_method()
    {
        var tenant = SeedTenant();
        var config = SeedDraftConfig(tenant.Id, DraftDocumentJson, version: 1);
        _tenants.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        _repo.GetByTenantIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(config);

        await _sut.GetPreviewAsync(tenant.Id);

        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        _repo.DidNotReceive().Update(Arg.Any<MobileConfiguration>());
        _repo.DidNotReceive().UpdateVersion(Arg.Any<MobileConfigurationVersion>());
        _repo.DidNotReceive().UpdateTheme(Arg.Any<MobileTheme>());
        await _repo.DidNotReceive().AddVersionAsync(Arg.Any<MobileConfigurationVersion>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().AddThemeAsync(Arg.Any<MobileTheme>(), Arg.Any<CancellationToken>());
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static Tenant SeedTenant(
        string name = "Test Tenant", string slug = "test-tenant", string? logoUrl = null)
    {
        var tenant = Tenant.Create(name, slug);
        if (logoUrl is not null) tenant.UpdateLogoUrl(logoUrl);
        return tenant;
    }

    private static MobileConfiguration SeedDraftConfig(Guid tenantId, string configurationJson, int version)
    {
        var config = MobileConfiguration.Create(tenantId);
        var draft = MobileConfigurationVersion.Create(
            config.Id, tenantId, version: version, schemaVersion: 1, configurationJson: configurationJson);
        config.SetDraftVersion(draft.Id);
        SetNavigation(config, nameof(MobileConfiguration.DraftVersion), draft);
        return config;
    }

    // MobileConfiguration.DraftVersion/Theme have private setters (only fixed up by EF Core's real
    // Include() during materialization) — tests simulate that same post-load shape via reflection,
    // same approach MobileConfigPublishedReadServiceTests/MobileConfigDraftServiceTests use.
    private static void SetNavigation(MobileConfiguration config, string propertyName, object? value)
    {
        typeof(MobileConfiguration).GetProperty(propertyName)!.SetValue(config, value);
    }
}
