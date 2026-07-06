using NSubstitute;
using ShelfGuard.Application.Features.Marketplace;
using ShelfGuard.Application.Features.Marketplace.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Marketplace;

/// <summary>
/// TASK-306: supplier task board — a standalone entity scoped to the calling
/// supplier tenant, resolved via the same owner-managed-supplier lookup used by
/// SupplierCabinetService.
/// </summary>
public sealed class SupplierTaskServiceTests
{
    private readonly ISupplierTaskRepository _repo = Substitute.For<ISupplierTaskRepository>();
    private readonly IMarketplaceRepository _marketplaceRepo = Substitute.For<IMarketplaceRepository>();
    private readonly SupplierTaskService _sut;

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId   = Guid.NewGuid();

    public SupplierTaskServiceTests() => _sut = new SupplierTaskService(_repo, _marketplaceRepo);

    private Supplier ArrangeOwnSupplier()
    {
        var supplier = new Supplier { TenantId = _tenantId, Name = "My Supplier" };
        var profile = new SupplierProfile
        {
            SupplierId     = supplier.Id,
            TenantId       = _tenantId,
            IsOwnerManaged = true,
        };
        _marketplaceRepo.GetOwnerManagedProfileAsync(_tenantId, Arg.Any<CancellationToken>())
                        .Returns((profile, supplier));
        return supplier;
    }

    [Fact]
    public async Task CreateAsync_ValidRequest_CreatesTaskForResolvedSupplier()
    {
        var supplier = ArrangeOwnSupplier();

        var request = new CreateSupplierTaskRequest("Follow up with client", null, null, null, null);
        var (task, error) = await _sut.CreateAsync(_tenantId, _userId, request);

        Assert.Null(error);
        Assert.NotNull(task);
        Assert.Equal("Follow up with client", task!.Title);
        Assert.Equal("pending", task.Status);
        await _repo.Received(1).AddAsync(
            Arg.Is<SupplierTask>(t => t.SupplierId == supplier.Id && t.TenantId == _tenantId
                                   && t.CreatedByUserId == _userId),
            Arg.Any<CancellationToken>());
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_MissingTitle_ReturnsValidationError_DoesNotSave()
    {
        ArrangeOwnSupplier();

        var request = new CreateSupplierTaskRequest("   ", null, null, null, null);
        var (task, error) = await _sut.CreateAsync(_tenantId, _userId, request);

        Assert.Null(task);
        Assert.NotNull(error);
        await _repo.DidNotReceive().AddAsync(Arg.Any<SupplierTask>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_NoOwnerManagedSupplier_ReturnsError()
    {
        _marketplaceRepo.GetOwnerManagedProfileAsync(_tenantId, Arg.Any<CancellationToken>())
                        .Returns(((SupplierProfile, Supplier)?)null);

        var request = new CreateSupplierTaskRequest("Task", null, null, null, null);
        var (task, error) = await _sut.CreateAsync(_tenantId, _userId, request);

        Assert.Null(task);
        Assert.Equal("Supplier cabinet is not available for this tenant.", error);
        await _repo.DidNotReceive().AddAsync(Arg.Any<SupplierTask>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_AssignedUserNotInTenant_ReturnsError()
    {
        ArrangeOwnSupplier();
        var otherUserId = Guid.NewGuid();
        _repo.UserBelongsToTenantAsync(_tenantId, otherUserId, Arg.Any<CancellationToken>())
             .Returns(false);

        var request = new CreateSupplierTaskRequest("Task", null, null, otherUserId, null);
        var (task, error) = await _sut.CreateAsync(_tenantId, _userId, request);

        Assert.Null(task);
        Assert.NotNull(error);
        await _repo.DidNotReceive().AddAsync(Arg.Any<SupplierTask>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAllAsync_AssignedToMe_FiltersByCallerUserId()
    {
        _repo.GetAllAsync(_tenantId, _userId, null, null, Arg.Any<CancellationToken>())
             .Returns(new List<(SupplierTask, string?, string?)>());

        var (tasks, error) = await _sut.GetAllAsync(_tenantId, _userId, assignedToMe: true, null, null);

        Assert.Null(error);
        Assert.NotNull(tasks);
        await _repo.Received(1).GetAllAsync(_tenantId, _userId, null, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAllAsync_InvalidStatus_ReturnsValidationError()
    {
        var (tasks, error) = await _sut.GetAllAsync(_tenantId, _userId, false, null, "not_a_status");

        Assert.Null(tasks);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task UpdateStatusAsync_ValidStatus_UpdatesAndSetsCompletedAt()
    {
        var task = new SupplierTask { SupplierId = Guid.NewGuid(), TenantId = _tenantId, Title = "X" };
        _repo.GetByIdAsync(_tenantId, task.Id, Arg.Any<CancellationToken>()).Returns(task);

        var (dto, error) = await _sut.UpdateStatusAsync(
            _tenantId, task.Id, new UpdateSupplierTaskStatusRequest("completed"));

        Assert.Null(error);
        Assert.Equal("completed", dto!.Status);
        Assert.NotNull(task.CompletedAt);
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateStatusAsync_InvalidStatus_ReturnsValidationError_DoesNotSave()
    {
        var (dto, error) = await _sut.UpdateStatusAsync(
            _tenantId, Guid.NewGuid(), new UpdateSupplierTaskStatusRequest("bogus"));

        Assert.Null(dto);
        Assert.NotNull(error);
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateStatusAsync_TaskNotFound_ReturnsError()
    {
        _repo.GetByIdAsync(_tenantId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
             .Returns((SupplierTask?)null);

        var (dto, error) = await _sut.UpdateStatusAsync(
            _tenantId, Guid.NewGuid(), new UpdateSupplierTaskStatusRequest("pending"));

        Assert.Null(dto);
        Assert.Equal("Task not found.", error);
    }
}
