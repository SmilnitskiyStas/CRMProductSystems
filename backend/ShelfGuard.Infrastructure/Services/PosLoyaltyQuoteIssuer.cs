using System.Text.Json;
using ShelfGuard.Application.Features.Pos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Services;

public sealed class PosLoyaltyQuoteIssuer(ICashRegisterRepository registers, ILoyaltyRepository loyalty,
    IPosLoyaltySnapshots snapshots, TimeProvider clock) : IPosLoyaltyQuoteIssuer
{
    public async Task<PosLoyaltyGrant?> ForResolvedAsync(Guid tenant, Guid register, Guid member, CancellationToken ct)
    {
        var r = await registers.GetByIdAsync(register, ct);
        if (r is null || r.TenantId != tenant || r.Status != CashRegister.StatusActive) return null;
        var m = await loyalty.GetMembershipByIdAsync(member, tenant, ct);
        if (m is null || m.TenantId != tenant || m.Status != LoyaltyMembershipStatus.Active) return null;
        if (!await loyalty.IsCheckoutProgramAvailableAsync(tenant, ct)) return null;
        var settings = await loyalty.GetSettingsAsync(tenant, ct);
        try
        {
            var terms = PosLoyaltyTerms.From(m, settings ?? new LoyaltyProgramSettings { TenantId = tenant }, settings is null);
            var issued = snapshots.Issue(tenant, r.StoreId, register, member, m.CustomerId, terms, clock.GetUtcNow().UtcDateTime);
            return issued is null ? null : new(issued.Token, issued.ExpiresAt, terms.TierId, m.CurrentTier?.Name,
                terms.Enabled ? terms.TierDiscountPercent : 0, terms.Enabled, m.CustomerId);
        }
        catch (JsonException) { return null; } // Invalid configuration never produces a partially certified quote.
    }
}
