using ShelfGuard.Application.Features.AutoService.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.AutoService;

/// <summary>
/// Business logic for the Auto Service module.
/// FEFO write-down mirrors the pattern used in PosService.ProcessSaleAsync.
/// </summary>
public sealed class AutoServiceService : IAutoServiceService
{
    private readonly IAutoServiceRepository _repo;

    public AutoServiceService(IAutoServiceRepository repo) => _repo = repo;

    // ── Customers ─────────────────────────────────────────────────────────────

    public async Task<List<CustomerListItemDto>> GetCustomersAsync(
        string? search, CancellationToken ct = default)
    {
        var customers = await _repo.GetCustomersAsync(search, ct);
        return customers
            .Select(c => new CustomerListItemDto(
                c.Id, c.Name, c.Phone, c.Email,
                c.Vehicles.Count))
            .ToList();
    }

    public async Task<CustomerDetailDto?> GetCustomerByIdAsync(
        Guid id, CancellationToken ct = default)
    {
        var c = await _repo.GetCustomerByIdAsync(id, ct);
        return c is null ? null : ToCustomerDetail(c);
    }

    public async Task<CustomerDetailDto> CreateCustomerAsync(
        CustomerCreateDto dto, CancellationToken ct = default)
    {
        var customer = new AsCustomer
        {
            Name  = dto.Name,
            Phone = dto.Phone,
            Email = dto.Email,
            Notes = dto.Notes,
        };

        await _repo.AddCustomerAsync(customer, ct);
        await _repo.SaveChangesAsync(ct);

        // Reload to populate navigations
        var loaded = await _repo.GetCustomerByIdAsync(customer.Id, ct);
        return ToCustomerDetail(loaded!);
    }

    public async Task<(CustomerDetailDto? Customer, string? Error, int? StatusCode)> UpdateCustomerAsync(
        Guid id, CustomerUpdateDto dto, CancellationToken ct = default)
    {
        var customer = await _repo.GetCustomerByIdAsync(id, ct);
        if (customer is null) return (null, "Customer not found.", 404);

        if (dto.Name is not null) customer.Name  = dto.Name;
        if (dto.Phone is not null) customer.Phone = dto.Phone;
        if (dto.Email is not null) customer.Email = dto.Email;
        if (dto.Notes is not null) customer.Notes = dto.Notes;
        customer.UpdatedAt = DateTimeOffset.UtcNow;

        _repo.UpdateCustomer(customer);
        await _repo.SaveChangesAsync(ct);

        return (ToCustomerDetail(customer), null, null);
    }

    public async Task<(bool Ok, string? Error, int? StatusCode)> DeleteCustomerAsync(
        Guid id, CancellationToken ct = default)
    {
        var customer = await _repo.GetCustomerByIdAsync(id, ct);
        if (customer is null) return (false, "Customer not found.", 404);

        var vehicleCount = await _repo.GetVehicleCountForCustomerAsync(id, ct);
        if (vehicleCount > 0)
            return (false, "Cannot delete customer with existing vehicles.", 409);

        _repo.RemoveCustomer(customer);
        await _repo.SaveChangesAsync(ct);
        return (true, null, null);
    }

    // ── Vehicles ──────────────────────────────────────────────────────────────

    public async Task<List<VehicleListItemDto>> GetVehiclesAsync(
        Guid? customerId, CancellationToken ct = default)
    {
        var vehicles = await _repo.GetVehiclesAsync(customerId, ct);
        return vehicles.Select(ToVehicleListItem).ToList();
    }

    public async Task<VehicleDetailDto?> GetVehicleByIdAsync(
        Guid id, CancellationToken ct = default)
    {
        var v = await _repo.GetVehicleByIdAsync(id, ct);
        return v is null ? null : ToVehicleDetail(v);
    }

