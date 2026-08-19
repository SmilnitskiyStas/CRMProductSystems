using System.Text.Json;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ShelfGuard.Application.Features.MobileConfig;
using ShelfGuard.Application.Features.MobileConfig.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Exceptions;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.MobileConfig;

/// <summary>
/// TASK-544 — <see cref="MobileConfigPublishService"/> orchestration: reject-on-invalid,
/// successful publish, theme composition, draft-continuity after publish (property #2), and
/// concurrent-publish safety. Wires up the real <see cref="MobileConfigValidator"/> (no mock) —
/// these tests care about publish orchestration, not re-deriving validation rules (already covered
/// by <c>MobileConfigValidatorTests</c>). Real-Postgres proof that the xmin token actually blocks a
/// concurrent writer lives in <c>MobileConfigPublishConcurrencyIntegrationTests</c> — a mocked
/// repository here can only prove the service reacts correctly to
/// <see cref="ConcurrencyConflictException"/>, not that Postgres actually throws it.
/// </summary>
public sealed class MobileConfigPublishServiceTests
{
    private readonly IMobileConfigurationRepository _repo = Substitute.For<IMobileConfigurationRepository>();
    private readonly IActivityLogRepository _activityLogs = Substitute.For<IActivityLogRepository>();
    private readonly MobileConfigPublishService _sut;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private const string ValidDraftJson = """
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
    /// A historical PUBLISHED/ARCHIVED version's <c>ConfigurationJson</c> — same theme-less body
    /// shape as <see cref="ValidDraftJson"/>, but with a composed <c>"theme"</c> key, exactly what
    /// <c>MobileConfigPublishService.ComposeTheme</c> produces at publish time (TASK-544). Feeding
    /// this WHOLE document through <see cref="MobileConfigValidator"/> as-is must fail ("unknown
    /// field 'theme'") — that is the exact naive-validation trap TASK-545's rollback flow avoids by
    /// splitting theme out first.
    /// </summary>
    private const string HistoricalComposedJsonWithTheme = """
    {
      "schemaVersion": 1,
      "features": { "loyalty": true, "news": false },
      "navigation": [
        { "type": "home", "label": "Головна", "icon": "home" },
        { "type": "profile", "label": "Профіль", "icon": "user" }
      ],
      "pages": { "home": { "blocks": [] } },
      "theme": {
        "logoUrl": "https://cdn/historical-logo.png",
        "colors": {
          "primary": "#AAAAAA",
          "secondary": "#BBBBBB",
          "background": "#CCCCCC",
          "surface": "#DDDDDD",
          "textPrimary": "#EEEEEE",
          "textSecondary": "#111111"
        },
        "buttons": { "radius": 99 },
        "cards": { "radius": 88 },
        "spacing": "cozy"
      }
    }
    """;

    public MobileConfigPublishServiceTests()
    {
        _sut = new MobileConfigPublishService(_repo, new MobileConfigValidator(), _activityLogs);
    }

    [Fact]
    public async Task PublishAsync_returns_NoDraftToPublish_when_tenant_has_no_MobileConfiguration_row_at_all()
    {
        _repo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns((MobileConfiguration?)null);

        var (published, error) = await _sut.PublishAsync(TenantId, UserId);

        Assert.Null(published);
        Assert.Equal(MobileConfigPublishErrorType.NoDraftToPublish, error!.Type);
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishAsync_returns_NoDraftToPublish_when_configuration_exists_but_has_no_draft()
    {
        var config = MobileConfiguration.Create(TenantId);
        _repo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(config);

        var (published, error) = await _sut.PublishAsync(TenantId, UserId);

        Assert.Null(published);
        Assert.Equal(MobileConfigPublishErrorType.NoDraftToPublish, error!.Type);
    }

    [Fact]
    public async Task PublishAsync_rejects_an_invalid_draft_and_persists_nothing()
    {
        var (config, draft) = SeedDraft("""{ "schemaVersion": 1, "features": {}, "navigation": [] }"""); // missing "pages"
        _repo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(config);

        var (published, error) = await _sut.PublishAsync(TenantId, UserId);

        Assert.Null(published);
        Assert.Equal(MobileConfigPublishErrorType.ValidationFailed, error!.Type);
        Assert.NotEmpty(error.ValidationErrors);
        Assert.Equal("draft", draft.Status); // untouched

        await _repo.DidNotReceive().AddVersionAsync(Arg.Any<MobileConfigurationVersion>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishAsync_publishes_a_valid_draft_and_sets_PublishedAt()
    {
        var (config, draft) = SeedDraft(ValidDraftJson);
        _repo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(config);
        _repo.GetMaxVersionNumberAsync(TenantId, Arg.Any<CancellationToken>()).Returns(1);

        var before = DateTime.UtcNow;
        var (published, error) = await _sut.PublishAsync(TenantId, UserId);
        var after = DateTime.UtcNow;

        Assert.Null(error);
        Assert.NotNull(published);
        Assert.Equal(draft.Id, published!.VersionId);
        Assert.Equal("published", draft.Status);
        Assert.InRange(published.PublishedAt, before, after);

        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        _repo.Received(1).UpdateVersion(Arg.Is<MobileConfigurationVersion>(v => v.Id == draft.Id));
        _repo.Received(1).Update(Arg.Is<MobileConfiguration>(c =>
            c.PublishedVersionId == draft.Id && c.DraftVersionId != draft.Id));
    }

    [Fact]
    public async Task PublishAsync_composes_the_current_MobileTheme_into_the_published_document_but_never_the_new_draft()
    {
        var (config, draft) = SeedDraft(ValidDraftJson);
        var theme = MobileTheme.CreateDefault(config.Id, TenantId);
        theme.Update(
            logoUrl: "https://cdn/logo.png", primaryColor: "#ABCDEF", secondaryColor: "#111111",
            backgroundColor: "#FFFFFF", surfaceColor: "#EEEEEE", textPrimaryColor: "#000000",
            textSecondaryColor: "#222222", buttonRadius: 12, cardRadius: 16, spacingPreset: "compact");
        SetNavigation(config, nameof(MobileConfiguration.Theme), theme);

        _repo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(config);
        _repo.GetMaxVersionNumberAsync(TenantId, Arg.Any<CancellationToken>()).Returns(1);

        MobileConfigurationVersion? newDraft = null;
        _repo.When(r => r.AddVersionAsync(Arg.Any<MobileConfigurationVersion>(), Arg.Any<CancellationToken>()))
             .Do(ci => newDraft = ci.Arg<MobileConfigurationVersion>());

        var (published, error) = await _sut.PublishAsync(TenantId, UserId);

        Assert.Null(error);
        using (var publishedDoc = JsonDocument.Parse(published!.ConfigurationJson))
        {
            var themeEl = publishedDoc.RootElement.GetProperty("theme");
            Assert.Equal("https://cdn/logo.png", themeEl.GetProperty("logoUrl").GetString());
            Assert.Equal("#ABCDEF", themeEl.GetProperty("colors").GetProperty("primary").GetString());
            Assert.Equal(12, themeEl.GetProperty("buttons").GetProperty("radius").GetInt32());
            Assert.Equal("compact", themeEl.GetProperty("spacing").GetString());
        }

        // Property #1: the draft's own ConfigurationJson (pre-composition, and the clone that
        // becomes the new draft) never carries a "theme" key.
        Assert.NotNull(newDraft);
        using var newDraftDoc = JsonDocument.Parse(newDraft!.ConfigurationJson);
        Assert.False(newDraftDoc.RootElement.TryGetProperty("theme", out _));
    }

    [Fact]
    public async Task PublishAsync_falls_back_to_default_theme_when_tenant_has_no_MobileTheme_row_yet()
    {
        var (config, _) = SeedDraft(ValidDraftJson); // config.Theme deliberately left null
        _repo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(config);
        _repo.GetMaxVersionNumberAsync(TenantId, Arg.Any<CancellationToken>()).Returns(1);

        var (published, error) = await _sut.PublishAsync(TenantId, UserId);

        Assert.Null(error);
        var expectedDefault = MobileTheme.CreateDefault(config.Id, TenantId);
        using var doc = JsonDocument.Parse(published!.ConfigurationJson);
        var themeEl = doc.RootElement.GetProperty("theme");
        Assert.Equal(expectedDefault.PrimaryColor, themeEl.GetProperty("colors").GetProperty("primary").GetString());
        Assert.Equal(JsonValueKind.Null, themeEl.GetProperty("logoUrl").ValueKind);
    }

    [Fact]
    public async Task PublishAsync_repoints_DraftVersionId_at_a_new_independently_mutable_row_property2()
    {
        var (config, draft) = SeedDraft(ValidDraftJson);
        _repo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(config);
        _repo.GetMaxVersionNumberAsync(TenantId, Arg.Any<CancellationToken>()).Returns(1);

        MobileConfigurationVersion? newDraft = null;
        _repo.When(r => r.AddVersionAsync(Arg.Any<MobileConfigurationVersion>(), Arg.Any<CancellationToken>()))
             .Do(ci => newDraft = ci.Arg<MobileConfigurationVersion>());

        await _sut.PublishAsync(TenantId, UserId);

        Assert.NotNull(newDraft);
        Assert.NotEqual(draft.Id, newDraft!.Id);
        Assert.Equal("draft", newDraft.Status);
        Assert.Equal(2, newDraft.Version); // GetMaxVersionNumberAsync stub returns 1 -> next is 2
        Assert.Equal(ValidDraftJson, newDraft.ConfigurationJson); // clones the ORIGINAL theme-less content
        Assert.Equal(config.DraftVersionId, newDraft.Id);
        Assert.NotEqual(config.PublishedVersionId, config.DraftVersionId);

        // The just-published row itself must never be mutated by a later SaveDraftAsync-shaped
        // call — UpdateConfigurationJson throws once Status != Draft.
        Assert.Throws<InvalidOperationException>(() => draft.UpdateConfigurationJson("{}"));
    }

    [Fact]
    public async Task PublishAsync_archives_the_previously_published_version_when_one_exists()
    {
        // TASK-545: this used to prove the OLD, now-superseded contract ("never touches the
        // previous published version" — TASK-544's deliberate deferral, see
        // MobileConfigPublishService's class remarks). TASK-545's DoD explicitly requires the
        // opposite: a superseded published version must be archived, not left dangling as
        // Published forever. This test was updated (not added-around) because it directly asserted
        // the exact old behavior TASK-545 changes.
        var config = MobileConfiguration.Create(TenantId);
        var oldPublished = MobileConfigurationVersion.Create(
            config.Id, TenantId, version: 1, schemaVersion: 1, configurationJson: ValidDraftJson);
        oldPublished.Publish(DateTime.UtcNow.AddDays(-1));
        config.SetPublishedVersion(oldPublished.Id);

        var draft = MobileConfigurationVersion.Create(
            config.Id, TenantId, version: 2, schemaVersion: 1, configurationJson: ValidDraftJson, createdBy: UserId);
        config.SetDraftVersion(draft.Id);
        SetNavigation(config, nameof(MobileConfiguration.DraftVersion), draft);

        _repo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(config);
        _repo.GetVersionByIdAsync(TenantId, oldPublished.Id, Arg.Any<CancellationToken>()).Returns(oldPublished);
        _repo.GetMaxVersionNumberAsync(TenantId, Arg.Any<CancellationToken>()).Returns(2);

        var (published, error) = await _sut.PublishAsync(TenantId, UserId);

        Assert.Null(error);
        Assert.Equal(draft.Id, published!.VersionId); // the NEW version becomes published
        Assert.Equal("archived", oldPublished.Status); // superseded, now archived — not dangling as Published
        Assert.NotEqual(oldPublished.Id, config.PublishedVersionId);

        _repo.Received(1).UpdateVersion(Arg.Is<MobileConfigurationVersion>(v => v.Id == oldPublished.Id));
    }

    [Fact]
    public async Task PublishAsync_does_not_attempt_to_archive_anything_on_the_tenants_first_ever_publish()
    {
        // config.PublishedVersionId is still null here (never published before) — must not call
        // GetVersionByIdAsync/UpdateVersion for a nonexistent "previous" version.
        var (config, draft) = SeedDraft(ValidDraftJson);
        _repo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(config);
        _repo.GetMaxVersionNumberAsync(TenantId, Arg.Any<CancellationToken>()).Returns(1);

        var (published, error) = await _sut.PublishAsync(TenantId, UserId);

        Assert.Null(error);
        Assert.NotNull(published);
        await _repo.DidNotReceive().GetVersionByIdAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        _repo.DidNotReceive().UpdateVersion(Arg.Is<MobileConfigurationVersion>(v => v.Id != draft.Id));
    }

    [Fact]
    public async Task PublishAsync_returns_ConcurrentPublish_when_SaveChangesAsync_detects_a_racing_writer()
    {
        var (config, _) = SeedDraft(ValidDraftJson);
        _repo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(config);
        _repo.GetMaxVersionNumberAsync(TenantId, Arg.Any<CancellationToken>()).Returns(1);
        _repo.SaveChangesAsync(Arg.Any<CancellationToken>())
             .Throws(new ConcurrencyConflictException("row modified concurrently"));

        var (published, error) = await _sut.PublishAsync(TenantId, UserId);

        Assert.Null(published);
        Assert.Equal(MobileConfigPublishErrorType.ConcurrentPublish, error!.Type);
    }

    // ── Audit logging (TASK-550) ────────────────────────────────────────────

    [Fact]
    public async Task PublishAsync_logs_mobileconfig_published_with_the_new_version_number()
    {
        var (config, draft) = SeedDraft(ValidDraftJson);
        _repo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(config);
        _repo.GetMaxVersionNumberAsync(TenantId, Arg.Any<CancellationToken>()).Returns(1);

        var (published, error) = await _sut.PublishAsync(TenantId, UserId);

        Assert.Null(error);
        await _activityLogs.Received(1).LogAsync(
            Arg.Is<ActivityLog>(a =>
                a.TenantId == TenantId && a.UserId == UserId &&
                a.Action == "mobileconfig.published" &&
                a.EntityType == "mobile_configuration_version" &&
                a.EntityId == draft.Id &&
                a.Meta == $"{{\"version\":{published!.Version}}}"),
            Arg.Any<CancellationToken>());
        await _activityLogs.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishAsync_does_not_log_anything_when_validation_fails()
    {
        var (config, _) = SeedDraft("""{ "schemaVersion": 1, "features": {}, "navigation": [] }"""); // missing "pages"
        _repo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(config);

        var (published, error) = await _sut.PublishAsync(TenantId, UserId);

        Assert.Null(published);
        Assert.NotNull(error);
        await _activityLogs.DidNotReceive().LogAsync(Arg.Any<ActivityLog>(), Arg.Any<CancellationToken>());
    }

    // ── RollbackAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task RollbackAsync_returns_VersionNotFound_when_tenant_has_no_configuration_row_at_all()
    {
        _repo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns((MobileConfiguration?)null);

        var (published, error) = await _sut.RollbackAsync(TenantId, Guid.NewGuid(), UserId);

        Assert.Null(published);
        Assert.Equal(MobileConfigPublishErrorType.VersionNotFound, error!.Type);
    }

    [Fact]
    public async Task RollbackAsync_returns_CannotRollbackToCurrentVersion_when_target_is_the_current_published_version()
    {
        var (config, _, currentPublished, _) = SeedRollbackScenario(HistoricalComposedJsonWithTheme);
        _repo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(config);

        var (published, error) = await _sut.RollbackAsync(TenantId, currentPublished.Id, UserId);

        Assert.Null(published);
        Assert.Equal(MobileConfigPublishErrorType.CannotRollbackToCurrentVersion, error!.Type);
        await _repo.DidNotReceive().GetVersionByIdAsync(Arg.Any<Guid>(), currentPublished.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RollbackAsync_returns_CannotRollbackToCurrentVersion_when_target_is_the_current_draft_version()
    {
        var (config, currentDraft, _, _) = SeedRollbackScenario(HistoricalComposedJsonWithTheme);
        _repo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(config);

        var (published, error) = await _sut.RollbackAsync(TenantId, currentDraft.Id, UserId);

        Assert.Null(published);
        Assert.Equal(MobileConfigPublishErrorType.CannotRollbackToCurrentVersion, error!.Type);
    }

    [Fact]
    public async Task RollbackAsync_returns_VersionNotFound_when_target_version_does_not_exist_for_tenant()
    {
        var (config, _, _, _) = SeedRollbackScenario(HistoricalComposedJsonWithTheme);
        _repo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(config);
        var missingId = Guid.NewGuid();
        _repo.GetVersionByIdAsync(TenantId, missingId, Arg.Any<CancellationToken>()).Returns((MobileConfigurationVersion?)null);

        var (published, error) = await _sut.RollbackAsync(TenantId, missingId, UserId);

        Assert.Null(published);
        Assert.Equal(MobileConfigPublishErrorType.VersionNotFound, error!.Type);
    }

    [Fact]
    public async Task RollbackAsync_succeeds_for_a_historical_theme_containing_version_that_naive_whole_document_validation_would_reject()
    {
        // Prove the point contrastively: validating the WHOLE historical document (theme still
        // present) fails, because MobileConfigValidator's top-level whitelist has no "theme" entry —
        // yet RollbackAsync succeeds, because it splits theme out first (MobileThemeJson.SplitTheme)
        // and only validates the remaining theme-less body.
        var naiveValidation = new MobileConfigValidator().Validate(HistoricalComposedJsonWithTheme);
        Assert.False(naiveValidation.IsValid);
        Assert.Contains(naiveValidation.Errors, e => e.Message.Contains("theme"));

        var (config, _, currentPublished, historicalTarget) = SeedRollbackScenario(HistoricalComposedJsonWithTheme);
        _repo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(config);
        _repo.GetVersionByIdAsync(TenantId, historicalTarget.Id, Arg.Any<CancellationToken>()).Returns(historicalTarget);
        _repo.GetVersionByIdAsync(TenantId, currentPublished.Id, Arg.Any<CancellationToken>()).Returns(currentPublished);
        _repo.GetMaxVersionNumberAsync(TenantId, Arg.Any<CancellationToken>()).Returns(3);

        var (published, error) = await _sut.RollbackAsync(TenantId, historicalTarget.Id, UserId);

        Assert.Null(error);
        Assert.NotNull(published);
    }

    [Fact]
    public async Task RollbackAsync_archives_the_currently_published_version()
    {
        var (config, _, currentPublished, historicalTarget) = SeedRollbackScenario(HistoricalComposedJsonWithTheme);
        _repo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(config);
        _repo.GetVersionByIdAsync(TenantId, historicalTarget.Id, Arg.Any<CancellationToken>()).Returns(historicalTarget);
        _repo.GetVersionByIdAsync(TenantId, currentPublished.Id, Arg.Any<CancellationToken>()).Returns(currentPublished);
        _repo.GetMaxVersionNumberAsync(TenantId, Arg.Any<CancellationToken>()).Returns(3);

        var (published, error) = await _sut.RollbackAsync(TenantId, historicalTarget.Id, UserId);

        Assert.Null(error);
        Assert.Equal("archived", currentPublished.Status);
        _repo.Received(1).UpdateVersion(Arg.Is<MobileConfigurationVersion>(v => v.Id == currentPublished.Id));

        // Never a destructive revert: the rollback TARGET itself is left exactly as it was (still
        // archived, never re-mutated) — only a NEW version is published from a clone of its content.
        Assert.Equal("archived", historicalTarget.Status);
        Assert.Equal(HistoricalComposedJsonWithTheme, historicalTarget.ConfigurationJson);
    }

    [Fact]
    public async Task RollbackAsync_clones_the_theme_less_restored_body_into_a_new_draft_property2()
    {
        var (config, _, currentPublished, historicalTarget) = SeedRollbackScenario(HistoricalComposedJsonWithTheme);
        _repo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(config);
        _repo.GetVersionByIdAsync(TenantId, historicalTarget.Id, Arg.Any<CancellationToken>()).Returns(historicalTarget);
        _repo.GetVersionByIdAsync(TenantId, currentPublished.Id, Arg.Any<CancellationToken>()).Returns(currentPublished);
        _repo.GetMaxVersionNumberAsync(TenantId, Arg.Any<CancellationToken>()).Returns(3);

        MobileConfigurationVersion? newDraft = null;
        _repo.When(r => r.AddVersionAsync(Arg.Any<MobileConfigurationVersion>(), Arg.Any<CancellationToken>()))
             .Do(ci => newDraft = ci.Arg<MobileConfigurationVersion>());

        await _sut.RollbackAsync(TenantId, historicalTarget.Id, UserId);

        Assert.NotNull(newDraft);
        Assert.Equal("draft", newDraft!.Status);
        Assert.Equal(historicalTarget.SchemaVersion, newDraft.SchemaVersion);
        using var doc = JsonDocument.Parse(newDraft.ConfigurationJson);
        Assert.False(doc.RootElement.TryGetProperty("theme", out _)); // property #2 still holds after rollback
    }

    [Fact]
    public async Task RollbackAsync_restores_the_historical_theme_so_a_subsequent_normal_publish_does_not_regress_it()
    {
        var (config, currentDraftBeforeRollback, currentPublished, historicalTarget) =
            SeedRollbackScenario(HistoricalComposedJsonWithTheme);

        // The tenant's CURRENT live theme, pre-rollback, deliberately different from the historical
        // one — this is the "stale" value that must NOT leak back in on the next normal publish.
        var staleTheme = MobileTheme.CreateDefault(config.Id, TenantId);
        staleTheme.Update(
            logoUrl: "https://cdn/stale-logo.png", primaryColor: "#000000", secondaryColor: "#000001",
            backgroundColor: "#000002", surfaceColor: "#000003", textPrimaryColor: "#000004",
            textSecondaryColor: "#000005", buttonRadius: 1, cardRadius: 2, spacingPreset: "compact");
        SetNavigation(config, nameof(MobileConfiguration.Theme), staleTheme);

        _repo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(config);
        _repo.GetVersionByIdAsync(TenantId, historicalTarget.Id, Arg.Any<CancellationToken>()).Returns(historicalTarget);
        _repo.GetVersionByIdAsync(TenantId, currentPublished.Id, Arg.Any<CancellationToken>()).Returns(currentPublished);
        // After rollback, PublishedVersionId points at the repurposed old draft row — the next
        // normal publish's own archive-previous step needs to find it too.
        _repo.GetVersionByIdAsync(TenantId, currentDraftBeforeRollback.Id, Arg.Any<CancellationToken>())
             .Returns(currentDraftBeforeRollback);
        _repo.GetMaxVersionNumberAsync(TenantId, Arg.Any<CancellationToken>()).Returns(3);

        MobileConfigurationVersion? cloneAfterRollback = null;
        _repo.When(r => r.AddVersionAsync(Arg.Any<MobileConfigurationVersion>(), Arg.Any<CancellationToken>()))
             .Do(ci => cloneAfterRollback = ci.Arg<MobileConfigurationVersion>());

        var (rolledBack, rollbackError) = await _sut.RollbackAsync(TenantId, historicalTarget.Id, UserId);

        Assert.Null(rollbackError);
        using (var rolledBackDoc = JsonDocument.Parse(rolledBack!.ConfigurationJson))
        {
            Assert.Equal(
                "#AAAAAA",
                rolledBackDoc.RootElement.GetProperty("theme").GetProperty("colors").GetProperty("primary").GetString());
        }

        // The live MobileTheme row itself was overwritten in place with the historical values — not
        // just the rollback's own published document.
        Assert.Equal("#AAAAAA", staleTheme.PrimaryColor);
        Assert.Equal("https://cdn/historical-logo.png", staleTheme.LogoUrl);
        _repo.Received(1).UpdateTheme(Arg.Is<MobileTheme>(t => t.PrimaryColor == "#AAAAAA"));

        // Simulate a fresh reload before the next request — MobileConfiguration.DraftVersion is a
        // nav property only EF's real Include() fixes up; every other test in this file does the
        // same reflection-based fixup for this reason (see SetNavigation's remarks below).
        Assert.NotNull(cloneAfterRollback);
        SetNavigation(config, nameof(MobileConfiguration.DraftVersion), cloneAfterRollback);

        // THE REQUIRED PROOF: a SUBSEQUENT NORMAL publish — which always composes theme from
        // MobileTheme's CURRENT values — must reflect the restored theme, not the pre-rollback
        // stale one. Skipping the theme-restoration step would make this assert "#000000" instead.
        _repo.GetMaxVersionNumberAsync(TenantId, Arg.Any<CancellationToken>()).Returns(4);
        var (secondPublish, secondError) = await _sut.PublishAsync(TenantId, UserId);

        Assert.Null(secondError);
        using var secondDoc = JsonDocument.Parse(secondPublish!.ConfigurationJson);
        Assert.Equal(
            "#AAAAAA",
            secondDoc.RootElement.GetProperty("theme").GetProperty("colors").GetProperty("primary").GetString());
    }

    [Fact]
    public async Task RollbackAsync_leaves_the_live_theme_untouched_when_the_historical_target_predates_theme_composition()
    {
        // Defensive/legacy path: a target whose ConfigurationJson has no "theme" key at all (predates
        // the TASK-544 reconciliation, or seeded directly). Must not fail, and must not touch the
        // tenant's live MobileTheme.
        const string legacyJsonWithoutTheme = ValidDraftJson;
        var (config, _, currentPublished, _) = SeedRollbackScenario(HistoricalComposedJsonWithTheme);
        var legacyTarget = MobileConfigurationVersion.Create(
            config.Id, TenantId, version: 0, schemaVersion: 1, configurationJson: legacyJsonWithoutTheme);
        legacyTarget.Publish(DateTime.UtcNow.AddDays(-60));
        legacyTarget.Archive();

        _repo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(config);
        _repo.GetVersionByIdAsync(TenantId, legacyTarget.Id, Arg.Any<CancellationToken>()).Returns(legacyTarget);
        _repo.GetVersionByIdAsync(TenantId, currentPublished.Id, Arg.Any<CancellationToken>()).Returns(currentPublished);
        _repo.GetMaxVersionNumberAsync(TenantId, Arg.Any<CancellationToken>()).Returns(3);

        var (published, error) = await _sut.RollbackAsync(TenantId, legacyTarget.Id, UserId);

        Assert.Null(error);
        Assert.NotNull(published);
        _repo.DidNotReceive().UpdateTheme(Arg.Any<MobileTheme>());
        await _repo.DidNotReceive().AddThemeAsync(Arg.Any<MobileTheme>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RollbackAsync_returns_ConcurrentPublish_when_SaveChangesAsync_detects_a_racing_writer()
    {
        var (config, _, currentPublished, historicalTarget) = SeedRollbackScenario(HistoricalComposedJsonWithTheme);
        _repo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(config);
        _repo.GetVersionByIdAsync(TenantId, historicalTarget.Id, Arg.Any<CancellationToken>()).Returns(historicalTarget);
        _repo.GetVersionByIdAsync(TenantId, currentPublished.Id, Arg.Any<CancellationToken>()).Returns(currentPublished);
        _repo.GetMaxVersionNumberAsync(TenantId, Arg.Any<CancellationToken>()).Returns(3);
        _repo.SaveChangesAsync(Arg.Any<CancellationToken>())
             .Throws(new ConcurrencyConflictException("row modified concurrently"));

        var (published, error) = await _sut.RollbackAsync(TenantId, historicalTarget.Id, UserId);

        Assert.Null(published);
        Assert.Equal(MobileConfigPublishErrorType.ConcurrentPublish, error!.Type);
    }

    [Fact]
    public async Task RollbackAsync_throws_when_the_tenant_unexpectedly_has_no_current_draft_row()
    {
        // Defensive-only path: PublishAsync/RollbackAsync always leave a fresh draft behind, so a
        // tenant with rollback-able history should never actually reach this state. Covered so the
        // failure mode is a loud, explicit exception rather than a silent NullReferenceException.
        var config = MobileConfiguration.Create(TenantId);
        var currentPublished = MobileConfigurationVersion.Create(
            config.Id, TenantId, version: 1, schemaVersion: 1, configurationJson: ValidDraftJson);
        currentPublished.Publish(DateTime.UtcNow.AddDays(-5));
        config.SetPublishedVersion(currentPublished.Id);
        // config.DraftVersionId deliberately left null.

        var historicalTarget = MobileConfigurationVersion.Create(
            config.Id, TenantId, version: 0, schemaVersion: 1, configurationJson: HistoricalComposedJsonWithTheme);

        _repo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(config);
        _repo.GetVersionByIdAsync(TenantId, historicalTarget.Id, Arg.Any<CancellationToken>()).Returns(historicalTarget);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.RollbackAsync(TenantId, historicalTarget.Id, UserId));
    }

    [Fact]
    public async Task RollbackAsync_logs_mobileconfig_rolled_back_recording_which_historical_version_was_restored()
    {
        var (config, _, currentPublished, historicalTarget) = SeedRollbackScenario(HistoricalComposedJsonWithTheme);
        _repo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(config);
        _repo.GetVersionByIdAsync(TenantId, historicalTarget.Id, Arg.Any<CancellationToken>()).Returns(historicalTarget);
        _repo.GetVersionByIdAsync(TenantId, currentPublished.Id, Arg.Any<CancellationToken>()).Returns(currentPublished);
        _repo.GetMaxVersionNumberAsync(TenantId, Arg.Any<CancellationToken>()).Returns(3);

        var (published, error) = await _sut.RollbackAsync(TenantId, historicalTarget.Id, UserId);

        Assert.Null(error);
        await _activityLogs.Received(1).LogAsync(
            Arg.Is<ActivityLog>(a =>
                a.TenantId == TenantId && a.UserId == UserId &&
                a.Action == "mobileconfig.rolled_back" &&
                a.EntityType == "mobile_configuration_version" &&
                a.EntityId == published!.VersionId &&
                a.Meta!.Contains($"\"rolledBackToVersion\":{historicalTarget.Version}") &&
                a.Meta.Contains($"\"rolledBackToVersionId\":\"{historicalTarget.Id}\"")),
            Arg.Any<CancellationToken>());
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static (MobileConfiguration Config, MobileConfigurationVersion Draft) SeedDraft(string configurationJson)
    {
        var config = MobileConfiguration.Create(TenantId);
        var draft = MobileConfigurationVersion.Create(
            config.Id, TenantId, version: 1, schemaVersion: 1, configurationJson: configurationJson, createdBy: UserId);
        config.SetDraftVersion(draft.Id);
        SetNavigation(config, nameof(MobileConfiguration.DraftVersion), draft);
        return (config, draft);
    }

    /// <summary>
    /// A tenant with a realistic rollback-able history: an old ARCHIVED version (the rollback
    /// target), a CURRENT published version (must get archived when the rollback supersedes it),
    /// and a CURRENT draft (the row RollbackAsync repurposes into the newly-published version).
    /// </summary>
    private static (MobileConfiguration Config, MobileConfigurationVersion CurrentDraft,
        MobileConfigurationVersion CurrentPublished, MobileConfigurationVersion HistoricalTarget)
        SeedRollbackScenario(string historicalConfigurationJson)
    {
        var config = MobileConfiguration.Create(TenantId);

        var historicalTarget = MobileConfigurationVersion.Create(
            config.Id, TenantId, version: 1, schemaVersion: 1, configurationJson: historicalConfigurationJson);
        historicalTarget.Publish(DateTime.UtcNow.AddDays(-30));
        historicalTarget.Archive(); // superseded long ago by currentPublished below

        var currentPublished = MobileConfigurationVersion.Create(
            config.Id, TenantId, version: 2, schemaVersion: 1, configurationJson: ValidDraftJson);
        currentPublished.Publish(DateTime.UtcNow.AddDays(-1));
        config.SetPublishedVersion(currentPublished.Id);

        var currentDraft = MobileConfigurationVersion.Create(
            config.Id, TenantId, version: 3, schemaVersion: 1, configurationJson: ValidDraftJson, createdBy: UserId);
        config.SetDraftVersion(currentDraft.Id);
        SetNavigation(config, nameof(MobileConfiguration.DraftVersion), currentDraft);

        return (config, currentDraft, currentPublished, historicalTarget);
    }

    // MobileConfiguration.DraftVersion/Theme have private setters (only fixed up by EF Core's real
    // Include() during materialization) — tests simulate that same post-load shape via reflection,
    // same approach MobileConfigDraftServiceTests/MobileConfigPublishedReadServiceTests use.
    private static void SetNavigation(MobileConfiguration config, string propertyName, object? value)
    {
        typeof(MobileConfiguration).GetProperty(propertyName)!.SetValue(config, value);
    }
}
