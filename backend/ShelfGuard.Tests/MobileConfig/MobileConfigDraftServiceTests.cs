using NSubstitute;
using ShelfGuard.Application.Features.MobileConfig;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.MobileConfig;

/// <summary>
/// TASK-532 — Draft CRUD orchestration: get-or-create the <see cref="MobileConfiguration"/> root
/// row, create-vs-mutate-in-place draft version, tenant isolation, and validation-failure short
/// circuiting. Whitelist rule coverage lives in <c>MobileConfigValidatorTests</c> — this suite
/// wires up the real <see cref="MobileConfigValidator"/> (no mock) since these tests care about
/// service orchestration, not re-deriving validation rules.
/// </summary>
public sealed class MobileConfigDraftServiceTests
{
    private readonly IMobileConfigurationRepository _repo = Substitute.For<IMobileConfigurationRepository>();
    private readonly IActivityLogRepository _activityLogs = Substitute.For<IActivityLogRepository>();
    private readonly MobileConfigDraftService _sut;

    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private const string ValidDocumentV1 = """
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

    private const string ValidDocumentV2 = """
    {
      "schemaVersion": 1,
      "features": { "loyalty": true, "news": true },
      "navigation": [
        { "type": "home", "label": "A", "icon": "home" },
        { "type": "profile", "label": "B", "icon": "user" }
      ],
      "pages": {}
    }
    """;

    public MobileConfigDraftServiceTests()
    {
        _sut = new MobileConfigDraftService(_repo, new MobileConfigValidator(), _activityLogs);
    }

    [Fact]
    public async Task SaveDraftAsync_rejects_invalid_payload_and_persists_nothing()
    {
        var (draft, errors) = await _sut.SaveDraftAsync(TenantA, UserId, "{ not json");

        Assert.Null(draft);
        Assert.NotEmpty(errors);
        await _repo.DidNotReceive().AddAsync(Arg.Any<MobileConfiguration>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().AddVersionAsync(Arg.Any<MobileConfigurationVersion>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveDraftAsync_creates_MobileConfiguration_and_first_draft_version_when_none_exists()
    {
        _repo.GetByTenantIdAsync(TenantA, Arg.Any<CancellationToken>()).Returns((MobileConfiguration?)null);
        _repo.GetMaxVersionNumberAsync(TenantA, Arg.Any<CancellationToken>()).Returns(0);

        var (draft, errors) = await _sut.SaveDraftAsync(TenantA, UserId, ValidDocumentV1);

        Assert.Empty(errors);
        Assert.NotNull(draft);
        Assert.Equal(1, draft!.Version);
        Assert.Equal("draft", draft.Status);
        Assert.Equal(UserId, draft.CreatedBy);
        Assert.Equal(ValidDocumentV1, draft.ConfigurationJson);

        await _repo.Received(1).AddAsync(
            Arg.Is<MobileConfiguration>(c => c.TenantId == TenantA), Arg.Any<CancellationToken>());
        await _repo.Received(1).AddVersionAsync(
            Arg.Is<MobileConfigurationVersion>(v => v.TenantId == TenantA && v.Version == 1),
            Arg.Any<CancellationToken>());
        _repo.Received(1).Update(Arg.Is<MobileConfiguration>(c => c.DraftVersionId != null));
        // Two SaveChangesAsync calls, not one (TASK-534b): the new MobileConfiguration and
        // MobileConfigurationVersion rows must be inserted before the pointer is set, to avoid a
        // circular-dependency error against real Postgres — see MobileConfigDraftService's
        // remarks on this branch.
        await _repo.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveDraftAsync_mutates_the_existing_draft_in_place_instead_of_creating_a_new_version()
    {
        var config = MobileConfiguration.Create(TenantA);
        var existingDraft = MobileConfigurationVersion.Create(
            config.Id, TenantA, version: 1, schemaVersion: 1, configurationJson: ValidDocumentV1, createdBy: UserId);
        config.SetDraftVersion(existingDraft.Id);
        SetDraftVersionNavigation(config, existingDraft);

        _repo.GetByTenantIdAsync(TenantA, Arg.Any<CancellationToken>()).Returns(config);

        var (draft, errors) = await _sut.SaveDraftAsync(TenantA, UserId, ValidDocumentV2);

        Assert.Empty(errors);
        Assert.NotNull(draft);
        Assert.Equal(existingDraft.Id, draft!.VersionId);
        Assert.Equal(1, draft.Version); // unchanged — same row, not a new version
        Assert.Equal(ValidDocumentV2, draft.ConfigurationJson);

        await _repo.DidNotReceive().AddVersionAsync(Arg.Any<MobileConfigurationVersion>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().GetMaxVersionNumberAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        _repo.Received(1).UpdateVersion(Arg.Is<MobileConfigurationVersion>(v => v.Id == existingDraft.Id));
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetDraftAsync_returns_null_when_tenant_has_no_configuration_yet()
    {
        _repo.GetByTenantIdAsync(TenantA, Arg.Any<CancellationToken>()).Returns((MobileConfiguration?)null);

        var draft = await _sut.GetDraftAsync(TenantA);

        Assert.Null(draft);
    }

    [Fact]
    public async Task GetDraftAsync_returns_null_when_configuration_exists_but_has_no_draft()
    {
        var config = MobileConfiguration.Create(TenantA);
        _repo.GetByTenantIdAsync(TenantA, Arg.Any<CancellationToken>()).Returns(config);

        var draft = await _sut.GetDraftAsync(TenantA);

        Assert.Null(draft);
    }

    [Fact]
    public async Task Draft_lifecycle_create_then_update_then_read_round_trips()
    {
        // Create
        _repo.GetByTenantIdAsync(TenantA, Arg.Any<CancellationToken>()).Returns((MobileConfiguration?)null);
        _repo.GetMaxVersionNumberAsync(TenantA, Arg.Any<CancellationToken>()).Returns(0);

        MobileConfiguration? persistedConfig = null;
        MobileConfigurationVersion? persistedVersion = null;
        _repo.When(r => r.AddAsync(Arg.Any<MobileConfiguration>(), Arg.Any<CancellationToken>()))
             .Do(ci => persistedConfig = ci.Arg<MobileConfiguration>());
        _repo.When(r => r.AddVersionAsync(Arg.Any<MobileConfigurationVersion>(), Arg.Any<CancellationToken>()))
             .Do(ci => persistedVersion = ci.Arg<MobileConfigurationVersion>());

        var (created, createErrors) = await _sut.SaveDraftAsync(TenantA, UserId, ValidDocumentV1);
        Assert.Empty(createErrors);
        Assert.NotNull(created);
        Assert.NotNull(persistedConfig);
        Assert.NotNull(persistedVersion);

        // Simulate the round trip: a fresh load after Create returns the config with its new
        // draft eagerly loaded — same shape MobileConfigurationRepository's real Include produces.
        persistedConfig!.SetDraftVersion(persistedVersion!.Id);
        SetDraftVersionNavigation(persistedConfig, persistedVersion);
        _repo.GetByTenantIdAsync(TenantA, Arg.Any<CancellationToken>()).Returns(persistedConfig);

        // Update
        var (updated, updateErrors) = await _sut.SaveDraftAsync(TenantA, UserId, ValidDocumentV2);
        Assert.Empty(updateErrors);
        Assert.Equal(created!.VersionId, updated!.VersionId);
        Assert.Equal(ValidDocumentV2, updated.ConfigurationJson);

        // Read
        var read = await _sut.GetDraftAsync(TenantA);
        Assert.NotNull(read);
        Assert.Equal(updated.VersionId, read!.VersionId);
        Assert.Equal(ValidDocumentV2, read.ConfigurationJson);
    }

    [Fact]
    public async Task Tenant_isolation_one_tenants_draft_never_touches_another_tenants_configuration()
    {
        var configA = MobileConfiguration.Create(TenantA);
        var draftA = MobileConfigurationVersion.Create(configA.Id, TenantA, 1, 1, ValidDocumentV1, UserId);
        configA.SetDraftVersion(draftA.Id);
        SetDraftVersionNavigation(configA, draftA);

        var configB = MobileConfiguration.Create(TenantB);
        // TenantB has no draft yet.

        _repo.GetByTenantIdAsync(TenantA, Arg.Any<CancellationToken>()).Returns(configA);
        _repo.GetByTenantIdAsync(TenantB, Arg.Any<CancellationToken>()).Returns(configB);
        _repo.GetMaxVersionNumberAsync(TenantB, Arg.Any<CancellationToken>()).Returns(0);

        var draftForA = await _sut.GetDraftAsync(TenantA);
        var draftForB = await _sut.GetDraftAsync(TenantB);

        Assert.NotNull(draftForA);
        Assert.Equal(draftA.Id, draftForA!.VersionId);
        Assert.Null(draftForB);

        // Saving TenantB's draft must never create/touch a version scoped to TenantA.
        var (savedB, errorsB) = await _sut.SaveDraftAsync(TenantB, UserId, ValidDocumentV1);
        Assert.Empty(errorsB);
        Assert.NotEqual(draftA.Id, savedB!.VersionId);
        await _repo.Received(1).AddVersionAsync(
            Arg.Is<MobileConfigurationVersion>(v => v.TenantId == TenantB), Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().AddVersionAsync(
            Arg.Is<MobileConfigurationVersion>(v => v.TenantId == TenantA), Arg.Any<CancellationToken>());
    }

    // ── Audit logging (TASK-550) ────────────────────────────────────────────

    [Fact]
    public async Task SaveDraftAsync_logs_draft_saved_but_not_feature_flags_changed_on_the_tenants_first_ever_draft()
    {
        _repo.GetByTenantIdAsync(TenantA, Arg.Any<CancellationToken>()).Returns((MobileConfiguration?)null);
        _repo.GetMaxVersionNumberAsync(TenantA, Arg.Any<CancellationToken>()).Returns(0);

        var (draft, errors) = await _sut.SaveDraftAsync(TenantA, UserId, ValidDocumentV1);

        Assert.Empty(errors);
        await _activityLogs.Received(1).LogAsync(
            Arg.Is<ActivityLog>(a =>
                a.TenantId == TenantA && a.UserId == UserId &&
                a.Action == "mobileconfig.draft_saved" &&
                a.EntityType == "mobile_configuration_version" &&
                a.EntityId == draft!.VersionId),
            Arg.Any<CancellationToken>());
        // Nothing to diff against on a brand-new tenant's very first draft — "features" are being
        // created, not "changed".
        await _activityLogs.DidNotReceive().LogAsync(
            Arg.Is<ActivityLog>(a => a.Action == "mobileconfig.feature_flags_changed"),
            Arg.Any<CancellationToken>());
        await _activityLogs.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveDraftAsync_logs_feature_flags_changed_with_only_the_changed_keys_when_features_actually_differ()
    {
        var config = MobileConfiguration.Create(TenantA);
        var existingDraft = MobileConfigurationVersion.Create(
            config.Id, TenantA, version: 1, schemaVersion: 1, configurationJson: ValidDocumentV1, createdBy: UserId);
        config.SetDraftVersion(existingDraft.Id);
        SetDraftVersionNavigation(config, existingDraft);
        _repo.GetByTenantIdAsync(TenantA, Arg.Any<CancellationToken>()).Returns(config);

        // ValidDocumentV1 features: { loyalty: true }. ValidDocumentV2 features: { loyalty: true, news: true }.
        var (draft, errors) = await _sut.SaveDraftAsync(TenantA, UserId, ValidDocumentV2);

        Assert.Empty(errors);
        await _activityLogs.Received(1).LogAsync(
            Arg.Is<ActivityLog>(a =>
                a.Action == "mobileconfig.feature_flags_changed" &&
                a.EntityId == draft!.VersionId &&
                a.Meta == "{\"news\":true}"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveDraftAsync_does_not_log_feature_flags_changed_when_the_features_object_is_unchanged()
    {
        var config = MobileConfiguration.Create(TenantA);
        var existingDraft = MobileConfigurationVersion.Create(
            config.Id, TenantA, version: 1, schemaVersion: 1, configurationJson: ValidDocumentV1, createdBy: UserId);
        config.SetDraftVersion(existingDraft.Id);
        SetDraftVersionNavigation(config, existingDraft);
        _repo.GetByTenantIdAsync(TenantA, Arg.Any<CancellationToken>()).Returns(config);

        // Re-saving the SAME document — only non-feature content (if any) would differ in a real
        // edit; here it's byte-identical, so features definitely did not change.
        var (_, errors) = await _sut.SaveDraftAsync(TenantA, UserId, ValidDocumentV1);

        Assert.Empty(errors);
        await _activityLogs.Received(1).LogAsync(
            Arg.Is<ActivityLog>(a => a.Action == "mobileconfig.draft_saved"), Arg.Any<CancellationToken>());
        await _activityLogs.DidNotReceive().LogAsync(
            Arg.Is<ActivityLog>(a => a.Action == "mobileconfig.feature_flags_changed"),
            Arg.Any<CancellationToken>());
    }

    // MobileConfiguration.DraftVersion has a private setter (only fixed up by EF Core's real
    // Include() during materialization) — tests simulate that same post-load shape via reflection
    // rather than adding a test-only public mutator to the domain entity.
    private static void SetDraftVersionNavigation(MobileConfiguration config, MobileConfigurationVersion draft)
    {
        typeof(MobileConfiguration)
            .GetProperty(nameof(MobileConfiguration.DraftVersion))!
            .SetValue(config, draft);
    }
}
