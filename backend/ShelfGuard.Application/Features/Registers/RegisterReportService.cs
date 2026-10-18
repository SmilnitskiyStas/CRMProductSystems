using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Registers;

public sealed record RegisterOperatorDto(Guid RegisterId, Guid CashierId, string CashierName, DateTime StartedAt);

/// <summary>Cash / card / mixed are turnover by receipt payment type (a mixed receipt is not split).</summary>
public sealed record SalesTotalsDto(
    decimal Turnover, int Receipts, decimal AverageReceipt, decimal Cash, decimal Card, decimal Mixed);

public sealed record OperatorStatsDto(Guid? CashierId, string? CashierName, SalesTotalsDto Totals);

public sealed record RegisterStatsDto(
    Guid RegisterId, int Number, string Name, SalesTotalsDto Totals, IReadOnlyList<OperatorStatsDto> Operators);

public sealed record RegisterSessionDto(
    Guid Id, Guid CashierId, string CashierName, DateTime StartedAt, DateTime? EndedAt, string? EndReason);

public sealed record RegisterShiftDto(
    Guid Id, int ShiftNumber, Guid? CashierId, string? CashierName, DateTime OpenedAt, DateTime? ClosedAt,
    decimal? OpeningCash, decimal? ClosingCash, decimal TotalSales, int Receipts);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public sealed record PosEmployeeDto(
    Guid Id, string FullName, string Role, bool HasPin, DateTime? PinSetAt, bool PinLockedOut);

/// <summary>Read-only data behind the register management page. Store scoping mirrors <see cref="RegisterService"/>.</summary>
public interface IRegisterReportService
{
    Task<IReadOnlyList<RegisterOperatorDto>> GetCurrentOperatorsAsync(
        RegisterCaller caller, Guid? storeId, CancellationToken ct = default);

    Task<IReadOnlyList<RegisterStatsDto>> GetStatsAsync(
        RegisterCaller caller, Guid? storeId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);

    Task<(PagedResult<RegisterSessionDto>? Result, string? Error, int? StatusCode)> GetSessionsAsync(
        RegisterCaller caller, Guid registerId, int page, int pageSize, CancellationToken ct = default);

    Task<(PagedResult<RegisterShiftDto>? Result, string? Error, int? StatusCode)> GetShiftsAsync(
        RegisterCaller caller, Guid registerId, int page, int pageSize, CancellationToken ct = default);

    Task<(IReadOnlyList<PosEmployeeDto>? Result, string? Error, int? StatusCode)> GetEmployeesAsync(
        RegisterCaller caller, Guid? storeId, CancellationToken ct = default);
}

public sealed class RegisterReportService : IRegisterReportService
{
    public const int MaxPageSize = 100;
    public const int MaxStatsRangeDays = 366;

    private readonly IRegisterService _registers;
    private readonly IRegisterReportRepository _report;
    private readonly IUserLocationRepository _userLocations;

    public RegisterReportService(
        IRegisterService registers, IRegisterReportRepository report, IUserLocationRepository userLocations)
    {
        _registers = registers;
        _report = report;
        _userLocations = userLocations;
    }

    public async Task<IReadOnlyList<RegisterOperatorDto>> GetCurrentOperatorsAsync(
        RegisterCaller caller, Guid? storeId, CancellationToken ct = default)
    {
        var visible = await _registers.ListAsync(caller, storeId, ct);
        if (visible.Count == 0) return [];

        var rows = await _report.GetCurrentOperatorsAsync(visible.Select(r => r.Id).ToList(), ct);
        // One active operator per register (a new sign-in replaces the old); keep the newest defensively.
        return rows.GroupBy(r => r.RegisterId).Select(g => g.First())
            .Select(r => new RegisterOperatorDto(r.RegisterId, r.CashierId, r.CashierName, r.StartedAt))
            .ToList();
    }

    public async Task<IReadOnlyList<RegisterStatsDto>> GetStatsAsync(
        RegisterCaller caller, Guid? storeId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        var visible = await _registers.ListAsync(caller, storeId, ct);
        if (visible.Count == 0 || toUtc <= fromUtc) return [];

        var rows = await _report.GetSalesAggregatesAsync(visible.Select(r => r.Id).ToList(), fromUtc, toUtc, ct);

        return visible.Select(reg =>
        {
            var mine = rows.Where(r => r.RegisterId == reg.Id).ToList();
            var operators = mine.GroupBy(r => r.CashierId)
                .Select(g => new OperatorStatsDto(g.Key, g.Select(x => x.CashierName).FirstOrDefault(x => x != null), Totals(g)))
                .OrderByDescending(o => o.Totals.Turnover)
                .ToList();
            return new RegisterStatsDto(reg.Id, reg.Number, reg.Name, Totals(mine), operators);
        }).ToList();
    }

