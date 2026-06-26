namespace ShelfGuard.Application.Features.Catalog.Dtos;

public sealed record BarcodeProductLookupDto(
    string Name,
    List<string> Barcodes,
    string? Brand,
    string? Manufacturer,
    string? CountryOrigin,
    string? ImageUrl,
    int? ShelfLifeDays,
    string? Unit
);
