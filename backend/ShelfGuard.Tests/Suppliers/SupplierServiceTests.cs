using NSubstitute;
using ShelfGuard.Application.Features.Suppliers;
using ShelfGuard.Application.Features.Suppliers.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Suppliers;

public sealed class SupplierServiceTests
{
    private readonly ISupplierRepository _repo = Substitute.For<ISupplierRepository>();
    private readonly SupplierService _sut;
    private readonly Guid _tenantId = Guid.NewGuid();

    public SupplierServiceTests() => _sut = new SupplierService(_repo);

    [Fact]
    public async Task CreateAsync_ValidRequest_ReturnsSupplier()
    {
        _repo.ExistsByNameAsync("АТБ", null, Arg.Any<CancellationToken>()).Returns(false);

        var req = new CreateSupplierRequest("АТБ", "12345678", "Іван", "+380501234567",
            "atb@example.com", 2, false, true, "30 днів", null);

        var (supplier, error) = await _sut.CreateAsync(_tenantId, req);

        Assert.Null(error);
        Assert.NotNull(supplier);
        Assert.Equal("АТБ", supplier.Name);
        Assert.True(supplier.ReturnPolicy);
        await _repo.Received(1).AddAsync(
            Arg.Is<Supplier>(s => s.TenantId == _tenantId && s.Name == "АТБ"),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task CreateAsync_EmptyName_ReturnsError(string name)
    {
        var req = new CreateSupplierRequest(name, null, null, null, null, 3, false, false, null, null);
        var (supplier, error) = await _sut.CreateAsync(_tenantId, req);

        Assert.Null(supplier);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task CreateAsync_DuplicateName_ReturnsError()
    {
        _repo.ExistsByNameAsync("АТБ", null, Arg.Any<CancellationToken>()).Returns(true);

        var req = new CreateSupplierRequest("АТБ", null, null, null, null, 3, false, false, null, null);
        var (supplier, error) = await _sut.CreateAsync(_tenantId, req);

        Assert.Null(supplier);
        Assert.Contains("already exists", error);
    }

    [Fact]
    public async Task CreateAsync_NegativeDeliveryDays_ReturnsError()
    {
        var req = new CreateSupplierRequest("X", null, null, null, null, -1, false, false, null, null);
        var (supplier, error) = await _sut.CreateAsync(_tenantId, req);

        Assert.Null(supplier);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task UpdateAsync_NotFound_ReturnsError()
    {
        var id = Guid.NewGuid();
        _repo.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns((Supplier?)null);

        var req = new UpdateSupplierRequest("X", null, null, null, null, 3, false, false, null, null, true);
        var (supplier, error) = await _sut.UpdateAsync(id, req);

        Assert.Null(supplier);
        Assert.Equal("Supplier not found.", error);
    }

    [Fact]
    public async Task UpdateAsync_Valid_UpdatesAndSaves()
    {
        var existing = new Supplier { TenantId = _tenantId, Name = "Old", DeliveryDays = 5 };
        _repo.GetByIdAsync(existing.Id, Arg.Any<CancellationToken>()).Returns(existing);
        _repo.ExistsByNameAsync("New Name", existing.Id, Arg.Any<CancellationToken>()).Returns(false);

        var req = new UpdateSupplierRequest("New Name", null, "Петро", null, null, 2, true, false, null, null, true);
        var (supplier, error) = await _sut.UpdateAsync(existing.Id, req);

        Assert.Null(error);
        Assert.Equal("New Name", supplier!.Name);
        Assert.Equal(2, supplier.DeliveryDays);
        _repo.Received(1).Update(existing);
    }

    [Fact]
    public async Task DeleteAsync_NotFound_ReturnsFalse()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Supplier?)null);
        var result = await _sut.DeleteAsync(Guid.NewGuid());
        Assert.False(result);
    }

    [Fact]
    public async Task DeleteAsync_Valid_SetsInactive()
    {
        var supplier = new Supplier { TenantId = _tenantId, Name = "Test" };
        _repo.GetByIdAsync(supplier.Id, Arg.Any<CancellationToken>()).Returns(supplier);

        var result = await _sut.DeleteAsync(supplier.Id);

        Assert.True(result);
        Assert.False(supplier.IsActive);
        _repo.Received(1).Update(supplier);
    }
}
