using ShelfGuard.Application.Features.SupplySchedules.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.SupplySchedules;

public sealed class SupplyScheduleService : ISupplyScheduleService
{
    private readonly ISupplyScheduleRepository _repo;

    public SupplyScheduleService(ISupplyScheduleRepository repo) => _repo = repo;

    public async Task<List<SupplyScheduleDto>> GetAsync(
        Guid? storeId, Guid? supplierId, CancellationToken ct = default)
    {
        var schedules = await _repo.GetAsync(storeId, supplierId, ct);
        return schedules.Select(ToDto).ToList();
    }

    public async Task<(SupplyScheduleDto? Schedule, string? Error)> CreateAsync(
        Guid tenantId, CreateSupplyScheduleRequest request, CancellationToken ct = default)
    {
        var error = ValidateDays(request.DayOfWeek) ?? ValidateLeadDays(request.OrderLeadDays);
        if (error is not null) return (null, error);

        if (!await _repo.StoreExistsAsync(request.StoreId, ct))
            return (null, "Store not found.");
        if (!await _repo.SupplierExistsAsync(request.SupplierId, ct))
            return (null, "Supplier not found.");

        if (await _repo.ActiveExistsAsync(request.StoreId, request.SupplierId, excludeId: null, ct))
            return (null, "An active schedule for this store and supplier already exists.");

        var schedule = new SupplySchedule
        {
            TenantId = tenantId,
            StoreId = request.StoreId,
            SupplierId = request.SupplierId,
            DayOfWeek = Normalize(request.DayOfWeek),
            OrderLeadDays = request.OrderLeadDays,
            IsActive = true,
        };

        await _repo.AddAsync(schedule, ct);
        await _repo.SaveChangesAsync(ct);

        var saved = await _repo.GetByIdAsync(schedule.Id, ct);
        return (ToDto(saved!), null);
    }

    public async Task<(SupplyScheduleDto? Schedule, string? Error)> UpdateAsync(
        Guid id, UpdateSupplyScheduleRequest request, CancellationToken ct = default)
    {
        var schedule = await _repo.GetByIdAsync(id, ct);
        if (schedule is null)
            return (null, "Supply schedule not found.");

        var error = ValidateDays(request.DayOfWeek) ?? ValidateLeadDays(request.OrderLeadDays);
        if (error is not null) return (null, error);

        // Re-activating must not create a second active schedule for the pair
        if (request.IsActive
            && await _repo.ActiveExistsAsync(schedule.StoreId, schedule.SupplierId, excludeId: id, ct))
            return (null, "An active schedule for this store and supplier already exists.");

        schedule.DayOfWeek = Normalize(request.DayOfWeek);
        schedule.OrderLeadDays = request.OrderLeadDays;
        schedule.IsActive = request.IsActive;

        await _repo.SaveChangesAsync(ct);
        return (ToDto(schedule), null);
    }

    public async Task<string?> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var schedule = await _repo.GetByIdAsync(id, ct);
        if (schedule is null)
            return "Supply schedule not found.";

        schedule.IsActive = false;
        await _repo.SaveChangesAsync(ct);
        return null;
    }

    // ── helpers ────────────────────────────────────────────────────────────

    internal static string? ValidateDays(List<int>? days)
    {
        if (days is null || days.Count == 0)
            return "DayOfWeek must contain at least one day (1 = Monday … 7 = Sunday).";
        if (days.Any(d => d is < 1 or > 7))
            return "DayOfWeek values must be between 1 (Monday) and 7 (Sunday).";
        return null;
    }

    internal static string? ValidateLeadDays(int? leadDays) =>
        leadDays is < 0 or > 60 ? "OrderLeadDays must be between 0 and 60." : null;

    internal static List<int> Normalize(List<int> days) =>
        days.Distinct().OrderBy(d => d).ToList();

    private static SupplyScheduleDto ToDto(SupplySchedule s) => new(
        s.Id,
        s.StoreId,
        s.Store?.Name ?? "",
        s.SupplierId,
        s.Supplier?.Name ?? "",
        s.DayOfWeek,
        s.OrderLeadDays,
        s.IsActive
    );
}
