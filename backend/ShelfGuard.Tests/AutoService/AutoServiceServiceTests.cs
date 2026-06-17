using ShelfGuard.Application.Features.AutoService;
using ShelfGuard.Application.Features.AutoService.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.AutoService;

// ── In-memory fake repository ────────────────────────────────────────────────

file sealed class FakeAutoServiceRepo : IAutoServiceRepository
{
    public List<AsCustomer>      Customers      { get; } = [];
    public List<AsVehicle>       Vehicles       { get; } = [];
    public List<AsServiceCatalog> ServiceCatalog { get; } = [];
    public List<AsWorkOrder>     WorkOrders     { get; } = [];
    public List<AsWorkOrderLine> Lines          { get; } = [];
    public List<Item>            Items          { get; } = [];
    public List<ProductStock>    Stock          { get; } = [];
    public List<StockEvent>      StockEvents    { get; } = [];

    // ── Customers ──────────────────────────────────────────────────────────

    public Task<List<AsCustomer>> GetCustomersAsync(string? search, CancellationToken ct = default) =>
        Task.FromResult(Customers.ToList());

    public Task<AsCustomer?> GetCustomerByIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Customers.FirstOrDefault(c => c.Id == id));

    public Task<int> GetVehicleCountForCustomerAsync(Guid customerId, CancellationToken ct = default) =>
        Task.FromResult(Vehicles.Count(v => v.CustomerId == customerId));

    public Task AddCustomerAsync(AsCustomer customer, CancellationToken ct = default)
    {
        Customers.Add(customer);
        return Task.CompletedTask;
    }

    public void UpdateCustomer(AsCustomer customer) { }
    public void RemoveCustomer(AsCustomer customer) => Customers.Remove(customer);

    // ── Vehicles ───────────────────────────────────────────────────────────

    public Task<List<AsVehicle>> GetVehiclesAsync(Guid? customerId, CancellationToken ct = default) =>
        Task.FromResult(Vehicles
            .Where(v => !customerId.HasValue || v.CustomerId == customerId)
            .ToList());

    public Task<AsVehicle?> GetVehicleByIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Vehicles.FirstOrDefault(v => v.Id == id));

    public Task<bool> HasOpenWorkOrdersAsync(Guid vehicleId, CancellationToken ct = default) =>
        Task.FromResult(WorkOrders.Any(w =>
            w.VehicleId == vehicleId &&
            w.Status != WorkOrderStatus.Done &&
            w.Status != WorkOrderStatus.Invoiced));

    public Task AddVehicleAsync(AsVehicle vehicle, CancellationToken ct = default)
    {
        Vehicles.Add(vehicle);
        return Task.CompletedTask;
    }

    public void UpdateVehicle(AsVehicle vehicle) { }
    public void RemoveVehicle(AsVehicle vehicle) => Vehicles.Remove(vehicle);

    // ── Service Catalog ────────────────────────────────────────────────────

    public Task<List<AsServiceCatalog>> GetServiceCatalogAsync(bool includeInactive, CancellationToken ct = default) =>
        Task.FromResult(ServiceCatalog
            .Where(s => includeInactive || s.IsActive)
            .ToList());

    public Task<AsServiceCatalog?> GetServiceCatalogItemByIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(ServiceCatalog.FirstOrDefault(s => s.Id == id));

    public Task AddServiceCatalogItemAsync(AsServiceCatalog item, CancellationToken ct = default)
    {
        ServiceCatalog.Add(item);
        return Task.CompletedTask;
    }

    public void UpdateServiceCatalogItem(AsServiceCatalog item) { }

    // ── Work Orders ────────────────────────────────────────────────────────

    public Task<List<AsWorkOrder>> GetWorkOrdersAsync(
        string? status, Guid? vehicleId, Guid? mechanicUserId, CancellationToken ct = default)
    {
        var query = WorkOrders.AsEnumerable();
        if (vehicleId.HasValue)  query = query.Where(w => w.VehicleId == vehicleId.Value);
        if (mechanicUserId.HasValue) query = query.Where(w => w.MechanicUserId == mechanicUserId.Value);
        return Task.FromResult(query.ToList());
    }

    public Task<AsWorkOrder?> GetWorkOrderByIdAsync(Guid id, CancellationToken ct = default)
    {
        var wo = WorkOrders.FirstOrDefault(w => w.Id == id);
        if (wo is not null)
        {
            // Attach lines from our separate list
            foreach (var line in Lines.Where(l => l.WorkOrderId == wo.Id))
            {
                if (!wo.Lines.Contains(line))
                    wo.Lines.Add(line);
                // Attach item navigation
                if (line.ItemId.HasValue)
                    line.GetType().GetProperty("Item")!
                        .SetValue(line, Items.FirstOrDefault(i => i.Id == line.ItemId.Value));
                if (line.ServiceCatalogId.HasValue)
                    line.GetType().GetProperty("ServiceCatalog")!
                        .SetValue(line, ServiceCatalog.FirstOrDefault(s => s.Id == line.ServiceCatalogId.Value));
            }
        }
        return Task.FromResult(wo);
    }

    public Task AddWorkOrderAsync(AsWorkOrder workOrder, CancellationToken ct = default)
    {
        WorkOrders.Add(workOrder);
        return Task.CompletedTask;
    }

    public void UpdateWorkOrder(AsWorkOrder workOrder) { }

    // ── Lines ──────────────────────────────────────────────────────────────

    public Task AddWorkOrderLineAsync(AsWorkOrderLine line, CancellationToken ct = default)
    {
        Lines.Add(line);
        var wo = WorkOrders.FirstOrDefault(w => w.Id == line.WorkOrderId);
        wo?.Lines.Add(line);
        return Task.CompletedTask;
    }

    public void RemoveWorkOrderLine(AsWorkOrderLine line)
    {
        Lines.Remove(line);
        foreach (var wo in WorkOrders)
            ((List<AsWorkOrderLine>)wo.Lines).Remove(line);
    }

    // ── Stock / Items ──────────────────────────────────────────────────────

    public Task<Item?> GetItemByIdAsync(Guid itemId, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(i => i.Id == itemId));

    public Task<List<ProductStock>> GetFefoOrderedAsync(Guid itemId, CancellationToken ct = default) =>
        Task.FromResult(Stock
            .Where(s => s.ProductId == itemId && s.Quantity > 0)
            .OrderBy(s => s.ExpiryDate)
            .ToList());

    public void UpdateStock(ProductStock batch) { }

    public Task AddStockEventAsync(StockEvent ev, CancellationToken ct = default)
    {
        StockEvents.Add(ev);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

// ── Test data builders ───────────────────────────────────────────────────────

file static class Build
{
    private static readonly Guid TenantId = Guid.NewGuid();

    public static AsCustomer Customer(string name = "John Doe") =>
        new() { TenantId = TenantId, Name = name };

    public static AsVehicle Vehicle(Guid customerId, string brand = "Toyota", string model = "Camry") =>
        new() { TenantId = TenantId, CustomerId = customerId, Brand = brand, Model = model };

    public static AsWorkOrder WorkOrder(Guid vehicleId, WorkOrderStatus status = WorkOrderStatus.New) =>
        new() { TenantId = TenantId, VehicleId = vehicleId, Status = status };

    public static AsWorkOrderLine PartLine(Guid workOrderId, Guid itemId, decimal qty = 2m) =>
        new()
        {
            TenantId    = TenantId,
            WorkOrderId = workOrderId,
            Type        = "part",
            ItemId      = itemId,
            Qty         = qty,
            Price       = 100m,
            Discount    = 0m,
        };

    public static AsWorkOrderLine ServiceLine(Guid workOrderId, Guid svcId) =>
        new()
        {
            TenantId         = TenantId,
            WorkOrderId      = workOrderId,
            Type             = "service",
            ServiceCatalogId = svcId,
            Qty              = 1m,
            Price            = 50m,
            Discount         = 0m,
        };

    public static Item SparePart(string name = "Oil Filter") =>
        new() { TenantId = TenantId, Name = name, ItemType = "spare_part" };

    public static AsServiceCatalog ServiceCatalogEntry(string name = "Oil Change") =>
        new() { TenantId = TenantId, Name = name, IsActive = true };

    public static ProductStock StockBatch(Guid itemId, decimal qty, int daysUntilExpiry = 365) =>
        new()
        {
            TenantId         = TenantId,
            ProductId        = itemId,
            StoreId          = Guid.NewGuid(),
            Quantity         = qty,
            QuantityInitial  = qty,
            ExpiryDate       = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(daysUntilExpiry)),
            Status           = "safe",
        };
}

