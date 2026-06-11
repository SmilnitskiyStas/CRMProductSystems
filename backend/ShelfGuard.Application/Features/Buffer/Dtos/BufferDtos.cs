namespace ShelfGuard.Application.Features.Buffer.Dtos;

public sealed record BufferDto(
    Guid StoreId,
    Guid ProductId,
    string? ProductName,
    decimal BufferTotal,
    decimal BufferGreen,
    decimal BufferYellow,
    decimal BufferRed,
    decimal LeadTimeDays,
    decimal OrderCycleDays,
    DateTime CalculatedAt
);

public sealed record RecalculateBuffersRequest(Guid StoreId);

public sealed record RecalculateBuffersResult(
    Guid StoreId,
    int BuffersCalculated,
    int SkippedNoSchedule
);
