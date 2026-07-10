namespace ShelfGuard.Application.Features.Leads.Dtos;

/// <summary>
/// Public landing-page lead form (TASK-333). All fields nullable so that model binding
/// never short-circuits with a ProblemDetails 400 — validation lives in the service
/// and returns the project-wide { "error": "..." } contract.
/// "Website" is a honeypot: hidden on the real form, so any non-empty value means a bot.
/// </summary>
public sealed record CaptureLeadRequest(
    string? Name,
    string? Phone,
    string? Company,
    string? Message,
    string? Website);
