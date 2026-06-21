namespace ShelfGuard.Application.Features.Provider.Dtos;

public record ProviderTeamMemberDto(
    Guid     Id,
    string   Email,
    string   FullName,
    string   Role,
    bool     IsActive,
    DateTime CreatedAt,
    Guid?    ProviderRoleId,
    Dictionary<string, bool>? PermissionsOverride
);

public record InviteProviderMemberRequest(
    string   Email,
    string   FullName,
    string   Role,
    string?  Password              = null,
    Guid?    ProviderRoleId        = null,
    Dictionary<string, bool>? PermissionsOverride = null
);

public record UpdateProviderMemberRequest(
    string   FullName,
    string   Role,
    Guid?    ProviderRoleId        = null,
    Dictionary<string, bool>? PermissionsOverride = null
);

public record ProviderMemberStatsDto(
    Guid   MemberId,
    string FullName,
    string Role,
    bool   IsActive,
    int    TicketsAssigned,
    int    TicketsResolved,
    int    TicketsCreatedByProvider,
    int    CommentsWritten,
    double? AvgResolutionHours
);
