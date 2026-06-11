using ShelfGuard.Application.Features.Events.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Events;

public sealed class EventService : IEventService
{
    private static readonly HashSet<string> ValidEventTypes =
        ["holiday", "promo", "local_event", "season_start", "custom"];
    private static readonly HashSet<string> ValidScopes = ["network", "store"];
    private static readonly HashSet<string> ValidCoefScopes = ["category", "segment", "product"];

    private readonly IEventRepository _repo;

    public EventService(IEventRepository repo) => _repo = repo;

    public async Task<List<DemandEventDto>> GetAsync(
        DateOnly? from, DateOnly? to, Guid? storeId, CancellationToken ct = default)
    {
        var events = await _repo.GetAsync(from, to, storeId, ct);
        return events.Select(ToDto).ToList();
    }

    public async Task<(DemandEventDto? Event, string? Error)> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var ev = await _repo.GetByIdAsync(id, ct);
        return ev is null ? (null, "Event not found.") : (ToDto(ev), null);
    }

    public async Task<(DemandEventDto? Event, string? Error)> CreateAsync(
        Guid tenantId, Guid? createdBy, UpsertEventRequest request, CancellationToken ct = default)
    {
        var error = Validate(request);
        if (error is not null) return (null, error);

        var ev = new DemandEvent
        {
            TenantId = tenantId,
            Name = request.Name.Trim(),
            EventType = request.EventType,
            Scope = request.Scope,
            StoreId = request.Scope == "store" ? request.StoreId : null,
            StartsAt = request.StartsAt,
            EndsAt = request.EndsAt,
            IsRecurring = request.IsRecurring,
            Notes = request.Notes,
            CreatedBy = createdBy,
        };

        await _repo.AddAsync(ev, ct);
        await _repo.SaveChangesAsync(ct);
        return (ToDto(ev), null);
    }

    public async Task<(DemandEventDto? Event, string? Error)> UpdateAsync(
        Guid id, UpsertEventRequest request, CancellationToken ct = default)
    {
        var ev = await _repo.GetByIdAsync(id, ct);
        if (ev is null) return (null, "Event not found.");

        var error = Validate(request);
        if (error is not null) return (null, error);

        ev.Name = request.Name.Trim();
        ev.EventType = request.EventType;
        ev.Scope = request.Scope;
        ev.StoreId = request.Scope == "store" ? request.StoreId : null;
        ev.StartsAt = request.StartsAt;
        ev.EndsAt = request.EndsAt;
        ev.IsRecurring = request.IsRecurring;
        ev.Notes = request.Notes;

        await _repo.SaveChangesAsync(ct);
        return (ToDto(ev), null);
    }

    public async Task<string?> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var ev = await _repo.GetByIdAsync(id, ct);
        if (ev is null) return "Event not found.";

        _repo.Remove(ev);
        await _repo.SaveChangesAsync(ct);
        return null;
    }

    public async Task<(EventCoefficientDto? Coefficient, string? Error)> AddCoefficientAsync(
        Guid eventId, CreateCoefficientRequest request, CancellationToken ct = default)
    {
        var ev = await _repo.GetByIdAsync(eventId, ct);
        if (ev is null) return (null, "Event not found.");

        if (!ValidCoefScopes.Contains(request.ScopeType))
            return (null, "ScopeType must be category, segment or product.");
        if (request.Coefficient is <= 0 or > 10)
            return (null, "Coefficient must be between 0.01 and 10.");

        var coef = new DemandEventCoefficient
        {
            EventId = eventId,
            ScopeType = request.ScopeType,
            ScopeId = request.ScopeId,
            Coefficient = request.Coefficient,
            Source = "manual",
        };

        await _repo.AddCoefficientAsync(coef, ct);
        await _repo.SaveChangesAsync(ct);
        return (ToCoefDto(coef), null);
    }

    public async Task<(EventCoefficientDto? Coefficient, string? Error)> UpdateCoefficientAsync(
        Guid eventId, Guid coefId, UpdateCoefficientRequest request, CancellationToken ct = default)
    {
        var coef = await _repo.GetCoefficientAsync(coefId, ct);
        if (coef is null || coef.EventId != eventId)
            return (null, "Coefficient not found.");
        if (request.Coefficient is <= 0 or > 10)
            return (null, "Coefficient must be between 0.01 and 10.");

        coef.Coefficient = request.Coefficient;
        coef.Source = "manual";
        await _repo.SaveChangesAsync(ct);
        return (ToCoefDto(coef), null);
    }

    public async Task<(SeedDefaultsResult? Result, string? Error)> SeedDefaultsAsync(
        Guid tenantId, CancellationToken ct = default)
    {
        // Idempotency: skip when tenant already has holidays.
        if (await _repo.CountByNameAndTypeAsync(tenantId, "holiday", ct) > 0)
            return (new SeedDefaultsResult(0, 0), null);

        var year = DateTime.UtcNow.Year;
        int events = 0, coefs = 0;

        foreach (var (name, starts, ends, coefficients) in DefaultHolidays.All(year))
        {
            var ev = new DemandEvent
            {
                TenantId = tenantId,
                Name = name,
                EventType = "holiday",
                Scope = "network",
                StartsAt = starts,
                EndsAt = ends,
                IsRecurring = true,
                Notes = "Стандартне свято (створено автоматично, коефіцієнти редагуються)",
            };
            await _repo.AddAsync(ev, ct);
            events++;

            foreach (var (categoryName, k) in coefficients)
            {
                // Category links are resolved by name later via UI; seeded as category-scope
                // placeholders with null ScopeId documented in Notes for the manager.
                await _repo.AddCoefficientAsync(new DemandEventCoefficient
                {
                    EventId = ev.Id,
                    ScopeType = "category",
                    ScopeId = null,
                    Coefficient = k,
                    Source = "manual",
                }, ct);
                coefs++;
                _ = categoryName;
            }
        }

        await _repo.SaveChangesAsync(ct);
        return (new SeedDefaultsResult(events, coefs), null);
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private static string? Validate(UpsertEventRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Name)) return "Name is required.";
        if (!ValidEventTypes.Contains(r.EventType))
            return "EventType must be holiday, promo, local_event, season_start or custom.";
        if (!ValidScopes.Contains(r.Scope)) return "Scope must be network or store.";
        if (r.Scope == "store" && r.StoreId is null) return "StoreId is required for store scope.";
        if (!r.IsRecurring && r.EndsAt < r.StartsAt) return "EndsAt must not be before StartsAt.";
        return null;
    }

    private static DemandEventDto ToDto(DemandEvent e) => new(
        e.Id, e.Name, e.EventType, e.Scope, e.StoreId,
        e.StartsAt.ToString("yyyy-MM-dd"), e.EndsAt.ToString("yyyy-MM-dd"),
        e.IsRecurring, e.Notes,
        e.Coefficients.Select(ToCoefDto).ToList());

    private static EventCoefficientDto ToCoefDto(DemandEventCoefficient c) =>
        new(c.Id, c.ScopeType, c.ScopeId, c.Coefficient, c.Source);
}

