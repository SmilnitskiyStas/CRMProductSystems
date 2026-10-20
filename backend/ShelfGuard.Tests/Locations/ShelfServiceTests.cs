using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ShelfGuard.Application.Features.Locations;
using ShelfGuard.Application.Features.Locations.Dtos;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using ShelfGuard.Infrastructure.AI.ShelfPhotoAdvisor;
using Xunit;

namespace ShelfGuard.Tests.Locations;

public sealed class ShelfServiceTests : IDisposable
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0];

    private readonly ILocationRepository _locations = Substitute.For<ILocationRepository>();
    private readonly IShelfRepository _repo = Substitute.For<IShelfRepository>();
    private readonly IShelfPhotoAdvisor _advisor = Substitute.For<IShelfPhotoAdvisor>();
    private readonly string _uploadRoot = Path.Combine(Path.GetTempPath(), "shelf-scan-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _itemsRoot = Path.Combine(Path.GetTempPath(), "shelf-items-tests-" + Guid.NewGuid().ToString("N"), "items");
    private readonly ShelfService _sut;

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Location _location;
    private readonly LocationZone _zone;

    public ShelfServiceTests()
    {
        _location = new Location { TenantId = _tenantId, Name = "Shop" };
        _zone = new LocationZone { LocationId = _location.Id, Name = "Z", Location = _location };
        _locations.GetZoneWithLocationAsync(_zone.Id, Arg.Any<CancellationToken>()).Returns(_zone);
        _sut = new ShelfService(_locations, _repo, _advisor, NullLogger<ShelfService>.Instance) { UploadRoot = _uploadRoot, ItemImagesRoot = _itemsRoot };
    }

    public void Dispose()
    {
        if (Directory.Exists(_uploadRoot)) Directory.Delete(_uploadRoot, recursive: true);
        var itemsParent = Path.GetDirectoryName(_itemsRoot)!;
        if (Directory.Exists(itemsParent)) Directory.Delete(itemsParent, recursive: true);
    }

    // ── planogram ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SetShelfItems_OtherTenantZone_ReturnsNotFound_AndWritesNothing()
    {
        var (items, error) = await _sut.SetShelfItemsAsync(
            Guid.NewGuid(), _location.Id, _zone.Id, new SetShelfItemsRequest(Guid.NewGuid(), []));

        Assert.Null(items);
        Assert.Equal("Zone not found.", error);
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetShelfItems_LocationMismatch_ReturnsNotFound()
    {
        var (_, error) = await _sut.SetShelfItemsAsync(
            _tenantId, Guid.NewGuid(), _zone.Id, new SetShelfItemsRequest(Guid.NewGuid(), []));

        Assert.Equal("Zone not found.", error);
    }

    [Fact]
    public async Task SetShelfItems_InvalidFacings_Rejected()
    {
        var itemId = Guid.NewGuid();
        var (_, error) = await _sut.SetShelfItemsAsync(
            _tenantId, _location.Id, _zone.Id,
            new SetShelfItemsRequest(Guid.NewGuid(), [new ShelfItemInput(itemId, 0)]));

        Assert.NotNull(error);
        await _repo.DidNotReceive().ReplaceShelfItemsAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<List<ShelfItem>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetShelfItems_UnknownItem_Rejected()
    {
        _repo.GetExistingItemIdsAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<Guid>());

        var (_, error) = await _sut.SetShelfItemsAsync(
            _tenantId, _location.Id, _zone.Id,
            new SetShelfItemsRequest(Guid.NewGuid(), [new ShelfItemInput(Guid.NewGuid(), 2)]));

        Assert.Equal("Item not found.", error);
    }

    [Fact]
    public async Task SetShelfItems_Valid_ReplacesRows_AndAddsMissingZoneTag()
    {
        var itemId = Guid.NewGuid();
        var shelfId = Guid.NewGuid();
        _repo.GetExistingItemIdsAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<Guid> { itemId });
        _repo.ZoneTagExistsAsync(itemId, _zone.Id, Arg.Any<CancellationToken>()).Returns(false);
        _repo.GetShelfItemsAsync(_zone.Id, shelfId, Arg.Any<CancellationToken>()).Returns(
            [new ShelfItem { ShelfId = shelfId, ItemId = itemId, ExpectedFacings = 3, Item = new Item { Name = "Milk" } }]);

        var (items, error) = await _sut.SetShelfItemsAsync(
            _tenantId, _location.Id, _zone.Id, new SetShelfItemsRequest(shelfId, [new ShelfItemInput(itemId, 3)]));

        Assert.Null(error);
        var dto = Assert.Single(items!);
        Assert.Equal("Milk", dto.ItemName);
        Assert.Equal(3, dto.ExpectedFacings);
        await _repo.Received(1).ReplaceShelfItemsAsync(
            _zone.Id, shelfId,
            Arg.Is<List<ShelfItem>>(r => r.Count == 1 && r[0].TenantId == _tenantId && r[0].LocationId == _location.Id),
            Arg.Any<CancellationToken>());
        await _repo.Received(1).AddZoneTagAsync(
            Arg.Is<ItemZoneAssignment>(t => t.ItemId == itemId && t.ZoneId == _zone.Id && t.TenantId == _tenantId),
            Arg.Any<CancellationToken>());
    }

    // ── scans ──────────────────────────────────────────────────────────────

    private void SeedPlanogram(Guid itemId)
    {
        _repo.GetShelfItemsAsync(_zone.Id, null, Arg.Any<CancellationToken>()).Returns(
            [new ShelfItem { ItemId = itemId, ExpectedFacings = 2, Item = new Item { Name = "Milk", Barcodes = ["123"] } }]);
        _advisor.IsConfiguredAsync(Arg.Any<CancellationToken>()).Returns(true);
    }

    private Task<(ShelfScanDto? Scan, string? Error, int? StatusCode)> Scan(byte[] bytes, string name = "p.jpg") =>
        _sut.CreateScanAsync(_tenantId, Guid.NewGuid(), _location.Id, _zone.Id, null,
            new MemoryStream(bytes), bytes.Length, name);

    [Fact]
    public async Task CreateScan_BadExtension_400()
    {
        var (_, error, status) = await Scan(Jpeg, "x.exe");
        Assert.NotNull(error);
        Assert.Equal(400, status);
    }

    [Fact]
    public async Task CreateScan_ContentNotMatchingExtension_400()
    {
        var (_, error, status) = await Scan([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13], "x.png");
        Assert.NotNull(error);
        Assert.Equal(400, status);
    }

    [Fact]
    public async Task CreateScan_NoPlanogram_422_AndAiNotCalled()
    {
        _repo.GetShelfItemsAsync(_zone.Id, null, Arg.Any<CancellationToken>()).Returns([]);

        var (_, _, status) = await Scan(Jpeg);

        Assert.Equal(422, status);
        await _advisor.DidNotReceive().AnalyzeAsync(
            Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<ShelfPhotoExpectedItem>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateScan_AiNotConfigured_503()
    {
        SeedPlanogram(Guid.NewGuid());
        _advisor.IsConfiguredAsync(Arg.Any<CancellationToken>()).Returns(false);

        var (_, _, status) = await Scan(Jpeg);

        Assert.Equal(503, status);
    }

    [Fact]
    public async Task CreateScan_AiThrows_502_NothingPersisted()
    {
        SeedPlanogram(Guid.NewGuid());
        _advisor.AnalyzeAsync(default!, default!, default!, default)
            .ReturnsForAnyArgs<Task<ShelfPhotoAnalysis>>(_ => throw new InvalidOperationException("credit balance is too low"));

        var (scan, error, status) = await Scan(Jpeg);

        Assert.Null(scan);
        Assert.Equal(502, status);
        Assert.DoesNotContain("credit", error!);
        await _repo.DidNotReceive().AddScanAsync(Arg.Any<ShelfScan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateScan_MissingAndLowItems_SetsAlertPendingAndCounts()
    {
        var milk = Guid.NewGuid();
        SeedPlanogram(milk);
        _advisor.AnalyzeAsync(default!, default!, default!, default).ReturnsForAnyArgs(new ShelfPhotoAnalysis(
            [new ShelfPhotoItemVerdict(milk, "Milk", "missing", 0, 0.9, "empty slot")], "Milk gone", "test-model", 42));

        ShelfScan? saved = null;
        await _repo.AddScanAsync(Arg.Do<ShelfScan>(s => saved = s), Arg.Any<CancellationToken>());

        var (scan, error, _) = await Scan(Jpeg);

        Assert.Null(error);
        Assert.NotNull(saved);
        Assert.True(saved!.AlertPending);
        Assert.Equal(1, saved.MissingCount);
        Assert.Equal(0, saved.LowCount);
        Assert.Equal(_tenantId, saved.TenantId);
        Assert.StartsWith($"/uploads/shelf-scans/{_tenantId}/", saved.ImagePath);
        Assert.Equal("Milk gone", scan!.Summary);
        Assert.Equal("missing", Assert.Single(scan.Items).Status);
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        Assert.Single(Directory.GetFiles(Path.Combine(_uploadRoot, _tenantId.ToString())));
    }

    [Fact]
    public async Task CreateScan_AllPresent_NoAlert()
    {
        var milk = Guid.NewGuid();
        SeedPlanogram(milk);
        _advisor.AnalyzeAsync(default!, default!, default!, default).ReturnsForAnyArgs(new ShelfPhotoAnalysis(
            [new ShelfPhotoItemVerdict(milk, "Milk", "present", 3, 0.9, null)], "ok", "test-model", 10));

        ShelfScan? saved = null;
        await _repo.AddScanAsync(Arg.Do<ShelfScan>(s => saved = s), Arg.Any<CancellationToken>());

        await Scan(Jpeg);

        Assert.False(saved!.AlertPending);
    }

    [Fact]
    public async Task CreateScan_SendsReferenceImages_OnlyForSafeExistingFiles_ItemsWithoutImageStillWork()
    {
        Directory.CreateDirectory(_itemsRoot);
        var withImg = Guid.NewGuid();
        var traversal = Guid.NewGuid();
        var noImg = Guid.NewGuid();
        File.WriteAllBytes(Path.Combine(_itemsRoot, "a.jpg"), Jpeg);
        File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(_itemsRoot)!, "secret.jpg"), Jpeg);

        _repo.GetShelfItemsAsync(_zone.Id, null, Arg.Any<CancellationToken>()).Returns(
        [
            new ShelfItem { ItemId = noImg, ExpectedFacings = 1, Item = new Item { Name = "NoImg" } },
            new ShelfItem { ItemId = withImg, ExpectedFacings = 2, Item = new Item { Name = "Milk", ImageUrl = "/uploads/items/a.jpg" } },
            new ShelfItem { ItemId = traversal, ExpectedFacings = 1, Item = new Item { Name = "Evil", ImageUrl = "/uploads/items/../secret.jpg" } },
        ]);
        _advisor.IsConfiguredAsync(Arg.Any<CancellationToken>()).Returns(true);
        IReadOnlyList<ShelfPhotoExpectedItem>? sent = null;
        _advisor.AnalyzeAsync(default!, default!, default!, default).ReturnsForAnyArgs(ci =>
        {
            sent = ci.Arg<IReadOnlyList<ShelfPhotoExpectedItem>>();
            return new ShelfPhotoAnalysis([], null, "m", 1);
        });

        var (_, error, _) = await Scan(Jpeg);

        Assert.Null(error);
        Assert.Equal(3, sent!.Count);
        Assert.Equal(withImg, sent[0].ItemId); // items with images first
        Assert.Single(sent[0].References!);
        Assert.Empty(sent.Single(s => s.ItemId == traversal).References!);
        Assert.Empty(sent.Single(s => s.ItemId == noImg).References!);
    }

    [Fact]
    public async Task CreateScan_LoadsUpToThreeAnglesPerItem_PrimaryFirst_BreadthFirstUnderGlobalCap()
    {
        var tenantDir = Path.Combine(_itemsRoot, _tenantId.ToString());
        Directory.CreateDirectory(tenantDir);
        var items = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();
        var images = new List<ItemImage>();
        foreach (var id in items)
            for (var n = 0; n < 4; n++) // 4 stored, only 3 may be used
            {
                var file = $"{id}-{n}.jpg";
                File.WriteAllBytes(Path.Combine(tenantDir, file), Jpeg);
                images.Add(new ItemImage { ItemId = id, ImageUrl = $"/uploads/items/{_tenantId}/{file}", SortOrder = n, IsPrimary = n == 0, Label = n == 0 ? "front" : null });
            }
        // Ordered as the repository would: not necessarily primary first inside the list.
        images.Reverse();

        _repo.GetShelfItemsAsync(_zone.Id, null, Arg.Any<CancellationToken>()).Returns(
            items.Select(id => new ShelfItem { ItemId = id, ExpectedFacings = 1, Item = new Item { Name = id.ToString() } }).ToList());
        _repo.GetItemImagesAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>()).Returns(images);
        _advisor.IsConfiguredAsync(Arg.Any<CancellationToken>()).Returns(true);
        IReadOnlyList<ShelfPhotoExpectedItem>? sent = null;
        _advisor.AnalyzeAsync(default!, default!, default!, default).ReturnsForAnyArgs(ci =>
        {
            sent = ci.Arg<IReadOnlyList<ShelfPhotoExpectedItem>>();
            return new ShelfPhotoAnalysis([], null, "m", 1);
        });

        var (_, error, _) = await Scan(Jpeg);

        Assert.Null(error);
        // 5 items x 3 = 15 > 12 cap: round 1 and 2 cover everybody (10), round 3 only the first two items.
        Assert.Equal(12, sent!.Sum(s => s.References!.Count));
        Assert.All(sent, s => Assert.True(s.References!.Count >= 2));
        Assert.All(sent, s => Assert.Equal("front", s.References![0].Label)); // primary first
        Assert.Equal(2, sent.Count(s => s.References!.Count == 3));
    }

    [Fact]
    public async Task GetShelves_ReturnsSectionsFromPlanJson_WithPlanogramItems()
    {
        var shelf = Guid.NewGuid();
        var other = Guid.NewGuid();
        var zone = new LocationZone
        {
            LocationId = _location.Id, Location = _location,
            Position = $$"""{"version":1,"items":[{"shelfId":"{{shelf}}","label":"Top"},{"shelfId":"{{other}}","label":"Bottom"},{"label":"bad"}]}""",
        };
        _locations.GetZoneWithLocationAsync(zone.Id, Arg.Any<CancellationToken>()).Returns(zone);
        _repo.GetShelfItemsAsync(zone.Id, null, Arg.Any<CancellationToken>()).Returns(
            [new ShelfItem { ShelfId = shelf, ItemId = Guid.NewGuid(), Item = new Item { Name = "Milk" } }]);

        var (sections, error) = await _sut.GetShelvesAsync(_tenantId, _location.Id, zone.Id);

        Assert.Null(error);
        Assert.Equal(2, sections!.Count);
        Assert.Equal("Top", sections[0].Label);
        Assert.Single(sections[0].Items);
        Assert.Empty(sections[1].Items);
    }

    [Fact]
    public void Advisor_BuildImages_LabelsReferencesThenShelfPhotoLast()
    {
        var withRef = new ShelfPhotoExpectedItem(Guid.NewGuid(), "Milk", "123", 2, [new ShelfReferenceImage([1], "image/png")]);
        var without = new ShelfPhotoExpectedItem(Guid.NewGuid(), "Bread", null, 1);

        var images = ShelfPhotoAdvisor.BuildImages(Jpeg, "image/jpeg", [without, withRef]);

        Assert.Equal(2, images.Count);
        Assert.Contains("Milk", images[0].Caption);
        Assert.Contains(withRef.ItemId.ToString(), images[0].Caption);
        Assert.Contains("123", images[0].Caption);
        Assert.Equal("image/png", images[0].MediaType);
        Assert.Contains("SHELF PHOTO", images[1].Caption);
        Assert.Equal("image/jpeg", images[1].MediaType);
    }

    // ── advisor parsing ────────────────────────────────────────────────────

    [Fact]
    public void Advisor_Parse_FillsSkippedItemsAsMissing_AndIgnoresUnknown()
    {
        var a = new ShelfPhotoExpectedItem(Guid.NewGuid(), "A", null, 1);
        var b = new ShelfPhotoExpectedItem(Guid.NewGuid(), "B", null, 2);
        var json = $$"""
        {"items":[
          {"itemId":"{{a.ItemId}}","status":"low","estimatedCount":1,"confidence":1.7,"note":"n"},
          {"itemId":"{{Guid.NewGuid()}}","status":"present","estimatedCount":9,"confidence":1,"note":"ghost"}
        ],"summary":"s"}
        """;

        var result = ShelfPhotoAdvisor.Parse(json, [a, b], "m", 5);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal("low", result.Items[0].Status);
        Assert.Equal(1.0, result.Items[0].Confidence); // clamped
        Assert.Equal("missing", result.Items[1].Status);
        Assert.Equal(0, result.Items[1].Confidence);
        Assert.Equal("s", result.Summary);
    }

    [Fact]
    public async Task Advisor_Analyze_SendsImageAndSchema_ThroughSlotClient()
    {
        var ai = Substitute.For<IAiClientFactory>();
        var prompt = Substitute.For<IAiPromptResolver>();
        var client = Substitute.For<IAiChatClient>();
        ai.ResolveAsync(AiSlot.Analyst, Arg.Any<CancellationToken>()).Returns(client);
        prompt.WrapSystemPromptAsync(Arg.Any<string>(), AiSlot.Analyst, Arg.Any<CancellationToken>())
            .Returns(ci => "GUARD " + ci.Arg<string>());

        var item = new ShelfPhotoExpectedItem(Guid.NewGuid(), "Milk", "123", 2);
        AiChatRequest? sent = null;
        client.CompleteAsync(Arg.Do<AiChatRequest>(r => sent = r), Arg.Any<CancellationToken>())
            .Returns(new AiChatResult($$"""{"items":[{"itemId":"{{item.ItemId}}","status":"present","estimatedCount":2,"confidence":0.8,"note":""}],"summary":""}""", "mdl", 7));

        var advisor = new ShelfPhotoAdvisor(ai, prompt);
        var result = await advisor.AnalyzeAsync(Jpeg, "image/jpeg", [item]);

        Assert.StartsWith("GUARD ", sent!.SystemPrompt);
        Assert.NotNull(sent.JsonSchema);
        var img = Assert.Single(sent.Images!); // no reference photo -> only the shelf photo
        Assert.Equal("image/jpeg", img.MediaType);
        Assert.Contains("Milk", sent.UserPrompt);
        Assert.Equal("present", Assert.Single(result.Items).Status);
        Assert.Equal(7, result.TokensUsed);
    }
}
