namespace ShelfGuard.Application.Features.Leads;

public sealed record LeadNotification(
    string Source, string Name, string Phone, string? Company, string? Message,
    bool IsCritical, string? CriticalReason, string? RequestType = null);

/// <summary>Pushes a saved lead to the owner. Lead capture must never depend on it succeeding.</summary>
public interface ILeadNotifier
{
    Task NotifyAsync(LeadNotification notification, CancellationToken ct);
}