/// <summary>Default Ukrainian holiday calendar with v2-spec §4 coefficients.</summary>
internal static class DefaultHolidays
{
    internal static IEnumerable<(string Name, DateOnly Starts, DateOnly Ends,
        (string Category, decimal K)[] Coefficients)> All(int year)
    {
        yield return ("Новий рік",
            new DateOnly(year, 12, 25), new DateOnly(year, 1, 2),
            [("Алкоголь", 2.5m), ("Кондитерські", 3.0m), ("Молочні", 1.2m), ("М'ясо", 1.8m)]);

        yield return ("Великдень",
            // Movable feast — window set for the seed year; manager adjusts annually.
            new DateOnly(year, 4, 5), new DateOnly(year, 4, 13),
            [("Борошно, яйця, масло", 3.5m), ("М'ясо", 2.0m), ("Кондитерські", 2.5m)]);

        yield return ("8 березня",
            new DateOnly(year, 3, 6), new DateOnly(year, 3, 8),
            [("Кондитерські", 2.0m), ("Алкоголь", 1.5m)]);

        yield return ("Початок школи",
            new DateOnly(year, 8, 25), new DateOnly(year, 9, 10),
            [("Канцелярія", 5.0m), ("Продукти (ланч)", 1.3m)]);

        yield return ("День Незалежності",
            new DateOnly(year, 8, 23), new DateOnly(year, 8, 24),
            [("Алкоголь", 1.4m), ("М'ясо", 1.6m)]);
    }
}
