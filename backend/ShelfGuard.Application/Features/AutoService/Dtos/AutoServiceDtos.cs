namespace ShelfGuard.Application.Features.AutoService.Dtos;

// ── Customers ─────────────────────────────────────────────────────────────────

public record CustomerListItemDto(
    Guid Id,
    string Name,
    string? Phone,
    string? Email,
    int VehicleCount);

public record CustomerDetailDto(
    Guid Id,
    string Name,
    string? Phone,
    string? Email,
    string? Notes,
    List<VehicleListItemDto> Vehicles);

public record CustomerCreateDto(
    string Name,
    string? Phone,
    string? Email,
    string? Notes);

public record CustomerUpdateDto(
    string? Name,
    string? Phone,
    string? Email,
    string? Notes);

// ── Vehicles ──────────────────────────────────────────────────────────────────

public record VehicleListItemDto(
    Guid Id,
    Guid CustomerId,
    string? CustomerName,
    string Brand,
    string Model,
    int? Year,
    string? LicensePlate,
    string? Vin);

public record VehicleDetailDto(
    Guid Id,
    Guid CustomerId,
    string? CustomerName,
    string Brand,
    string Model,
    int? Year,
    string? Vin,
    string? LicensePlate,
    int? Mileage,
    string? Notes,
    List<WorkOrderListItemDto> WorkOrders);

public record VehicleCreateDto(
    Guid CustomerId,
    string Brand,
    string Model,
    int? Year,
    string? Vin,
    string? LicensePlate,
    int? Mileage,
    string? Notes);

public record VehicleUpdateDto(
    string? Brand,
    string? Model,
    int? Year,
    string? Vin,
    string? LicensePlate,
    int? Mileage,
    string? Notes);

// ── Service Catalog ───────────────────────────────────────────────────────────

public record ServiceCatalogItemDto(
    Guid Id,
    string Name,
    string? Description,
    Guid? ItemId,
    decimal? DefaultPrice,
    decimal? DurationHours,
    bool IsActive);

public record ServiceCatalogCreateDto(
    string Name,
    string? Description,
    Guid? ItemId,
    decimal? DefaultPrice,
    decimal? DurationHours);

public record ServiceCatalogUpdateDto(
    string? Name,
    string? Description,
    Guid? ItemId,
    decimal? DefaultPrice,
    decimal? DurationHours,
    bool? IsActive);

// ── Work Orders ───────────────────────────────────────────────────────────────

public record WorkOrderListItemDto(
    Guid Id,
    string VehicleBrand,
    string VehicleModel,
    string? LicensePlate,
    string? MechanicName,
    string Status,
    DateTimeOffset CreatedAt);

public record WorkOrderDetailDto(
    Guid Id,
    VehicleListItemDto Vehicle,
    string? MechanicName,
    string Status,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    List<WorkOrderLineDto> Lines,
    decimal TotalAmount);

public record WorkOrderCreateDto(
    Guid VehicleId,
    Guid? MechanicUserId,
    string? Notes);

public record WorkOrderUpdateDto(
    string? Status,
    Guid? MechanicUserId,
    string? Notes);

public record WorkOrderLineCreateDto(
    string Type,
    Guid? ServiceCatalogId,
    Guid? ItemId,
    decimal Qty,
    decimal Price,
    decimal Discount);

public record WorkOrderLineDto(
    Guid Id,
    string Type,
    string? ServiceName,
    string? ItemName,
    decimal Qty,
    decimal Price,
    decimal Discount,
    decimal Total);