    public async Task<(VehicleDetailDto? Vehicle, string? Error, int? StatusCode)> CreateVehicleAsync(
        VehicleCreateDto dto, CancellationToken ct = default)
    {
        // Validate customer exists
        var customer = await _repo.GetCustomerByIdAsync(dto.CustomerId, ct);
        if (customer is null)
            return (null, "Customer not found.", 404);

        var vehicle = new AsVehicle
        {
            CustomerId   = dto.CustomerId,
            Brand        = dto.Brand,
            Model        = dto.Model,
            Year         = dto.Year.HasValue ? (short)dto.Year.Value : null,
            Vin          = dto.Vin,
            LicensePlate = dto.LicensePlate,
            Mileage      = dto.Mileage,
            Notes        = dto.Notes,
        };

        await _repo.AddVehicleAsync(vehicle, ct);
        await _repo.SaveChangesAsync(ct);

        var loaded = await _repo.GetVehicleByIdAsync(vehicle.Id, ct);
        return (ToVehicleDetail(loaded!), null, null);
    }

    public async Task<(VehicleDetailDto? Vehicle, string? Error, int? StatusCode)> UpdateVehicleAsync(
        Guid id, VehicleUpdateDto dto, CancellationToken ct = default)
    {
        var v = await _repo.GetVehicleByIdAsync(id, ct);
        if (v is null) return (null, "Vehicle not found.", 404);

        if (dto.Brand  is not null) v.Brand  = dto.Brand;
        if (dto.Model  is not null) v.Model  = dto.Model;
        if (dto.Year.HasValue)      v.Year   = (short)dto.Year.Value;
        if (dto.Vin    is not null) v.Vin    = dto.Vin;
        if (dto.LicensePlate is not null) v.LicensePlate = dto.LicensePlate;
        if (dto.Mileage.HasValue)   v.Mileage = dto.Mileage.Value;
        if (dto.Notes  is not null) v.Notes  = dto.Notes;
        v.UpdatedAt = DateTimeOffset.UtcNow;

        _repo.UpdateVehicle(v);
        await _repo.SaveChangesAsync(ct);
        return (ToVehicleDetail(v), null, null);
    }

    public async Task<(bool Ok, string? Error, int? StatusCode)> DeleteVehicleAsync(
        Guid id, CancellationToken ct = default)
    {
        var v = await _repo.GetVehicleByIdAsync(id, ct);
        if (v is null) return (false, "Vehicle not found.", 404);

        var hasOpen = await _repo.HasOpenWorkOrdersAsync(id, ct);
        if (hasOpen)
            return (false, "Cannot delete vehicle with open work orders.", 409);

        _repo.RemoveVehicle(v);
        await _repo.SaveChangesAsync(ct);
        return (true, null, null);
    }

    // ── Service Catalog ───────────────────────────────────────────────────────

    public async Task<List<ServiceCatalogItemDto>> GetServiceCatalogAsync(
        bool includeInactive, CancellationToken ct = default)
    {
        var items = await _repo.GetServiceCatalogAsync(includeInactive, ct);
        return items.Select(ToServiceCatalogDto).ToList();
    }

    public async Task<ServiceCatalogItemDto> CreateServiceCatalogItemAsync(
        ServiceCatalogCreateDto dto, CancellationToken ct = default)
    {
        var item = new AsServiceCatalog
        {
            Name          = dto.Name,
            Description   = dto.Description,
            ItemId        = dto.ItemId,
            DefaultPrice  = dto.DefaultPrice,
            DurationHours = dto.DurationHours,
            IsActive      = true,
        };

        await _repo.AddServiceCatalogItemAsync(item, ct);
        await _repo.SaveChangesAsync(ct);
        return ToServiceCatalogDto(item);
    }

    public async Task<(ServiceCatalogItemDto? Item, string? Error, int? StatusCode)> UpdateServiceCatalogItemAsync(
        Guid id, ServiceCatalogUpdateDto dto, CancellationToken ct = default)
    {
        var item = await _repo.GetServiceCatalogItemByIdAsync(id, ct);
        if (item is null) return (null, "Service catalog item not found.", 404);

        if (dto.Name          is not null) item.Name          = dto.Name;
        if (dto.Description   is not null) item.Description   = dto.Description;
        if (dto.ItemId        is not null) item.ItemId        = dto.ItemId;
        if (dto.DefaultPrice  is not null) item.DefaultPrice  = dto.DefaultPrice;
        if (dto.DurationHours is not null) item.DurationHours = dto.DurationHours;
        if (dto.IsActive      is not null) item.IsActive      = dto.IsActive.Value;

        _repo.UpdateServiceCatalogItem(item);
        await _repo.SaveChangesAsync(ct);
        return (ToServiceCatalogDto(item), null, null);
    }

