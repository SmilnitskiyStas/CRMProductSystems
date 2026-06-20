using ShelfGuard.Application.Features.Provider.Dtos;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Provider;

public sealed class ProviderScheduleService(
    IProviderScheduleRepository schedules,
    IUserRepository users) : IProviderScheduleService
{
    private static readonly string[] ProviderTeamRoles =
        [AppRoles.Provider, AppRoles.ProviderAdmin, AppRoles.ProviderAgent];

    public async Task<List<ProviderScheduleSlotDto>> GetAllAsync(Guid? userId, CancellationToken ct)
    {
        var slots = await schedules.GetAllAsync(userId, ct);
        return slots.Select(Map).ToList();
    }

    public async Task<(ProviderScheduleSlotDto? Slot, string? Error)> CreateAsync(
        CreateProviderScheduleSlotRequest req, CancellationToken ct)
    {
        if (req.DayOfWeek > 6)
            return (null, "DayOfWeek must be 0 (Monday) – 6 (Sunday).");

        if (!TimeOnly.TryParseExact(req.StartTime, "HH:mm", out var start))
            return (null, "StartTime must be in HH:mm format.");

        if (!TimeOnly.TryParseExact(req.EndTime, "HH:mm", out var end))
            return (null, "EndTime must be in HH:mm format.");

        if (end <= start)
            return (null, "EndTime must be after StartTime.");

        var user = await users.GetByIdAsync(req.UserId, ct);
        if (user is null || !ProviderTeamRoles.Contains(user.Role))
            return (null, "User not found or is not a provider team member.");

        var slot = new ProviderScheduleSlot
        {
            UserId    = req.UserId,
            DayOfWeek = req.DayOfWeek,
            StartTime = start,
            EndTime   = end,
            Notes     = req.Notes?.Trim(),
        };

        await schedules.AddAsync(slot, ct);
        await schedules.SaveChangesAsync(ct);

        // Attach navigation manually so Map works without reloading
        var dto = new ProviderScheduleSlotDto(
            Id:           slot.Id,
            UserId:       slot.UserId,
            UserFullName: user.FullName,
            UserRole:     user.Role,
            DayOfWeek:    slot.DayOfWeek,
            StartTime:    slot.StartTime.ToString("HH:mm"),
            EndTime:      slot.EndTime.ToString("HH:mm"),
            Notes:        slot.Notes,
            IsActive:     slot.IsActive);

        return (dto, null);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        var slot = await schedules.GetByIdAsync(id, ct);
        if (slot is null) return false;
        schedules.Remove(slot);
        await schedules.SaveChangesAsync(ct);
        return true;
    }

    private static ProviderScheduleSlotDto Map(ProviderScheduleSlot s) => new(
        Id:           s.Id,
        UserId:       s.UserId,
        UserFullName: s.User?.FullName ?? "",
        UserRole:     s.User?.Role ?? "",
        DayOfWeek:    s.DayOfWeek,
        StartTime:    s.StartTime.ToString("HH:mm"),
        EndTime:      s.EndTime.ToString("HH:mm"),
        Notes:        s.Notes,
        IsActive:     s.IsActive);
}