    public async Task<(PagedResult<RegisterSessionDto>? Result, string? Error, int? StatusCode)> GetSessionsAsync(
        RegisterCaller caller, Guid registerId, int page, int pageSize, CancellationToken ct = default)
    {
        if (!await IsVisibleAsync(caller, registerId, ct)) return (null, "Register not found.", 404);
        (page, pageSize) = Normalize(page, pageSize);

        var (items, total) = await _report.GetSessionsPageAsync(registerId, (page - 1) * pageSize, pageSize, ct);
        return (new PagedResult<RegisterSessionDto>(
            items.Select(s => new RegisterSessionDto(s.Id, s.CashierId, s.CashierName, s.StartedAt, s.EndedAt, s.EndReason)).ToList(),
            total, page, pageSize), null, null);
    }

    public async Task<(PagedResult<RegisterShiftDto>? Result, string? Error, int? StatusCode)> GetShiftsAsync(
        RegisterCaller caller, Guid registerId, int page, int pageSize, CancellationToken ct = default)
    {
        if (!await IsVisibleAsync(caller, registerId, ct)) return (null, "Register not found.", 404);
        (page, pageSize) = Normalize(page, pageSize);

        var (items, total) = await _report.GetShiftsPageAsync(registerId, (page - 1) * pageSize, pageSize, ct);
        return (new PagedResult<RegisterShiftDto>(
            items.Select(s => new RegisterShiftDto(
                s.Id, s.ShiftNumber, s.CashierId, s.CashierName, s.OpenedAt, s.ClosedAt,
                s.OpeningCash, s.ClosingCash, s.TotalSales, s.Receipts)).ToList(),
            total, page, pageSize), null, null);
    }

    public async Task<(IReadOnlyList<PosEmployeeDto>? Result, string? Error, int? StatusCode)> GetEmployeesAsync(
        RegisterCaller caller, Guid? storeId, CancellationToken ct = default)
    {
        if (storeId is null && caller.Role == AppRoles.StoreManager) storeId = caller.StoreId;
        if (storeId is null) return (null, "storeId is required.", 400);

        // Same assignment rule as register management: user_locations plus the primary store.
        var allowed = caller.Role switch
        {
            AppRoles.EnterpriseAdmin or AppRoles.Provider => true,
            AppRoles.StoreManager or AppRoles.NetworkManager =>
                (await _userLocations.GetLocationIdsForUserAsync(caller.TenantId, caller.UserId, ct)).Contains(storeId.Value)
                || caller.StoreId == storeId,
            _ => false,
        };
        if (!allowed) return (null, "Store not found.", 404);

        // Same rule as SetPin: never list someone the caller could not manage (outranking roles).
        var callerRank = RegisterService.PosRoleRank.GetValueOrDefault(
            caller.Role, caller.Role == AppRoles.Provider ? int.MaxValue : 0);
        var roles = RegisterService.PosRoleRank.Where(kv => kv.Value <= callerRank).Select(kv => kv.Key).ToList();

        var rows = await _report.GetPosEmployeesAsync(storeId.Value, roles, ct);
        return (rows.Select(e => new PosEmployeeDto(e.Id, e.FullName, e.Role, e.HasPin, e.PinSetAt, e.PinLockedOut)).ToList(), null, null);
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private async Task<bool> IsVisibleAsync(RegisterCaller caller, Guid registerId, CancellationToken ct) =>
        (await _registers.ListAsync(caller, null, ct)).Any(r => r.Id == registerId);

    private static (int Page, int PageSize) Normalize(int page, int pageSize) =>
        (Math.Max(page, 1), Math.Clamp(pageSize <= 0 ? 20 : pageSize, 1, MaxPageSize));

    private static SalesTotalsDto Totals(IEnumerable<RegisterSalesAggregate> rows)
    {
        decimal turnover = 0, cash = 0, card = 0, mixed = 0;
        var receipts = 0;
        foreach (var r in rows)
        {
            turnover += r.Turnover;
            receipts += r.Receipts;
            switch (r.PaymentType)
            {
                case "cash": cash += r.Turnover; break;
                case "card": card += r.Turnover; break;
                default: mixed += r.Turnover; break;
            }
        }
        return new SalesTotalsDto(turnover, receipts, receipts == 0 ? 0 : Math.Round(turnover / receipts, 2), cash, card, mixed);
    }
}