    public async Task<(bool Ok, string? Error, int? StatusCode)> DeleteServiceCatalogItemAsync(
        Guid id, CancellationToken ct = default)
    {
        var item = await _repo.GetServiceCatalogItemByIdAsync(id, ct);
        if (item is null) return (false, "Service catalog item not found.", 404);

        item.IsActive = false;
        _repo.UpdateServiceCatalogItem(item);
        await _repo.SaveChangesAsync(ct);
        return (true, null, null);
    }

    // ── Work Orders ───────────────────────────────────────────────────────────

    public async Task<List<WorkOrderListItemDto>> GetWorkOrdersAsync(
        string? status, Guid? vehicleId, Guid? mechanicUserId, CancellationToken ct = default)
    {
        var orders = await _repo.GetWorkOrdersAsync(status, vehicleId, mechanicUserId, ct);
        return orders.Select(ToWorkOrderListItem).ToList();
    }

    public async Task<WorkOrderDetailDto?> GetWorkOrderByIdAsync(
        Guid id, CancellationToken ct = default)
    {
        var order = await _repo.GetWorkOrderByIdAsync(id, ct);
        return order is null ? null : ToWorkOrderDetail(order);
    }

    public async Task<(WorkOrderDetailDto? Order, string? Error, int? StatusCode)> CreateWorkOrderAsync(
        WorkOrderCreateDto dto, Guid tenantId, CancellationToken ct = default)
    {
        var vehicle = await _repo.GetVehicleByIdAsync(dto.VehicleId, ct);
        if (vehicle is null)
            return (null, "Vehicle not found.", 404);

        var order = new AsWorkOrder
        {
            TenantId       = tenantId,
            VehicleId      = dto.VehicleId,
            MechanicUserId = dto.MechanicUserId,
            Status         = WorkOrderStatus.New,
            Notes          = dto.Notes,
        };

        await _repo.AddWorkOrderAsync(order, ct);
        await _repo.SaveChangesAsync(ct);

        var loaded = await _repo.GetWorkOrderByIdAsync(order.Id, ct);
        return (ToWorkOrderDetail(loaded!), null, null);
    }

    public async Task<(WorkOrderDetailDto? Order, string? Error, int? StatusCode)> UpdateWorkOrderAsync(
        Guid id, WorkOrderUpdateDto dto, CancellationToken ct = default)
    {
        var order = await _repo.GetWorkOrderByIdAsync(id, ct);
        if (order is null) return (null, "Work order not found.", 404);

        if (dto.Status is not null)
        {
            if (!Enum.TryParse<WorkOrderStatus>(dto.Status, true, out var newStatus))
                return (null, $"Invalid status value '{dto.Status}'.", 400);
            order.Status = newStatus;
        }

        if (dto.MechanicUserId.HasValue)
            order.MechanicUserId = dto.MechanicUserId.Value;

        if (dto.Notes is not null)
            order.Notes = dto.Notes;

        order.UpdatedAt = DateTimeOffset.UtcNow;

        _repo.UpdateWorkOrder(order);
        await _repo.SaveChangesAsync(ct);
        return (ToWorkOrderDetail(order), null, null);
    }