// ── Tests ────────────────────────────────────────────────────────────────────

public sealed class AutoServiceServiceTests
{
    private static readonly Guid TenantId  = Guid.NewGuid();
    private static readonly Guid UserId    = Guid.NewGuid();

    private static AutoServiceService BuildSvc(IAutoServiceRepository repo) =>
        new(repo);

    // ── DeleteCustomerAsync — 409 when has vehicles ───────────────────────────

    [Fact]
    public async Task DeleteCustomerAsync_WithVehicles_Returns409()
    {
        var repo     = new FakeAutoServiceRepo();
        var svc      = BuildSvc(repo);
        var customer = Build.Customer();
        var vehicle  = Build.Vehicle(customer.Id);

        repo.Customers.Add(customer);
        repo.Vehicles.Add(vehicle);

        var (ok, error, statusCode) = await svc.DeleteCustomerAsync(customer.Id);

        Assert.False(ok);
        Assert.Equal(409, statusCode);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task DeleteCustomerAsync_WithNoVehicles_Succeeds()
    {
        var repo     = new FakeAutoServiceRepo();
        var svc      = BuildSvc(repo);
        var customer = Build.Customer();
        repo.Customers.Add(customer);

        var (ok, error, statusCode) = await svc.DeleteCustomerAsync(customer.Id);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Empty(repo.Customers);
    }

    // ── AddLineAsync — invalid type ───────────────────────────────────────────

    [Fact]
    public async Task AddLineAsync_InvalidType_ReturnsValidationError()
    {
        var repo    = new FakeAutoServiceRepo();
        var svc     = BuildSvc(repo);
        var vehicle = Build.Vehicle(Guid.NewGuid());
        repo.Vehicles.Add(vehicle);

        var wo = Build.WorkOrder(vehicle.Id);
        repo.WorkOrders.Add(wo);

        var dto = new WorkOrderLineCreateDto("unknown_type", null, null, 1m, 100m, 0m);
        var (line, error, statusCode) = await svc.AddLineAsync(wo.Id, dto, TenantId);

        Assert.Null(line);
        Assert.Equal(400, statusCode);
        Assert.NotNull(error);
    }

    // ── AddLineAsync — on done order → 409 ───────────────────────────────────

    [Fact]
    public async Task AddLineAsync_OnDoneOrder_Returns409()
    {
        var repo    = new FakeAutoServiceRepo();
        var svc     = BuildSvc(repo);
        var vehicle = Build.Vehicle(Guid.NewGuid());
        repo.Vehicles.Add(vehicle);

        var wo = Build.WorkOrder(vehicle.Id, WorkOrderStatus.Done);
        repo.WorkOrders.Add(wo);

        var svcEntry = Build.ServiceCatalogEntry();
        repo.ServiceCatalog.Add(svcEntry);

        var dto = new WorkOrderLineCreateDto("service", svcEntry.Id, null, 1m, 50m, 0m);
        var (line, error, statusCode) = await svc.AddLineAsync(wo.Id, dto, TenantId);

        Assert.Null(line);
        Assert.Equal(409, statusCode);
    }

    // ── CompleteWorkOrderAsync — insufficient stock → 422 ────────────────────

    [Fact]
    public async Task CompleteWorkOrderAsync_InsufficientStock_Returns422()
    {
        var repo    = new FakeAutoServiceRepo();
        var svc     = BuildSvc(repo);
        var vehicle = Build.Vehicle(Guid.NewGuid());
        repo.Vehicles.Add(vehicle);

        var wo   = Build.WorkOrder(vehicle.Id, WorkOrderStatus.InProgress);
        repo.WorkOrders.Add(wo);

        var part = Build.SparePart();
        repo.Items.Add(part);

        // Add a stock batch with only 1 unit but line needs 5
        repo.Stock.Add(Build.StockBatch(part.Id, 1m));

        var line = Build.PartLine(wo.Id, part.Id, qty: 5m);
        repo.Lines.Add(line);
        wo.Lines.Add(line);

        var (order, error, statusCode) = await svc.CompleteWorkOrderAsync(wo.Id, UserId, TenantId);

        Assert.Null(order);
        Assert.Equal(422, statusCode);
        Assert.Contains(part.Name, error);
        // Stock must NOT have been consumed
        Assert.Equal(1m, repo.Stock.First().Quantity);
        Assert.Empty(repo.StockEvents);
    }

    // ── CompleteWorkOrderAsync — happy path ───────────────────────────────────

    [Fact]
    public async Task CompleteWorkOrderAsync_HappyPath_StatusDoneAndStockEventsCreated()
    {
        var repo    = new FakeAutoServiceRepo();
        var svc     = BuildSvc(repo);
        var vehicle = Build.Vehicle(Guid.NewGuid());
        repo.Vehicles.Add(vehicle);

        var wo = Build.WorkOrder(vehicle.Id, WorkOrderStatus.InProgress);
        repo.WorkOrders.Add(wo);

        var part = Build.SparePart();
        repo.Items.Add(part);

        // Two FEFO batches: nearest expiry first
        var batch1 = Build.StockBatch(part.Id, 3m, daysUntilExpiry: 10);
        var batch2 = Build.StockBatch(part.Id, 10m, daysUntilExpiry: 60);
        repo.Stock.Add(batch1);
        repo.Stock.Add(batch2);

        var line = Build.PartLine(wo.Id, part.Id, qty: 4m);
        repo.Lines.Add(line);
        wo.Lines.Add(line);

        var (order, error, statusCode) = await svc.CompleteWorkOrderAsync(wo.Id, UserId, TenantId);

        Assert.Null(error);
        Assert.Equal(200, statusCode ?? 200);
        Assert.Equal(WorkOrderStatus.Done, wo.Status);
        Assert.NotNull(wo.CompletedAt);

        // Stock events created: batch1 fully consumed (3), batch2 partially (1)
        Assert.Equal(2, repo.StockEvents.Count);
        Assert.All(repo.StockEvents, ev => Assert.Equal("auto_service_consumption", ev.EventType));

        // FEFO: batch1 (nearest expiry) consumed first
        Assert.Equal(-3m, repo.StockEvents.First(e => e.ProductStockId == batch1.Id).QuantityDelta);
        Assert.Equal(-1m, repo.StockEvents.First(e => e.ProductStockId == batch2.Id).QuantityDelta);
    }
}
