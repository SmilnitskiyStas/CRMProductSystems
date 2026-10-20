using NSubstitute;
using ShelfGuard.Application.Features.Registers;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Registers;

public sealed class RegisterReportServiceTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid StoreA = Guid.NewGuid();
    private static readonly Guid StoreB = Guid.NewGuid();

    private readonly IRegisterService _registers = Substitute.For<IRegisterService>();
    private readonly IRegisterReportRepository _report = Substitute.For<IRegisterReportRepository>();
    private readonly IUserLocationRepository _locations = Substitute.For<IUserLocationRepository>();
    private readonly RegisterReportService _sut;

    public RegisterReportServiceTests() => _sut = new RegisterReportService(_registers, _report, _locations);

    private static RegisterCaller Manager(Guid store) =>
        new(Tenant, Guid.NewGuid(), AppRoles.StoreManager, store);

    private static RegisterDto Reg(Guid id, int number, Guid store) =>
        new(id, store, "S", number, "line", $"Till {number}", "active", null, null, null, false, null, DateTime.UtcNow);

    [Fact]
    public async Task Stats_AggregatesPerRegisterAndOperator_WithPaymentSplit()
    {
        var caller = Manager(StoreA);
        var reg = Guid.NewGuid();
        var anna = Guid.NewGuid();
        var ivan = Guid.NewGuid();
        _registers.ListAsync(caller, null, Arg.Any<CancellationToken>()).Returns([Reg(reg, 1, StoreA)]);
        _report.GetSalesAggregatesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([
                new RegisterSalesAggregate(reg, anna, "Anna", "cash", 2, 100m),
                new RegisterSalesAggregate(reg, anna, "Anna", "card", 1, 50m),
                new RegisterSalesAggregate(reg, ivan, "Ivan", "mixed", 1, 50m),
            ]);

        var stats = await _sut.GetStatsAsync(caller, null, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow);

        var s = Assert.Single(stats);
        Assert.Equal(200m, s.Totals.Turnover);
        Assert.Equal(4, s.Totals.Receipts);
        Assert.Equal(50m, s.Totals.AverageReceipt);
        Assert.Equal(100m, s.Totals.Cash);
        Assert.Equal(50m, s.Totals.Card);
        Assert.Equal(50m, s.Totals.Mixed);
        Assert.Equal(2, s.Operators.Count);
        Assert.Equal("Anna", s.Operators[0].CashierName);
        Assert.Equal(150m, s.Operators[0].Totals.Turnover);
    }

    [Fact]
    public async Task Stats_RegisterWithoutSales_ReturnsZeroTotals()
    {
        var caller = Manager(StoreA);
        _registers.ListAsync(caller, null, Arg.Any<CancellationToken>()).Returns([Reg(Guid.NewGuid(), 1, StoreA)]);
        _report.GetSalesAggregatesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var s = Assert.Single(await _sut.GetStatsAsync(caller, null, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow));
        Assert.Equal(0m, s.Totals.AverageReceipt);
        Assert.Empty(s.Operators);
    }

    [Fact]
    public async Task Sessions_ForRegisterOutsideScope_Returns404_AndNeverQueries()
    {
        var caller = Manager(StoreA);
        _registers.ListAsync(caller, null, Arg.Any<CancellationToken>()).Returns([Reg(Guid.NewGuid(), 1, StoreA)]);

        var (result, error, status) = await _sut.GetSessionsAsync(caller, Guid.NewGuid(), 1, 20);

        Assert.Null(result);
        Assert.NotNull(error);
        Assert.Equal(404, status);
        await _report.DidNotReceiveWithAnyArgs().GetSessionsPageAsync(default, default, default);
    }

    [Fact]
    public async Task Shifts_ClampsPageSize_AndComputesSkip()
    {
        var caller = Manager(StoreA);
        var reg = Guid.NewGuid();
        _registers.ListAsync(caller, null, Arg.Any<CancellationToken>()).Returns([Reg(reg, 1, StoreA)]);
        _report.GetShiftsPageAsync(reg, 100, 100, Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<RegisterShiftRow>)[], 0));

        var (result, _, _) = await _sut.GetShiftsAsync(caller, reg, 2, 5000);

        Assert.NotNull(result);
        Assert.Equal(100, result!.PageSize);
        Assert.Equal(2, result.Page);
    }

    [Fact]
    public async Task Employees_StoreManager_RolesCappedAtCallerRank_AndDefaultsToOwnStore()
    {
        var caller = Manager(StoreA);
        _report.GetPosEmployeesAsync(StoreA, Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns([new PosEmployeeRow(Guid.NewGuid(), "Anna", AppRoles.Cashier, true, DateTime.UtcNow, false)]);

        var (result, error, _) = await _sut.GetEmployeesAsync(caller, null); // no store requested -> own store

        Assert.Null(error);
        Assert.Single(result!);
        await _report.Received(1).GetPosEmployeesAsync(
            StoreA,
            Arg.Is<IReadOnlyCollection<string>>(r =>
                r.Contains(AppRoles.Cashier) && r.Contains(AppRoles.StoreManager)
                && !r.Contains(AppRoles.NetworkManager) && !r.Contains(AppRoles.EnterpriseAdmin)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Employees_StoreManager_OnUnassignedStore_Returns404()
    {
        var caller = Manager(StoreA);

        var (result, _, status) = await _sut.GetEmployeesAsync(caller, StoreB); // not assigned

        Assert.Null(result);
        Assert.Equal(404, status);
    }

    [Fact]
    public async Task Employees_StoreManager_OnExtraAssignedStore_IsAllowed()
    {
        var caller = Manager(StoreA);
        _locations.GetLocationIdsForUserAsync(Tenant, caller.UserId, Arg.Any<CancellationToken>())
            .Returns(new List<Guid> { StoreB }); // user_locations: manager runs two stores
        _report.GetPosEmployeesAsync(StoreB, Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns([new PosEmployeeRow(Guid.NewGuid(), "Bob", AppRoles.Cashier, true, DateTime.UtcNow, false)]);

        var (result, error, _) = await _sut.GetEmployeesAsync(caller, StoreB);

        Assert.Null(error);
        Assert.Single(result!);
    }

    [Fact]
    public async Task Employees_NetworkManager_OnUnassignedStore_Returns404()
    {
        var caller = new RegisterCaller(Tenant, Guid.NewGuid(), AppRoles.NetworkManager, null);
        _locations.GetLocationIdsForUserAsync(Tenant, caller.UserId, Arg.Any<CancellationToken>())
            .Returns(new List<Guid> { StoreA });

        var (result, _, status) = await _sut.GetEmployeesAsync(caller, StoreB);

        Assert.Null(result);
        Assert.Equal(404, status);
    }

    [Fact]
    public async Task CurrentOperators_ReturnsOnlyOnePerRegister()
    {
        var caller = Manager(StoreA);
        var reg = Guid.NewGuid();
        _registers.ListAsync(caller, null, Arg.Any<CancellationToken>()).Returns([Reg(reg, 1, StoreA)]);
        _report.GetCurrentOperatorsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([
                new RegisterOperatorRow(reg, Guid.NewGuid(), "New", DateTime.UtcNow),
                new RegisterOperatorRow(reg, Guid.NewGuid(), "Old", DateTime.UtcNow.AddHours(-1)),
            ]);

        var ops = await _sut.GetCurrentOperatorsAsync(caller, null);

        Assert.Equal("New", Assert.Single(ops).CashierName);
    }
}
