using ShelfGuard.Application.Features.Provider.Dtos;

namespace ShelfGuard.Application.Features.Provider;

public interface IProviderStatsService
{
    Task<IReadOnlyList<ProviderMemberStatsDto>> GetTeamStatsAsync(CancellationToken ct = default);
}