    public async Task<(WorkOrderLineDto? Line, string? Error, int? StatusCode)> AddLineAsync(
        Guid workOrderId, WorkOrderLineCreateDto dto, Guid tenantId, CancellationToken ct = default)
    {
        var order = await _repo.GetWorkOrderByIdAsync(workOrderId, ct);
        if (order is null)
            return (null, "Work order not found.", 404);

        if (order.Status == WorkOrderStatus.Done || order.Status == WorkOrderStatus.Invoiced)
            return (null, "Cannot add lines to a completed or invoiced work order.", 409);

        var typeNorm = dto.Type?.ToLower();
        if (typeNorm != "service" && typeNorm != "part")
            return (null, "Line type must be 'service' or 'part'.", 400);

        string? serviceName = null;
        string? itemName    = null;

        if (typeNorm == "service")
        {
            if (!dto.ServiceCatalogId.HasValue)
                return (null, "ServiceCatalogId is required for service lines.", 400);

            var svc = await _repo.GetServiceCatalogItemByIdAsync(dto.ServiceCatalogId.Value, ct);
            if (svc is null)
                return (null, "Service catalog item not found.", 404);

            serviceName = svc.Name;
        }
        else // part
        {
            if (!dto.ItemId.HasValue)
                return (null, "ItemId is required for part lines.", 400);

            var item = await _repo.GetItemByIdAsync(dto.ItemId.Value, ct);
            if (item is null)
                return (null, "Item not found.", 404);

            if (!string.Equals(item.ItemType, "spare_part", StringComparison.OrdinalIgnoreCase))
                return (null, "Item must have item_type = spare_part for part lines.", 400);

            itemName = item.Name;
        }

        var line = new AsWorkOrderLine
        {
            TenantId         = tenantId,
            WorkOrderId      = workOrderId,
            Type             = typeNorm,
            ServiceCatalogId = dto.ServiceCatalogId,
            ItemId           = dto.ItemId,
            Qty              = dto.Qty,
            Price            = dto.Price,
            Discount         = dto.Discount,
        };

        await _repo.AddWorkOrderLineAsync(line, ct);
        await _repo.SaveChangesAsync(ct);

        var lineDto = new WorkOrderLineDto(
            line.Id,
            line.Type,
            serviceName,
            itemName,
            line.Qty,
            line.Price,
            line.Discount,
            Math.Round((line.Price - line.Discount) * line.Qty, 2));

        return (lineDto, null, null);
    }

    public async Task<(bool Ok, string? Error, int? StatusCode)> RemoveLineAsync(
        Guid workOrderId, Guid lineId, CancellationToken ct = default)
    {
        var order = await _repo.GetWorkOrderByIdAsync(workOrderId, ct);
        if (order is null) return (false, "Work order not found.", 404);

        if (order.Status == WorkOrderStatus.Done || order.Status == WorkOrderStatus.Invoiced)
            return (false, "Cannot remove lines from a completed or invoiced work order.", 409);

        var line = order.Lines.FirstOrDefault(l => l.Id == lineId);
        if (line is null) return (false, "Line not found.", 404);

        _repo.RemoveWorkOrderLine(line);
        await _repo.SaveChangesAsync(ct);
        return (true, null, null);
    }

    // ── Complete work order (FEFO write-down) ─────────────────────────────────

