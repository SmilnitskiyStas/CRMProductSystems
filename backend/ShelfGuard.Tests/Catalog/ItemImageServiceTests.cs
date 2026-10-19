using NSubstitute;
using ShelfGuard.Application.Features.Catalog;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Catalog;

public sealed class ItemImageServiceTests : IDisposable
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0];

    private readonly IItemRepository _items = Substitute.For<IItemRepository>();
    private readonly IItemImageRepository _images = Substitute.For<IItemImageRepository>();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "item-img-tests-" + Guid.NewGuid().ToString("N"));
    private readonly ItemImageService _sut;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Item _item;
    private readonly List<ItemImage> _store = [];

    public ItemImageServiceTests()
    {
        _item = new Item { TenantId = _tenantId, Name = "Milk" };
        _items.GetByIdAsync(_item.Id, Arg.Any<CancellationToken>()).Returns(_item);
        _images.GetByItemAsync(_item.Id, Arg.Any<CancellationToken>())
            .Returns(_ => _store.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.SortOrder).ToList());
        _images.AddAsync(Arg.Do<ItemImage>(i => _store.Add(i)), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _images.When(r => r.Remove(Arg.Any<ItemImage>())).Do(c => _store.Remove(c.Arg<ItemImage>()));
        _sut = new ItemImageService(_items, _images) { ItemImagesRoot = _root };
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private Task<(ItemImageDto? Image, string? Error, int? StatusCode)> Add(
        string name = "a.jpg", string? label = null, bool makePrimary = false, byte[]? bytes = null)
    {
        bytes ??= Jpeg;
        return _sut.AddAsync(_tenantId, _item.Id, new MemoryStream(bytes), bytes.Length, name, label, makePrimary);
    }

    [Fact]
    public async Task First_image_becomes_primary_and_mirrors_item_image_url()
    {
        var (img, error, status) = await Add(label: " front ");

        Assert.Null(error);
        Assert.Equal(201, status);
        Assert.True(img!.IsPrimary);
        Assert.Equal("front", img.Label);
        Assert.Equal(img.Url, _item.ImageUrl);
        Assert.StartsWith($"/uploads/items/{_tenantId}/", img.Url);
        Assert.True(File.Exists(Path.Combine(_root, _tenantId.ToString(), Path.GetFileName(img.Url))));
    }

    [Fact]
    public async Task Second_image_is_not_primary_and_item_url_unchanged()
    {
        var (first, _, _) = await Add();
        var (second, _, _) = await Add("b.png", bytes: [0x89, 0x50, 0x4E, 0x47, 0, 0, 0, 0, 0, 0]);

        Assert.False(second!.IsPrimary);
        Assert.Equal(first!.Url, _item.ImageUrl);
        Assert.NotEqual(first.Url, second.Url);
    }

    [Fact]
    public async Task Ninth_image_is_rejected()
    {
        for (var i = 0; i < ItemImageService.MaxImagesPerItem; i++)
            Assert.Null((await Add()).Error);

        var (_, error, status) = await Add();

        Assert.NotNull(error);
        Assert.Equal(400, status);
        Assert.Equal(ItemImageService.MaxImagesPerItem, _store.Count);
    }

    [Fact]
    public async Task Bad_extension_and_bad_signature_rejected()
    {
        Assert.Equal(400, (await Add("x.exe")).StatusCode);
        Assert.Equal(400, (await Add("x.png", bytes: Jpeg)).StatusCode);
        Assert.Empty(_store);
    }

    [Fact]
    public async Task Other_tenant_gets_404_everywhere()
    {
        var other = Guid.NewGuid();
        Assert.Equal(404, (await _sut.AddAsync(other, _item.Id, new MemoryStream(Jpeg), Jpeg.Length, "a.jpg", null)).StatusCode);
        Assert.NotNull((await _sut.ListAsync(other, _item.Id)).Error);
        Assert.Equal(404, (await _sut.UpdateAsync(other, _item.Id, Guid.NewGuid(), new UpdateItemImageRequest("x", null, null))).StatusCode);
        Assert.False((await _sut.DeleteAsync(other, _item.Id, Guid.NewGuid())).Ok);
        Assert.Empty(_store);
    }

    [Fact]
    public async Task Setting_primary_unsets_previous_and_updates_item_url()
    {
        var (first, _, _) = await Add();
        var (second, _, _) = await Add("b.jpg");

        var (updated, error, _) = await _sut.UpdateAsync(_tenantId, _item.Id, second!.Id, new UpdateItemImageRequest("side", true, null));

        Assert.Null(error);
        Assert.True(updated!.IsPrimary);
        Assert.Equal("side", updated.Label);
        Assert.Equal(1, _store.Count(i => i.IsPrimary));
        Assert.False(_store.Single(i => i.Id == first!.Id).IsPrimary);
        Assert.Equal(second.Url, _item.ImageUrl);
    }

    [Fact]
    public async Task Unsetting_primary_directly_is_rejected()
    {
        var (first, _, _) = await Add();

        var (_, error, status) = await _sut.UpdateAsync(_tenantId, _item.Id, first!.Id, new UpdateItemImageRequest(null, false, null));

        Assert.NotNull(error);
        Assert.Equal(400, status);
    }

    [Fact]
    public async Task Deleting_primary_promotes_next_and_removes_file()
    {
        var (first, _, _) = await Add();
        var (second, _, _) = await Add("b.jpg");
        var firstFile = Path.Combine(_root, _tenantId.ToString(), Path.GetFileName(first!.Url));

        var (ok, _) = await _sut.DeleteAsync(_tenantId, _item.Id, first.Id);

        Assert.True(ok);
        Assert.False(File.Exists(firstFile));
        Assert.True(_store.Single().IsPrimary);
        Assert.Equal(second!.Url, _item.ImageUrl);
    }

    [Fact]
    public async Task Deleting_last_image_clears_item_url()
    {
        var (first, _, _) = await Add();

        await _sut.DeleteAsync(_tenantId, _item.Id, first!.Id);

        Assert.Empty(_store);
        Assert.Null(_item.ImageUrl);
    }

    [Fact]
    public async Task Legacy_upload_becomes_primary_and_legacy_delete_removes_primary()
    {
        var (first, _, _) = await Add();
        var (legacy, _, _) = await Add("b.jpg", makePrimary: true);

        Assert.True(legacy!.IsPrimary);
        Assert.Equal(1, _store.Count(i => i.IsPrimary));
        Assert.Equal(legacy.Url, _item.ImageUrl);

        var (ok, _) = await _sut.DeletePrimaryAsync(_tenantId, _item.Id);

        Assert.True(ok);
        Assert.Equal(first!.Url, _item.ImageUrl);
        Assert.True(_store.Single().IsPrimary);
    }
}
