using NSubstitute;
using ShelfGuard.Application.Features.Schedules;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Schedules;

/// <summary>
/// TASK-360 (Block 9 pre-launch audit) — Schedules had zero test coverage. Focused on the gap
/// the audit found: overlap ("накладання") was only checked at publish time
/// (DetectShiftConflicts, inside UpdateScheduleAsync when Status transitions to "published").
/// Adding a shift to an already-published schedule, or editing an existing shift's time window,
/// never re-checked for double-booking the same employee on the same day. Fixed by adding the
/// same overlap rule (FindOverlap) directly to AddShiftAsync/UpdateShiftAsync.
/// </summary>
public sealed class ScheduleServiceTests
{
    private readonly IScheduleRepository _repo = Substitute.For<IScheduleRepository>();
    private readonly ScheduleService _sut;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _locationId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    public ScheduleServiceTests() => _sut = new ScheduleService(_repo);

    private WorkSchedule MakeSchedule(Guid id) => new()
    {
        Id = id,
        TenantId = _tenantId,
        LocationId = _locationId,
        Name = "Week 1",
        WeekStart = new DateOnly(2026, 7, 13),
        Status = "draft",
    };

    // ── AddShiftAsync: overlap guard ────────────────────────────────────────────

    [Fact]
    public async Task AddShiftAsync_OverlapsExistingShift_ReturnsError_AndDoesNotSave()
    {
        var scheduleId = Guid.NewGuid();
        var shiftDate = new DateOnly(2026, 7, 13);
        _repo.GetByIdAsync(scheduleId, _tenantId, Arg.Any<CancellationToken>()).Returns(MakeSchedule(scheduleId));
        _repo.LocationExistsAsync(_locationId, _tenantId, Arg.Any<CancellationToken>()).Returns(true);

        var existingShift = new ScheduleShift
        {
            TenantId = _tenantId,
            ScheduleId = scheduleId,
            UserId = _userId,
            LocationId = _locationId,
            ShiftDate = shiftDate,
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(17, 0),
            Status = "scheduled",
        };
        _repo.GetShiftsByUserAsync(_userId, _tenantId, shiftDate, shiftDate, Arg.Any<CancellationToken>())
            .Returns([existingShift]);

        // Overlaps the 09:00-17:00 shift above (starts before it ends).
        var dto = new CreateShiftDto(_userId, _locationId, shiftDate, new TimeOnly(16, 0), new TimeOnly(20, 0));
        var (shift, error) = await _sut.AddShiftAsync(scheduleId, _tenantId, dto);

        Assert.Null(shift);
        Assert.Contains("overlap", error, StringComparison.OrdinalIgnoreCase);
        await _repo.DidNotReceive().AddShiftAsync(Arg.Any<ScheduleShift>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddShiftAsync_SameDayDifferentUser_NoConflict_Succeeds()
    {
        var scheduleId = Guid.NewGuid();
        var shiftDate = new DateOnly(2026, 7, 13);
        var otherUserId = Guid.NewGuid();
        _repo.GetByIdAsync(scheduleId, _tenantId, Arg.Any<CancellationToken>()).Returns(MakeSchedule(scheduleId));
        _repo.LocationExistsAsync(_locationId, _tenantId, Arg.Any<CancellationToken>()).Returns(true);
        // The overlap check only queries shifts for this user on this day — no existing shift
        // for otherUserId, so it correctly comes back empty.
        _repo.GetShiftsByUserAsync(otherUserId, _tenantId, shiftDate, shiftDate, Arg.Any<CancellationToken>())
            .Returns([]);
        _repo.AddShiftAsync(Arg.Any<ScheduleShift>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<ScheduleShift>());
        _repo.GetShiftByIdAsync(Arg.Any<Guid>(), _tenantId, Arg.Any<CancellationToken>())
            .Returns(ci => new ScheduleShift
            {
                Id = ci.ArgAt<Guid>(0),
                TenantId = _tenantId,
                ScheduleId = scheduleId,
                UserId = otherUserId,
                LocationId = _locationId,
                ShiftDate = shiftDate,
                StartTime = new TimeOnly(9, 0),
                EndTime = new TimeOnly(17, 0),
                Status = "scheduled",
            });

        var dto = new CreateShiftDto(otherUserId, _locationId, shiftDate, new TimeOnly(9, 0), new TimeOnly(17, 0));
        var (shift, error) = await _sut.AddShiftAsync(scheduleId, _tenantId, dto);

        Assert.Null(error);
        Assert.NotNull(shift);
        await _repo.Received(1).AddShiftAsync(Arg.Any<ScheduleShift>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddShiftAsync_BackToBackNotOverlapping_Succeeds()
    {
        // 09:00-17:00 then 17:00-21:00 for the same user/day — end == start, not an overlap.
        var scheduleId = Guid.NewGuid();
        var shiftDate = new DateOnly(2026, 7, 13);
        _repo.GetByIdAsync(scheduleId, _tenantId, Arg.Any<CancellationToken>()).Returns(MakeSchedule(scheduleId));
        _repo.LocationExistsAsync(_locationId, _tenantId, Arg.Any<CancellationToken>()).Returns(true);

        var existingShift = new ScheduleShift
        {
            TenantId = _tenantId, ScheduleId = scheduleId, UserId = _userId, LocationId = _locationId,
            ShiftDate = shiftDate, StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(17, 0), Status = "scheduled",
        };
        _repo.GetShiftsByUserAsync(_userId, _tenantId, shiftDate, shiftDate, Arg.Any<CancellationToken>())
            .Returns([existingShift]);
        _repo.AddShiftAsync(Arg.Any<ScheduleShift>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<ScheduleShift>());
        _repo.GetShiftByIdAsync(Arg.Any<Guid>(), _tenantId, Arg.Any<CancellationToken>())
            .Returns(ci => new ScheduleShift
            {
                Id = ci.ArgAt<Guid>(0), TenantId = _tenantId, ScheduleId = scheduleId, UserId = _userId,
                LocationId = _locationId, ShiftDate = shiftDate,
                StartTime = new TimeOnly(17, 0), EndTime = new TimeOnly(21, 0), Status = "scheduled",
            });

        var dto = new CreateShiftDto(_userId, _locationId, shiftDate, new TimeOnly(17, 0), new TimeOnly(21, 0));
        var (shift, error) = await _sut.AddShiftAsync(scheduleId, _tenantId, dto);

        Assert.Null(error);
        Assert.NotNull(shift);
    }

    // ── UpdateShiftAsync: overlap guard on time-window edits ────────────────────

    [Fact]
    public async Task UpdateShiftAsync_ExtendingIntoAnotherShift_ReturnsError_AndDoesNotSave()
    {
        var scheduleId = Guid.NewGuid();
        var shiftDate = new DateOnly(2026, 7, 13);

        var shiftToUpdate = new ScheduleShift
        {
            TenantId = _tenantId, ScheduleId = scheduleId, UserId = _userId, LocationId = _locationId,
            ShiftDate = shiftDate, StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(13, 0), Status = "scheduled",
        };
        var otherShift = new ScheduleShift
        {
            TenantId = _tenantId, ScheduleId = scheduleId, UserId = _userId, LocationId = _locationId,
            ShiftDate = shiftDate, StartTime = new TimeOnly(14, 0), EndTime = new TimeOnly(18, 0), Status = "scheduled",
        };

        _repo.GetShiftByIdAsync(shiftToUpdate.Id, _tenantId, Arg.Any<CancellationToken>()).Returns(shiftToUpdate);
        _repo.GetShiftsByUserAsync(_userId, _tenantId, shiftDate, shiftDate, Arg.Any<CancellationToken>())
            .Returns([shiftToUpdate, otherShift]);

        // Extend the 09:00-13:00 shift to 09:00-15:00 — now overlaps otherShift (14:00-18:00).
        var dto = new UpdateShiftDto(new TimeOnly(9, 0), new TimeOnly(15, 0), 60, null, null, "scheduled");
        var (shift, error) = await _sut.UpdateShiftAsync(scheduleId, shiftToUpdate.Id, _tenantId, dto);

        Assert.Null(shift);
        Assert.Contains("overlap", error, StringComparison.OrdinalIgnoreCase);
        await _repo.DidNotReceive().UpdateShiftAsync(Arg.Any<ScheduleShift>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateShiftAsync_CancellingOverlappingShift_SkipsOverlapCheck()
    {
        // Cancelling a shift should never be blocked by the overlap guard (it's the shift being
        // removed from the schedule, not one being newly placed into it).
        var scheduleId = Guid.NewGuid();
        var shiftDate = new DateOnly(2026, 7, 13);

        var shiftToCancel = new ScheduleShift
        {
            TenantId = _tenantId, ScheduleId = scheduleId, UserId = _userId, LocationId = _locationId,
            ShiftDate = shiftDate, StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(13, 0), Status = "scheduled",
        };
        _repo.GetShiftByIdAsync(shiftToCancel.Id, _tenantId, Arg.Any<CancellationToken>()).Returns(shiftToCancel);

        var dto = new UpdateShiftDto(new TimeOnly(9, 0), new TimeOnly(13, 0), 60, null, null, "cancelled");
        var (shift, error) = await _sut.UpdateShiftAsync(scheduleId, shiftToCancel.Id, _tenantId, dto);

        Assert.Null(error);
        Assert.NotNull(shift);
        await _repo.Received(1).UpdateShiftAsync(shiftToCancel, Arg.Any<CancellationToken>());
    }
}
