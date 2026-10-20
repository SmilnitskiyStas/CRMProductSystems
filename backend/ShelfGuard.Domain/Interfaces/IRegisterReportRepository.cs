namespace ShelfGuard.Domain.Interfaces;

public sealed record RegisterOperatorRow(Guid RegisterId, Guid CashierId, string CashierName, DateTime StartedAt);

/// <summary>Sales grouped by register / operator / payment type (cancelled receipts excluded).</summary>
public sealed record RegisterSalesAggregate(
    Guid RegisterId, Guid? CashierId, string? CashierName, string PaymentType, int Receipts, decimal Turnover);

public sealed record RegisterSessionRow(
    Guid Id, Guid CashierId, string CashierName, DateTime StartedAt, DateTime? EndedAt, string? EndReason);

public sealed record RegisterShiftRow(
    Guid Id, int ShiftNumber, Guid? CashierId, string? CashierName, DateTime OpenedAt, DateTime? ClosedAt,
    decimal? OpeningCash, decimal? ClosingCash, decimal TotalSales, int Receipts);

public sealed record PosEmployeeRow(
    Guid Id, string FullName, string Role, bool HasPin, DateTime? PinSetAt, bool PinLockedOut);

/// <summary>Read-only reporting queries for the register management page. RLS-scoped like <see cref="ICashRegisterRepository"/>.</summary>
public interface IRegisterReportRepository
{
    Task<IReadOnlyList<RegisterOperatorRow>> GetCurrentOperatorsAsync(
        IReadOnlyCollection<Guid> registerIds, CancellationToken ct = default);

    Task<IReadOnlyList<RegisterSalesAggregate>> GetSalesAggregatesAsync(
        IReadOnlyCollection<Guid> registerIds, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);

    Task<(IReadOnlyList<RegisterSessionRow> Items, int Total)> GetSessionsPageAsync(
        Guid registerId, int skip, int take, CancellationToken ct = default);

    Task<(IReadOnlyList<RegisterShiftRow> Items, int Total)> GetShiftsPageAsync(
        Guid registerId, int skip, int take, CancellationToken ct = default);

    /// <summary>Active employees of the store holding one of <paramref name="roles"/>.</summary>
    Task<IReadOnlyList<PosEmployeeRow>> GetPosEmployeesAsync(
        Guid storeId, IReadOnlyCollection<string> roles, CancellationToken ct = default);
}
