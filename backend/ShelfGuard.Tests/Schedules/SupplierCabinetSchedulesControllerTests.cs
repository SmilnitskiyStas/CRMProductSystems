using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using ShelfGuard.Api.Controllers;
using ShelfGuard.Application.Features.Marketplace;
using ShelfGuard.Application.Features.Schedules;
using ShelfGuard.Domain.Constants;
using Xunit;

namespace ShelfGuard.Tests.Schedules;

/// <summary>
/// Supplier-portal expansion Phase 5 (plan 1-partitioned-book.md D6, request #6) — the thin
/// <see cref="SupplierCabinetSchedulesController"/> pass-through to the shared
/// <see cref="IScheduleService"/>. Orchestration (WeekStart=Monday, overlap detection, tenant
/// scoping) is already covered by <c>ScheduleServiceTests</c> + the retail controller; this suite
/// only covers what the supplier wrapper adds: the supplier tenant id is resolved from the JWT
/// and threaded into the service, and every mutation is gated by the <c>workforce_management</c>
/// supplier permission while GET list/detail is not.
/// </summary>
public sealed class SupplierCabinetSchedulesControllerTests
{
    private readonly IScheduleService _schedules = Substitute.For<IScheduleService>();
    private readonly ISupplierCabinetService _cabinet = Substitute.For<ISupplierCabinetService>();
    private readonly SupplierCabinetSchedulesController _controller;

    private static readonly Guid SupplierTenant = Guid.NewGuid();
    private static readonly Guid CallerUser = Guid.NewGuid();
    private static readonly Guid WarehouseId = Guid.NewGuid();

    public SupplierCabinetSchedulesControllerTests()
    {
        _controller = new SupplierCabinetSchedulesController(_schedules, _cabinet)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    /// <param name="permissions">
    /// null → no "permissions" claim at all (tenant owner / unrestricted, matches
    /// <c>SupplierPermissionAuthorization</c>). Otherwise the exact comma-joined claim value.
    /// </param>
    private void AuthenticateAs(Guid tenantId, Guid userId, string? permissions)
    {
        var claims = new List<Claim>
        {
            new("tenant_id", tenantId.ToString()),
            new(ClaimTypes.NameIdentifier, userId.ToString()),
        };
        if (permissions is not null)
            claims.Add(new Claim("permissions", permissions));

        _controller.ControllerContext.HttpContext.User =
            new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    private static WorkScheduleDto SampleSchedule(Guid id) => new(
        id, WarehouseId, "Основний склад", "Тиждень 1",
        new DateOnly(2026, 9, 7), "draft", 0, DateTime.UtcNow);

    // ── GET list — no permission gate, tenant resolved from JWT ────────────────

    [Fact]
    public async Task GetAll_forwards_the_supplier_tenant_from_the_jwt_without_a_permission_gate()
    {
        // A restricted staff role WITHOUT workforce_management can still read schedules.
        AuthenticateAs(SupplierTenant, CallerUser, permissions: "catalog_management");
        _schedules.GetSchedulesAsync(SupplierTenant, null, null, Arg.Any<CancellationToken>())
            .Returns(new List<WorkScheduleDto> { SampleSchedule(Guid.NewGuid()) });

        var result = await _controller.GetAll(null, null, default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var list = Assert.IsAssignableFrom<List<WorkScheduleDto>>(ok.Value);
        Assert.Single(list);
        await _schedules.Received(1).GetSchedulesAsync(SupplierTenant, null, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAll_returns_forbid_when_the_jwt_has_no_tenant()
    {
        _controller.ControllerContext.HttpContext.User =
            new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, CallerUser.ToString()) }, "TestAuth"));

        var result = await _controller.GetAll(null, null, default);

        Assert.IsType<ForbidResult>(result);
        await _schedules.DidNotReceive().GetSchedulesAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<DateOnly?>(), Arg.Any<CancellationToken>());
    }

    // ── POST create — round-trips through the service with the resolved tenant ─