    public async Task<(WorkOrderDetailDto? Order, string? Error, int? StatusCode)> CompleteWorkOrderAsync(
        Guid workOrderId, Guid performedBy, Guid tenantId, CancellationToken ct = default)
    {
        var order = await _repo.GetWorkOrderByIdAsync(workOrderId, ct);
        if (order is null)
            return (null, "Work order not found.", 404);

        // Only these statuses can be completed
        if (order.Status == WorkOrderStatus.Done || order.Status == WorkOrderStatus.Invoiced)
            return (null, "Work order is already completed or invoiced.", 409);

        var partLines = order.Lines
            .Where(l => string.Equals(l.Type, "part", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Pre-validate stock for ALL parts before consuming any (atomic guarantee)
        var consumptions = new List<(AsWorkOrderLine Line, Item Item, List<(ProductStock Batch, decimal Consume)> Batches)>();

        foreach (var line in partLines)
        {
            if (!line.ItemId.HasValue)
                return (null, "Part line missing ItemId.", 422);

            var item = await _repo.GetItemByIdAsync(line.ItemId.Value, ct);
            if (item is null)
                return (null, $"Item '{line.ItemId}' not found.", 422);

            var batches = await _repo.GetFefoOrderedAsync(line.ItemId.Value, ct);
            var available = batches.Sum(b => b.Quantity);

            if (available < line.Qty)
                return (null, $"Insufficient stock for item '{item.Name}'", 422);

            // Plan the FEFO consumption
            decimal remaining = line.Qty;
            var planned = new List<(ProductStock Batch, decimal Consume)>();
            foreach (var batch in batches)
            {
                if (remaining <= 0) break;
                var consume = Math.Min(remaining, batch.Quantity);
                planned.Add((batch, consume));
                remaining -= consume;
            }

            consumptions.Add((line, item, planned));
        }

        // All parts have sufficient stock — now apply write-downs
        foreach (var (line, item, planned) in consumptions)
        {
            foreach (var (batch, consume) in planned)
            {
                batch.Quantity -= consume;
                _repo.UpdateStock(batch);

                await _repo.AddStockEventAsync(new StockEvent
                {
                    TenantId       = tenantId,
                    EventType      = "auto_service_consumption",
                    ProductStockId = batch.Id,
                    ProductId      = item.Id,
                    QuantityDelta  = -consume,
                    Confidence     = 100,
                    Meta           = $"{{\"work_order_id\":\"{workOrderId}\"}}",
                    PerformedBy    = performedBy,
                }, ct);
            }
        }

        order.Status      = WorkOrderStatus.Done;
        order.CompletedAt = DateTimeOffset.UtcNow;
        order.UpdatedAt   = DateTimeOffset.UtcNow;
        _repo.UpdateWorkOrder(order);

        await _repo.SaveChangesAsync(ct);

        return (ToWorkOrderDetail(order), null, null);
    }

    // ── Mapping helpers ───────────────────────────────────────────────────────

    private static CustomerDetailDto ToCustomerDetail(AsCustomer c) =>
        new(c.Id, c.Name, c.Phone, c.Email, c.Notes,
            c.Vehicles.Select(v => new VehicleListItemDto(
                v.Id, v.CustomerId, c.Name,
                v.Brand, v.Model, v.Year, v.LicensePlate, v.Vin)).ToList());

    private static VehicleListItemDto ToVehicleListItem(AsVehicle v) =>
        new(v.Id, v.CustomerId, v.Customer?.Name,
            v.Brand, v.Model, v.Year, v.LicensePlate, v.Vin);

    private static VehicleDetailDto ToVehicleDetail(AsVehicle v) =>
        new(v.Id, v.CustomerId, v.Customer?.Name,
            v.Brand, v.Model, v.Year, v.Vin, v.LicensePlate, v.Mileage, v.Notes,
            v.WorkOrders.Select(o => new WorkOrderListItemDto(
                o.Id,
                v.Brand, v.Model, v.LicensePlate,
                o.Mechanic?.FullName,
                o.Status.ToString(),
                o.CreatedAt)).ToList());

    private static ServiceCatalogItemDto ToServiceCatalogDto(AsServiceCatalog s) =>
        new(s.Id, s.Name, s.Description, s.ItemId, s.DefaultPrice, s.DurationHours, s.IsActive);

    private static WorkOrderListItemDto ToWorkOrderListItem(AsWorkOrder o) =>
        new(o.Id,
            o.Vehicle?.Brand ?? string.Empty,
            o.Vehicle?.Model ?? string.Empty,
            o.Vehicle?.LicensePlate,
            o.Mechanic?.FullName,
            o.Status.ToString(),
            o.CreatedAt);

    private static WorkOrderDetailDto ToWorkOrderDetail(AsWorkOrder o)
    {
        var lines = o.Lines.Select(l => new WorkOrderLineDto(
            l.Id,
            l.Type,
            l.ServiceCatalog?.Name,
            l.Item?.Name,
            l.Qty,
            l.Price,
            l.Discount,
            Math.Round((l.Price - l.Discount) * l.Qty, 2))).ToList();

        var total = lines.Sum(l => l.Total);

        var vehicleDto = o.Vehicle is { } v
            ? new VehicleListItemDto(v.Id, v.CustomerId, v.Customer?.Name,
                v.Brand, v.Model, v.Year, v.LicensePlate, v.Vin)
            : new VehicleListItemDto(o.VehicleId, Guid.Empty, null,
                string.Empty, string.Empty, null, null, null);

        return new WorkOrderDetailDto(
            o.Id,
            vehicleDto,
            o.Mechanic?.FullName,
            o.Status.ToString(),
            o.Notes,
            o.CreatedAt,
            o.CompletedAt,
            lines,
            total);
    }
}
