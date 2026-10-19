using System.Text.Json;
using NSubstitute;
using ShelfGuard.Application.Features.Promotions;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Promotions;

public sealed class PromotionRuleServiceTests
{
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _storeA = Guid.NewGuid();
    private readonly Guid _storeB = Guid.NewGuid();
    private readonly Guid _categoryId = Guid.NewGuid();
    private readonly Guid _tierId = Guid.NewGuid();
    private readonly Item _item;
    private readonly InMemoryRuleRepo _repo = new();
    private readonly PromotionRuleService _svc;
    private readonly List<Item> _catalog = [];
    private readonly IItemRepository _itemsRepo = Substitute.For<IItemRepository>();
    private readonly IUserLocationRepository _userLocations = Substitute.For<IUserLocationRepository>();

    public PromotionRuleServiceTests()
    {
        _item = new Item { TenantId = _tenant, Name = "Milk", Barcodes = ["4820000000001"], PriceRetail = 30m, CategoryId = _categoryId };
        var items = _itemsRepo;
        var catalog = _catalog;
        catalog.Add(_item);
        items.SearchByBarcodeFragmentAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ci => catalog.Where(i => i.TenantId == ci.ArgAt<Guid>(0) && i.IsActive
                && i.Barcodes.Any(b => b.Contains(ci.ArgAt<string>(1)) || b.TrimStart('0') == ci.ArgAt<string>(1).TrimStart('0'))).ToList());
        items.GetPagedAsync(
                Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<IReadOnlyList<Guid>?>(), Arg.Any<string?>(), Arg.Any<bool?>(), Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<decimal?>(), Arg.Any<decimal?>(), Arg.Any<bool?>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var ids = ci.ArgAt<IReadOnlyList<Guid>?>(4);
                var search = ci.ArgAt<string?>(3);
                var cat = ci.ArgAt<Guid?>(0);
                var q = catalog.AsEnumerable();
                if (ids is { Count: > 0 }) q = q.Where(i => ids.Contains(i.Id));
                if (search is not null) q = q.Where(i => i.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || i.Barcodes.Contains(search));
                if (cat is not null) q = q.Where(i => i.CategoryId == cat);
                var list = q.ToList();
                return (list, list.Count);
            });

        var categories = Substitute.For<ICategoryRepository>();
        categories.ActiveExistsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(ci => ci.Arg<Guid>() == _categoryId);

        var locations = Substitute.For<ILocationRepository>();
        locations.GetAllAsync(Arg.Any<CancellationToken>()).Returns(
            [new Location { Id = _storeA, TenantId = _tenant, Name = "A" }, new Location { Id = _storeB, TenantId = _tenant, Name = "B" }]);

        var loyalty = Substitute.For<ILoyaltyRepository>();
        loyalty.GetTierLadderAsync(_tenant, Arg.Any<CancellationToken>())
            .Returns([new LoyaltyTierDefinition { Id = _tierId, TenantId = _tenant, Name = "Gold" }]);

        var users = Substitute.For<IUserRepository>();
        users.GetByIdAsync(_userId, Arg.Any<CancellationToken>())
            .Returns(User.Create(_tenant, "boss@test.io", "Boss Person", "pw", AppRoles.EnterpriseAdmin, null));

        _userLocations.GetLocationIdsForUserAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Guid>>([]));

        _svc = new PromotionRuleService(_repo, items, categories, locations, loyalty, users, _userLocations);
    }

    private PromotionRuleCaller Admin => new(_tenant, _userId, AppRoles.EnterpriseAdmin, null);

    private PromotionRuleCaller ManagerOf(Guid store)
    {
        _userLocations.GetLocationIdsForUserAsync(_tenant, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Guid>>([store]));
        return new PromotionRuleCaller(_tenant, Guid.NewGuid(), AppRoles.StoreManager, store);
    }

    private JsonElement Def(Action<PromotionDefinition>? edit = null)
    {
        var def = new PromotionDefinition
        {
            Type = PromotionRuleTypes.PercentOff,
            Targets = new PromotionTargets { ItemIds = [_item.Id] },
            Scope = new PromotionScope { StoreIds = [_storeA] },
            Params = new PromotionParams { Percent = 10m },
        };
        edit?.Invoke(def);
        return JsonSerializer.SerializeToElement(def, PromotionDefinitionJson.Options);
    }

    private async Task<RuleDetailDto> CreateAsync(PromotionRuleCaller? caller = null, Action<PromotionDefinition>? edit = null, string name = "Rule")
    {
        var r = await _svc.CreateAsync(caller ?? Admin, new CreatePromotionRuleRequest(name, null, Def(edit)));
        Assert.True(r.Ok, r.Error);
        return r.Value!;
    }

    // ── versions + lifecycle ───────────────────────────────────────────────

    [Fact]
    public async Task Create_makes_a_draft_v1_with_creator_name()
    {
        var r = await _svc.CreateAsync(Admin, new CreatePromotionRuleRequest(" Sale ", "d", Def()));
        Assert.Equal(201, r.Status);
        var d = r.Value!;
        Assert.Equal(("Sale", "draft", 1), (d.Name, d.Status, d.CurrentVersion.VersionNumber));
        Assert.Null(d.PublishedVersion);
        Assert.Equal("Boss Person", d.CurrentVersion.CreatedByName);
        Assert.Equal("percent_off", d.CurrentVersion.Definition.GetProperty("type").GetString());
        Assert.Single(d.Versions);
    }

    [Fact]
    public async Task Create_rejects_blank_name_and_missing_definition()
    {
        var blank = await _svc.CreateAsync(Admin, new CreatePromotionRuleRequest("  ", null, Def()));
        Assert.Equal(400, blank.Status);
        var none = await _svc.CreateAsync(Admin, new CreatePromotionRuleRequest("x", null, default));
        Assert.Equal(400, none.Status);
    }

    [Fact]
    public async Task Update_of_an_unpublished_draft_replaces_it_without_piling_up_versions()
    {
        var created = await CreateAsync();
        var r = await _svc.UpdateAsync(Admin, created.Id,
            new UpdatePromotionRuleRequest("Renamed", null, Def(d => d.Params.Percent = 20m), "tweak"));
        var d2 = r.Value!;
        Assert.Equal(("Renamed", 1), (d2.Name, d2.CurrentVersion.VersionNumber));
        Assert.Equal("tweak", d2.CurrentVersion.ChangeComment);
        Assert.Single(d2.Versions); // superseded draft v1 deleted
        Assert.Single(_repo.Versions);

        var r3 = await _svc.UpdateAsync(Admin, created.Id, new UpdatePromotionRuleRequest("Renamed", null, Def(), null));
        Assert.Equal(1, r3.Value!.CurrentVersion.VersionNumber); // a draft keeps its number
        Assert.Single(_repo.Versions);
    }

    [Fact]
    public async Task Update_of_a_published_rule_adds_a_new_draft_and_keeps_the_published_version_live()
    {
        var created = await CreateAsync();
        var pub = await _svc.PublishAsync(Admin, created.Id);
        Assert.Equal("published", pub.Value!.Status);
        Assert.Equal(1, pub.Value.PublishedVersion!.VersionNumber);

        var upd = (await _svc.UpdateAsync(Admin, created.Id,
            new UpdatePromotionRuleRequest("R", null, Def(d => d.Params.Percent = 25m), null))).Value!;
        Assert.Equal("published", upd.Status);
        Assert.Equal((2, 1), (upd.CurrentVersion.VersionNumber, upd.PublishedVersion!.VersionNumber));
        Assert.Equal(2, upd.Versions.Count);
        Assert.Equal(10m, upd.PublishedVersion.Definition.GetProperty("params").GetProperty("percent").GetDecimal());

        var listed = Assert.Single(await _svc.ListAsync(Admin, null, null));
        Assert.True(listed.HasUnpublishedChanges);
        Assert.Equal((2, 1), (listed.CurrentVersionNumber, listed.PublishedVersionNumber));

        // A second edit replaces the unpublished v2, never the published v1.
        var again = (await _svc.UpdateAsync(Admin, created.Id,
            new UpdatePromotionRuleRequest("R", null, Def(d => d.Params.Percent = 30m), null))).Value!;
        Assert.Equal((2, 1), (again.CurrentVersion.VersionNumber, again.PublishedVersion!.VersionNumber));
        Assert.Equal(2, again.Versions.Count);

        var republished = (await _svc.PublishAsync(Admin, created.Id)).Value!;
        Assert.Equal(2, republished.PublishedVersion!.VersionNumber);
        Assert.False(Assert.Single(await _svc.ListAsync(Admin, null, null)).HasUnpublishedChanges);
    }

    [Fact]
    public async Task Pause_resume_archive_transitions()
    {
        var id = (await CreateAsync()).Id;

        Assert.Equal(400, (await _svc.PauseAsync(Admin, id)).Status);   // draft cannot be paused
        Assert.Equal(400, (await _svc.ResumeAsync(Admin, id)).Status);  // draft cannot be resumed
        await _svc.PublishAsync(Admin, id);
        Assert.Equal("paused", (await _svc.PauseAsync(Admin, id)).Value!.Status);
        Assert.Empty(await _repo.GetPublishedAsync(_tenant));            // paused rules are not synced
        Assert.Equal(400, (await _svc.PauseAsync(Admin, id)).Status);   // already paused
        var resumed = (await _svc.ResumeAsync(Admin, id)).Value!;
        Assert.Equal("published", resumed.Status);
        Assert.NotNull(resumed.PublishedVersion);
        Assert.Single(await _repo.GetPublishedAsync(_tenant));

        Assert.Equal("archived", (await _svc.ArchiveAsync(Admin, id)).Value!.Status);
        Assert.Empty(await _repo.GetPublishedAsync(_tenant));
        Assert.Equal("archived", (await _svc.ArchiveAsync(Admin, id)).Value!.Status); // idempotent
        Assert.Equal(400, (await _svc.PublishAsync(Admin, id)).Status);                // archived cannot publish
        Assert.Equal(400, (await _svc.UpdateAsync(Admin, id, new UpdatePromotionRuleRequest("x", null, Def(), null))).Status);
    }

    [Fact]
    public async Task Publish_with_errors_returns_the_validation_body_and_does_not_publish()
    {
        var id = (await CreateAsync(edit: d => d.Targets = new PromotionTargets())).Id;
        var r = await _svc.PublishAsync(Admin, id);
        Assert.Equal(400, r.Status);
        Assert.Contains(r.Validation!.Errors, e => e.Code == "no_targets");
        Assert.Equal("draft", (await _svc.GetAsync(Admin, id)).Value!.Status);
    }

    [Fact]
    public async Task Unknown_rule_and_foreign_tenant_rule_are_404()
    {
        Assert.Equal(404, (await _svc.GetAsync(Admin, Guid.NewGuid())).Status);
        var d = await CreateAsync();
        var foreign = new PromotionRuleCaller(Guid.NewGuid(), _userId, AppRoles.EnterpriseAdmin, null);
        Assert.Equal(404, (await _svc.GetAsync(foreign, d.Id)).Status);
    }

    // ── validation codes ───────────────────────────────────────────────────

    public static IEnumerable<object[]> ErrorCases() =>
    [
        ["no_targets", "targets"],
        ["missing_param", "params.percent"],
        ["percent_out_of_range", "params.percent"],
        ["price_below_minimum", "params.price"],
        ["invalid_quantity", "params.buyQty"],
        ["invalid_period", "scope.validTo"],
        ["unknown_item", "targets.itemIds"],
        ["unknown_category", "targets.categoryIds"],
        ["unknown_store", "scope.storeIds"],
        ["unknown_tier", "audience.tierIds"],
        ["tier_required", "audience.tierIds"],
    ];

    [Theory]
    [MemberData(nameof(ErrorCases))]
    public async Task Validate_emits_each_error_code(string code, string field)
    {
        Action<PromotionDefinition> edit = code switch
        {
            "no_targets" => d => d.Targets = new PromotionTargets(),
            "missing_param" => d => d.Params.Percent = null,
            "percent_out_of_range" => d => d.Params.Percent = 120m,
            "price_below_minimum" => d => { d.Type = PromotionRuleTypes.FixedPrice; d.Params = new PromotionParams { Price = 0m }; },
            "invalid_quantity" => d => { d.Type = PromotionRuleTypes.BuyNGetM; d.Params = new PromotionParams { BuyQty = 0, GetQty = 1, GetPrice = 0.01m }; },
            "invalid_period" => d => { d.Scope.ValidFrom = DateTimeOffset.UtcNow.AddDays(5); d.Scope.ValidTo = DateTimeOffset.UtcNow.AddDays(1); },
            "unknown_item" => d => d.Targets.ItemIds.Add(Guid.NewGuid()),
            "unknown_category" => d => d.Targets.CategoryIds.Add(Guid.NewGuid()),
            "unknown_store" => d => d.Scope.StoreIds.Add(Guid.NewGuid()),
            "unknown_tier" => d => { d.Audience = new PromotionAudience { Kind = "loyalty_tier", TierIds = [Guid.NewGuid()] }; },
            "tier_required" => d => d.Audience = new PromotionAudience { Kind = "loyalty_tier" },
            _ => throw new InvalidOperationException(code),
        };
        var id = (await CreateAsync(edit: edit)).Id;
        var v = (await _svc.ValidateAsync(Admin, id)).Value!;
        var issue = Assert.Single(v.Errors, e => e.Code == code);
        Assert.Equal(field, issue.Field);
        Assert.False(string.IsNullOrWhiteSpace(issue.Message));
    }

    [Fact]
    public async Task Validate_store_scope_violation_for_a_store_manager_on_a_foreign_store()
    {
        // The rule is created by an admin for store B; a manager of A is validated against it.
        var id = (await CreateAsync(edit: d => d.Scope.StoreIds = [_storeA, _storeB])).Id;
        // Out of scope for the manager -> 404, never reaches validation.
        Assert.Equal(404, (await _svc.ValidateAsync(ManagerOf(_storeA), id)).Status);

        // Directly exercising the validator with a scoped caller flags the violation.
        var mgr = ManagerOf(_storeA);
        var def = PromotionDefinitionJson.TryParse(Def(d => d.Scope.StoreIds = [_storeB]).GetRawText())!;
        var v = await _svc.ValidateDefinitionAsync(mgr, [_storeA], def, null, default);
        Assert.Contains(v.Errors, e => e.Code == "store_scope_violation");
    }

    [Fact]
    public async Task Validate_a_clean_rule_has_no_errors_and_expected_warnings()
    {
        var id = (await CreateAsync()).Id;
        var v = (await _svc.ValidateAsync(Admin, id)).Value!;
        Assert.Empty(v.Errors);
        Assert.Empty(v.Warnings);
    }

    [Fact]
    public async Task Validate_warnings()
    {
        var allStores = (await CreateAsync(edit: d => d.Scope.StoreIds = [], name: "all")).Id;
        Assert.Contains((await _svc.ValidateAsync(Admin, allStores)).Value!.Warnings, w => w.Code == "all_stores");

        var expired = (await CreateAsync(edit: d => d.Scope.ValidTo = DateTimeOffset.UtcNow.AddDays(-1), name: "old")).Id;
        Assert.Contains((await _svc.ValidateAsync(Admin, expired)).Value!.Warnings, w => w.Code == "already_expired");

        var notLower = (await CreateAsync(edit: d =>
        {
            d.Type = PromotionRuleTypes.FixedPrice;
            d.Params = new PromotionParams { Price = 30m };
        }, name: "fp")).Id;
        Assert.Contains((await _svc.ValidateAsync(Admin, notLower)).Value!.Warnings, w => w.Code == "promo_price_not_lower");

        // Cashier loyalty scanning exists: loyalty audiences no longer warn.
        var loyalty = (await CreateAsync(edit: d => d.Audience = new PromotionAudience { Kind = "loyalty_member" }, name: "l")).Id;
        Assert.DoesNotContain((await _svc.ValidateAsync(Admin, loyalty)).Value!.Warnings, w => w.Code == "loyalty_not_available_yet");
    }

    [Fact]
    public async Task Validate_warns_when_another_published_or_paused_rule_overlaps()
    {
        var first = (await CreateAsync(name: "first")).Id;
        await _svc.PublishAsync(Admin, first);
        var second = (await CreateAsync(name: "second")).Id;
        var w = (await _svc.ValidateAsync(Admin, second)).Value!.Warnings;
        Assert.Contains(w, x => x.Code == "overlaps_other_rule" && x.Message.Contains("first"));

        // Paused still counts.
        await _svc.PauseAsync(Admin, first);
        Assert.Contains((await _svc.ValidateAsync(Admin, second)).Value!.Warnings, x => x.Code == "overlaps_other_rule");

        // A different store does not overlap.
        var other = (await CreateAsync(edit: d => d.Scope.StoreIds = [_storeB], name: "b")).Id;
        Assert.DoesNotContain((await _svc.ValidateAsync(Admin, other)).Value!.Warnings, x => x.Code == "overlaps_other_rule");

        // A rule never overlaps itself.
        Assert.DoesNotContain((await _svc.ValidateAsync(Admin, first)).Value!.Warnings, x => x.Code == "overlaps_other_rule");
    }

    // ── store scoping ──────────────────────────────────────────────────────

    [Fact]
    public async Task Store_manager_sees_and_edits_only_rules_inside_their_stores()
    {
        var mine = await CreateAsync(edit: d => d.Scope.StoreIds = [_storeA], name: "mine");
        var foreign = await CreateAsync(edit: d => d.Scope.StoreIds = [_storeB], name: "foreign");
        var everywhere = await CreateAsync(edit: d => d.Scope.StoreIds = [], name: "all");
        var mixed = await CreateAsync(edit: d => d.Scope.StoreIds = [_storeA, _storeB], name: "mixed");
        var mgr = ManagerOf(_storeA);

        var list = await _svc.ListAsync(mgr, null, null);
        Assert.Equal(["mine"], list.Select(l => l.Name).ToArray());
        Assert.Equal(4, (await _svc.ListAsync(Admin, null, null)).Count);

        Assert.True((await _svc.GetAsync(mgr, mine.Id)).Ok);
        foreach (var hidden in new[] { foreign.Id, everywhere.Id, mixed.Id })
        {
            Assert.Equal(404, (await _svc.GetAsync(mgr, hidden)).Status);
            Assert.Equal(404, (await _svc.UpdateAsync(mgr, hidden, new UpdatePromotionRuleRequest("x", null, Def(), null))).Status);
            Assert.Equal(404, (await _svc.PublishAsync(mgr, hidden)).Status);
            Assert.Equal(404, (await _svc.PauseAsync(mgr, hidden)).Status);
            Assert.Equal(404, (await _svc.ArchiveAsync(mgr, hidden)).Status);
        }
    }

    [Fact]
    public async Task Store_manager_cannot_save_empty_or_foreign_store_scope()
    {
        var mgr = ManagerOf(_storeA);
        foreach (var stores in new[] { Array.Empty<Guid>(), [_storeB], [_storeA, _storeB] })
        {
            var r = await _svc.CreateAsync(mgr, new CreatePromotionRuleRequest("x", null, Def(d => d.Scope.StoreIds = stores.ToList())));
            Assert.Equal(400, r.Status);
            Assert.Contains(r.Validation!.Errors, e => e.Code == "store_scope_violation");
        }
        var ok = await _svc.CreateAsync(mgr, new CreatePromotionRuleRequest("x", null, Def(d => d.Scope.StoreIds = [_storeA])));
        Assert.Equal(201, ok.Status);

        var upd = await _svc.UpdateAsync(mgr, ok.Value!.Id, new UpdatePromotionRuleRequest("x", null, Def(d => d.Scope.StoreIds = []), null));
        Assert.Equal(400, upd.Status);
        Assert.Contains(upd.Validation!.Errors, e => e.Code == "store_scope_violation");
    }

    [Fact]
    public async Task Other_roles_see_nothing_and_cannot_save()
    {
        var cashier = new PromotionRuleCaller(_tenant, Guid.NewGuid(), AppRoles.Cashier, _storeA);
        await CreateAsync();
        Assert.Empty(await _svc.ListAsync(cashier, null, null));
        Assert.Equal(400, (await _svc.CreateAsync(cashier, new CreatePromotionRuleRequest("x", null, Def()))).Status);
    }

    [Fact]
    public async Task List_filters_by_status_and_store()
    {
        var a = await CreateAsync(edit: d => d.Scope.StoreIds = [_storeA], name: "a");
        await CreateAsync(edit: d => d.Scope.StoreIds = [_storeB], name: "b");
        await _svc.PublishAsync(Admin, a.Id);

        Assert.Equal(["a"], (await _svc.ListAsync(Admin, "published", null)).Select(x => x.Name).ToArray());
        Assert.Equal(["a"], (await _svc.ListAsync(Admin, null, _storeA)).Select(x => x.Name).ToArray());
        var item = Assert.Single(await _svc.ListAsync(Admin, "draft", null));
        Assert.Equal(("b", "percent_off", 1, (int?)null), (item.Name, item.Type, item.CurrentVersionNumber, item.PublishedVersionNumber));
        Assert.Equal([_storeB], item.StoreIds);
    }

    // ── simulate ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Simulate_two_plus_one_splits_the_line()
    {
        var def = Def(d =>
        {
            d.Type = PromotionRuleTypes.BuyNGetM;
            d.Params = new PromotionParams { BuyQty = 2, GetQty = 1, GetPrice = 0.01m };
        });
        var r = await _svc.SimulateAsync(Admin, new SimulateRequest(
            null, def, _storeA, null, null, [new SimulateLineRequest(_item.Id, 3, null)]));
        Assert.True(r.Ok, r.Error);
        var line = Assert.Single(r.Value!.Lines);
        Assert.Equal(("Milk", 30m, 90m), (line.Name, line.RegularUnitPrice, line.RegularLineTotal));
        Assert.Equal(2, line.Segments.Count);
        Assert.Equal((2m, 30m), (line.Segments[0].Quantity, line.Segments[0].UnitPrice));
        Assert.Equal((1m, 0.01m), (line.Segments[1].Quantity, line.Segments[1].UnitPrice));
        Assert.NotNull(line.Segments[1].Label);
        Assert.Equal(60.01m, line.LineTotal);
        Assert.Equal((60.01m, 90m, 29.99m), (r.Value.Total, r.Value.RegularTotal, r.Value.Savings));
    }

    [Fact]
    public async Task Simulate_uses_the_saved_rule_by_id_and_an_explicit_unit_price_and_category_targets()
    {
        var id = (await CreateAsync(edit: d =>
        {
            d.Targets = new PromotionTargets { CategoryIds = [_categoryId] }; // via the item's category
            d.Params = new PromotionParams { Percent = 50m };
        })).Id;
        var r = await _svc.SimulateAsync(Admin, new SimulateRequest(
            id, null, _storeA, DateTimeOffset.UtcNow, null, [new SimulateLineRequest(_item.Id, 2, 10m)]));
        Assert.True(r.Ok, r.Error);
        Assert.Equal((10m, 20m, 10m, 10m), (r.Value!.Lines[0].RegularUnitPrice, r.Value.RegularTotal, r.Value.Total, r.Value.Savings));
    }

    [Fact]
    public async Task Simulate_audience_membership_and_other_store_do_not_match()
    {
        var def = Def(d => d.Audience = new PromotionAudience { Kind = "loyalty_tier", TierIds = [_tierId] });
        SimulateRequest Req(SimulateMembershipDto? m, Guid store) =>
            new(null, def, store, null, m, [new SimulateLineRequest(_item.Id, 1, null)]);

        Assert.Equal(0m, (await _svc.SimulateAsync(Admin, Req(null, _storeA))).Value!.Savings);
        Assert.Equal(3m, (await _svc.SimulateAsync(Admin, Req(new SimulateMembershipDto(_tierId), _storeA))).Value!.Savings);
        Assert.Equal(0m, (await _svc.SimulateAsync(Admin, Req(new SimulateMembershipDto(_tierId), _storeB))).Value!.Savings);
    }

    [Fact]
    public async Task Simulate_rejects_bad_input_and_hides_foreign_stores_from_managers()
    {
        var line = new SimulateLineRequest(_item.Id, 1, null);
        Assert.Equal(400, (await _svc.SimulateAsync(Admin, new SimulateRequest(null, Def(), _storeA, null, null, []))).Status);
        Assert.Equal(400, (await _svc.SimulateAsync(Admin, new SimulateRequest(null, null, _storeA, null, null, [line]))).Status);
        Assert.Equal(400, (await _svc.SimulateAsync(Admin, new SimulateRequest(null, Def(), _storeA, null, null,
            [new SimulateLineRequest(Guid.NewGuid(), 1, null)]))).Status);
        Assert.Equal(400, (await _svc.SimulateAsync(Admin, new SimulateRequest(null, Def(), _storeA, null, null,
            [new SimulateLineRequest(_item.Id, 0, null)]))).Status);
        Assert.Equal(404, (await _svc.SimulateAsync(Admin, new SimulateRequest(Guid.NewGuid(), null, _storeA, null, null, [line]))).Status);

        var mgr = ManagerOf(_storeA);
        Assert.Equal(403, (await _svc.SimulateAsync(mgr, new SimulateRequest(null, Def(), _storeB, null, null, [line]))).Status);
        Assert.True((await _svc.SimulateAsync(mgr, new SimulateRequest(null, Def(), _storeA, null, null, [line]))).Ok);
    }

    // ── lookups ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Lookup_items_returns_the_contract_shape_and_filters()
    {
        var hit = Assert.Single(await _svc.LookupItemsAsync(Admin, "mil", null, null));
        Assert.Equal((_item.Id, "Milk", "4820000000001", 30m, (Guid?)_categoryId), (hit.Id, hit.Name, hit.Barcode, hit.PriceRetail, hit.CategoryId));
        Assert.Single(await _svc.LookupItemsAsync(Admin, "4820000000001", null, 500));
        Assert.Empty(await _svc.LookupItemsAsync(Admin, "zzz", null, null));
        Assert.Empty(await _svc.LookupItemsAsync(Admin, null, Guid.NewGuid(), null));
        Assert.Empty(await _svc.LookupItemsAsync(new PromotionRuleCaller(Guid.NewGuid(), _userId, AppRoles.EnterpriseAdmin, null), "mil", null, null));
    }

    [Fact]
    public async Task Lookup_items_by_ids_returns_exact_items_omits_unknown_and_caps_at_50()
    {
        var other = new Item { TenantId = Guid.NewGuid(), Name = "Foreign" };
        var r = await _svc.LookupItemsAsync(Admin, "ignored", Guid.NewGuid(), 1, default,
            [_item.Id, Guid.NewGuid(), other.Id]);
        Assert.Equal([_item.Id], r.Select(x => x.Id).ToArray());

        var many = Enumerable.Range(0, 80).Select(_ => Guid.NewGuid()).Prepend(_item.Id).ToList();
        Assert.Single(await _svc.LookupItemsAsync(Admin, null, null, null, default, many));
        // 51st+ ids are dropped: the real item placed after 50 unknown ids is not returned.
        var late = Enumerable.Range(0, 50).Select(_ => Guid.NewGuid()).Append(_item.Id).ToList();
        Assert.Empty(await _svc.LookupItemsAsync(Admin, null, null, null, default, late));
    }

    [Fact]
    public async Task Failure_midway_through_a_draft_replacement_rolls_everything_back()
    {
        var created = await CreateAsync();
        var original = _repo.Versions.Single();
        _repo.FailOnAddVersion = _ => true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.UpdateAsync(Admin, created.Id,
            new UpdatePromotionRuleRequest("Changed", null, Def(d => d.Params.Percent = 50m), null)));

        _repo.FailOnAddVersion = null;
        var detail = (await _svc.GetAsync(Admin, created.Id)).Value!;
        Assert.Equal(("Rule", original.Id), (detail.Name, detail.CurrentVersion.Id));
        Assert.Equal(original.Id, Assert.Single(_repo.Versions).Id);
    }

    [Fact]
    public async Task Lookup_items_matches_barcode_fragments_and_ranks_exact_first()
    {
        var substring = new Item { TenantId = _tenant, Name = "AAA", Barcodes = ["9912399"] };
        var exact = new Item { TenantId = _tenant, Name = "ZZZ", Barcodes = ["00123"] }; // equals "123" after zero strip
        var inactive = new Item { TenantId = _tenant, Name = "Old", Barcodes = ["123000"], IsActive = false };
        var foreign = new Item { TenantId = Guid.NewGuid(), Name = "Other", Barcodes = ["123456"] };
        _catalog.AddRange([substring, exact, inactive, foreign]);

        var r = await _svc.LookupItemsAsync(Admin, " 123\r\n", null, null);
        Assert.Equal(["ZZZ", "AAA"], r.Select(x => x.Name).ToArray()); // exact first, then others; no inactive/foreign

        // partial of the standard fixture barcode (suffix + prefix + middle)
        foreach (var frag in new[] { "0000001", "48200", "20000000" })
            Assert.Equal([_item.Id], (await _svc.LookupItemsAsync(Admin, frag, null, null)).Select(x => x.Id).ToArray());
        // scanner whitespace inside the digits
        Assert.Single(await _svc.LookupItemsAsync(Admin, "4820 0000 00001", null, null));
        // < 3 digits: no fragment search
        _itemsRepo.ClearReceivedCalls();
        await _svc.LookupItemsAsync(Admin, "12", null, null);
        await _itemsRepo.DidNotReceive().SearchByBarcodeFragmentAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        // non-digit query is name-only
        await _svc.LookupItemsAsync(Admin, "mil", null, null);
        await _itemsRepo.DidNotReceive().SearchByBarcodeFragmentAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Lookup_tiers_returns_tenant_tiers_ordered()
    {
        var t = Assert.Single(await _svc.LookupTiersAsync(Admin));
        Assert.Equal((_tierId, "Gold", 0m, 0), (t.Id, t.Name, t.DiscountPercent, t.SortOrder));
    }

    // ── in-memory repository (mirrors the DB trigger: no version updates) ──

    private sealed class InMemoryRuleRepo : IPromotionRuleRepository
    {
        public List<PromotionRule> Rules { get; } = [];
        public List<PromotionRuleVersion> Versions { get; } = [];

        public Task<IReadOnlyList<PublishedPromotionRuleRow>> GetPublishedAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PublishedPromotionRuleRow>>(Rules
                .Where(r => r.TenantId == tenantId && r.Status == PromotionRule.StatusPublished && r.PublishedVersionId != null)
                .Select(r => (r, v: Versions.Single(x => x.Id == r.PublishedVersionId)))
                .Select(x => new PublishedPromotionRuleRow(x.r.Id, x.r.Name, x.r.CreatedAt, x.v.Id, x.v.DefinitionJson)).ToList());

        public Task<IReadOnlyList<PromotionRule>> ListAsync(string? status, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PromotionRule>>(Rules.Where(r => status is null || r.Status == status)
                .OrderBy(r => r.CreatedAt).ToList());

        public Task<IReadOnlyList<PromotionRuleVersion>> GetVersionsByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
        {
            var set = ids.ToHashSet();
            return Task.FromResult<IReadOnlyList<PromotionRuleVersion>>(Versions.Where(v => set.Contains(v.Id)).ToList());
        }

        public Task RemoveVersionAsync(Guid versionId, CancellationToken ct = default)
        {
            Assert.DoesNotContain(Rules, r => r.CurrentVersionId == versionId || r.PublishedVersionId == versionId);
            Versions.RemoveAll(v => v.Id == versionId);
            return Task.CompletedTask;
        }

        public Task<PromotionRule?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(Rules.FirstOrDefault(r => r.Id == id));

        public Task<IReadOnlyList<PromotionRuleVersion>> GetVersionsAsync(Guid ruleId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PromotionRuleVersion>>(Versions.Where(v => v.RuleId == ruleId).OrderBy(v => v.VersionNumber).ToList());

        public Task AddRuleAsync(PromotionRule rule, CancellationToken ct = default) { Rules.Add(rule); return Task.CompletedTask; }
        public Task AddVersionAsync(PromotionRuleVersion version, CancellationToken ct = default)
        {
            if (FailOnAddVersion?.Invoke(version) == true) throw new InvalidOperationException("boom");
            Versions.Add(version);
            return Task.CompletedTask;
        }
        public Task AddApplicationsAsync(IEnumerable<PromotionRuleApplication> applications, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<PromotionRuleApplication>> GetApplicationsForTransactionAsync(Guid transactionId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PromotionRuleApplication>>([]);
        public Func<PromotionRuleVersion, bool>? FailOnAddVersion { get; set; }

        /// <summary>Snapshot/restore emulates a DB rollback.</summary>
        public async Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> work, CancellationToken ct = default)
        {
            var rules = Rules.Select(r => (r, r.Name, r.Status, r.CurrentVersionId, r.PublishedVersionId)).ToList();
            var versions = Versions.ToList();
            try { return await work(); }
            catch
            {
                Versions.Clear(); Versions.AddRange(versions);
                foreach (var (r, n, s, c, p) in rules) { r.Name = n; r.Status = s; r.CurrentVersionId = c; r.PublishedVersionId = p; }
                throw;
            }
        }

        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}
