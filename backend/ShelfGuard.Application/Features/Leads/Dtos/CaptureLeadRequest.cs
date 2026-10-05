namespace ShelfGuard.Application.Features.Leads.Dtos;

/// <summary>
/// Public landing-page lead form (TASK-333, extended TASK-721). All fields nullable so that model
/// binding never short-circuits with a ProblemDetails 400 — validation lives in the service
/// and returns the project-wide { "error": "..." } contract.
/// "Website" is a honeypot: hidden on the real form, so any non-empty value means a bot.
/// Attribution fields are optional; over-long values are truncated, not rejected (a lead is never
/// dropped because of tracking metadata).
/// </summary>
public sealed record CaptureLeadRequest(
    string? Name,
    string? Phone,
    string? Company,
    string? Message,
    string? Website,
    string? Source = null,
    string? PageUrl = null,
    string? Locale = null,
    string? Referrer = null,
    string? UtmSource = null,
    string? UtmMedium = null,
    string? UtmCampaign = null);
