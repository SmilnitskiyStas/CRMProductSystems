namespace ShelfGuard.Application.Features.Movements.Dtos;

public sealed record StockHistoryPointDto(DateOnly Date, decimal Stock);
public sealed record StockHistoryDto(decimal CurrentStock, IReadOnlyList<StockHistoryPointDto> Points);