    [Fact]
    public async Task Create_threads_the_supplier_tenant_and_caller_into_the_service_and_returns_201()
    {
        AuthenticateAs(SupplierTenant, CallerUser, permissions: null); // owner / unrestricted
        var created = SampleSchedule(Guid.NewGuid());
        var dto = new CreateWorkScheduleDto(WarehouseId, "Тиждень 1", new DateOnly(2026, 9, 7));
        _schedules.CreateScheduleAsync(SupplierTenant, CallerUser, dto, Arg.Any<CancellationToken>())
            .Returns((created, (string?)null));

        var result = await _controller.Create(dto, default);

        var createdResult = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Same(created, createdResult.Value);
        await _schedules.Received(1).CreateScheduleAsync(SupplierTenant, CallerUser, dto, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_maps_a_service_error_to_400()
    {
        AuthenticateAs(SupplierTenant, CallerUser, permissions: null);
        var dto = new CreateWorkScheduleDto(WarehouseId, "Тиждень 1", new DateOnly(2026, 9, 8)); // not a Monday
        _schedules.CreateScheduleAsync(SupplierTenant, CallerUser, dto, Arg.Any<CancellationToken>())
            .Returns(((WorkScheduleDto?)null, "WeekStart must be a Monday."));

        var result = await _controller.Create(dto, default);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    // ── workforce_management gate on mutations ────────────────────────────────

    [Fact]
    public async Task Create_returns_forbid_without_the_workforce_management_permission()
    {
        AuthenticateAs(SupplierTenant, CallerUser, permissions: "catalog_management,client_reviews");
        var dto = new CreateWorkScheduleDto(WarehouseId, "Тиждень 1", new DateOnly(2026, 9, 7));

        var result = await _controller.Create(dto, default);

        Assert.IsType<ForbidResult>(result);
        await _schedules.DidNotReceive().CreateScheduleAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CreateWorkScheduleDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddShift_returns_forbid_without_the_workforce_management_permission()
    {
        AuthenticateAs(SupplierTenant, CallerUser, permissions: "task_board");
        var dto = new CreateShiftDto(Guid.NewGuid(), WarehouseId, new DateOnly(2026, 9, 7),
            new TimeOnly(9, 0), new TimeOnly(18, 0));

        var result = await _controller.AddShift(Guid.NewGuid(), dto, default);

        Assert.IsType<ForbidResult>(result);
        await _schedules.DidNotReceive().AddShiftAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CreateShiftDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddShift_with_the_permission_delegates_to_the_service()
    {
        AuthenticateAs(SupplierTenant, CallerUser, permissions: SupplierPermissions.WorkforceManagement);
        var scheduleId = Guid.NewGuid();
        var dto = new CreateShiftDto(Guid.NewGuid(), WarehouseId, new DateOnly(2026, 9, 7),
            new TimeOnly(9, 0), new TimeOnly(18, 0));
        var shift = new ScheduleShiftDto(Guid.NewGuid(), dto.UserId, "Іван", WarehouseId, dto.ShiftDate,
            dto.StartTime, dto.EndTime, 60, null, null, "scheduled");
        _schedules.AddShiftAsync(scheduleId, SupplierTenant, dto, Arg.Any<CancellationToken>())
            .Returns((shift, (string?)null));

        var result = await _controller.AddShift(scheduleId, dto, default);

        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        await _schedules.Received(1).AddShiftAsync(scheduleId, SupplierTenant, dto, Arg.Any<CancellationToken>());
    }

    // ── my-shifts — resolves the caller, no permission gate ───────────────────

    [Fact]
    public async Task GetMyShifts_forwards_the_caller_and_tenant_from_the_jwt()
    {
        AuthenticateAs(SupplierTenant, CallerUser, permissions: "catalog_management");
        var from = new DateOnly(2026, 9, 7);
        var to = new DateOnly(2026, 9, 13);
        _schedules.GetMyShiftsAsync(CallerUser, SupplierTenant, from, to, Arg.Any<CancellationToken>())
            .Returns(new List<ScheduleShiftDto>());

        var result = await _controller.GetMyShifts(from, to, default);

        Assert.IsType<OkObjectResult>(result);
        await _schedules.Received(1).GetMyShiftsAsync(CallerUser, SupplierTenant, from, to, Arg.Any<CancellationToken>());
    }

    // ── assignee picker — gated by workforce_management ───────────────────────

    [Fact]
    public async Task GetStaff_returns_forbid_without_the_workforce_management_permission()
    {
        AuthenticateAs(SupplierTenant, CallerUser, permissions: "catalog_management");

        var result = await _controller.GetStaff(default);

        Assert.IsType<ForbidResult>(result);
        await _cabinet.DidNotReceive().GetStaffAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
