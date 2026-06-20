namespace ShelfGuard.Application.Features.Provider.Dtos;

public record ProviderTeamMemberDto(
    Guid     Id,
    string   Email,
    string   FullName,
    string   Role,
    bool     IsActive,
    DateTime CreatedAt
);

public record InviteProviderMemberRequest(
    string Email,
    string FullName,
    string Role
);

public record UpdateProviderMemberRequest(
    string FullName,
    string Role
);
