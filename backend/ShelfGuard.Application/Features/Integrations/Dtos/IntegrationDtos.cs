using System.Text.Json.Nodes;

namespace ShelfGuard.Application.Features.Integrations.Dtos;

/// <summary>Full integration config returned to admin callers. Config is returned as raw JSON object.</summary>
public sealed record IntegrationConfigDto(
    Guid        Id,
    string      Service,
    JsonObject? Config,
    bool        IsEnabled,
    DateTime    UpdatedAt
);

/// <summary>Summary list item — config is omitted to avoid leaking credentials.</summary>
public sealed record IntegrationSummaryDto(
    string   Service,
    bool     IsEnabled,
    DateTime UpdatedAt
);

/// <summary>Request body for creating or updating an integration.</summary>
public sealed record UpsertIntegrationRequest(
    /// <summary>Service-specific settings. Shape depends on the service.</summary>
    JsonObject Config,
    bool IsEnabled = true
);
