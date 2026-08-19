using System.Text.Json;

namespace ShelfGuard.Application.Features.MobileConfig;

/// <summary>See <see cref="IConsumerFeatureFlagService"/> for the full contract and the
/// production-safety default-enabled rationale.</summary>
public sealed class ConsumerFeatureFlagService : IConsumerFeatureFlagService
{
    private readonly IMobileConfigPublishedReadService _publishedRead;

    public ConsumerFeatureFlagService(IMobileConfigPublishedReadService publishedRead) =>
        _publishedRead = publishedRead;

    public async Task<bool> IsEnabledAsync(Guid tenantId, string flagKey, CancellationToken ct = default)
    {
        if (!MobileConfigWhitelists.FeatureKeys.Contains(flagKey))
        {
            throw new ArgumentException(
                $"'{flagKey}' is not a known consumer feature flag. Valid keys: " +
                string.Join(", ", MobileConfigWhitelists.FeatureKeys),
                nameof(flagKey));
        }

        // Delegates to the same published-version lookup TASK-534 already built — tenant
        // existence check, RLS-scoped read via ITenantSessionOverride, draft/archived versions
        // never leaking, defensive published-pointer validation — instead of duplicating any of
        // that here.
        var (documentJson, _, _) = await _publishedRead.GetPublishedConfigAsync(tenantId, ct);

        // PRODUCTION-SAFETY DEFAULT: no published document at all (tenant not found, no
        // MobileConfiguration row yet, or no PublishedVersionId yet) => enabled. See interface
        // remarks — this must hold until a real Publish flow (TASK-544) exists, and even then
        // only a tenant that has actually published gets to turn a flag off.
        if (documentJson is null)
            return true;

        using var doc = JsonDocument.Parse(documentJson);
        if (!doc.RootElement.TryGetProperty("features", out var featuresEl) ||
            featuresEl.ValueKind != JsonValueKind.Object)
        {
            return true;
        }

        if (!featuresEl.TryGetProperty(flagKey, out var flagEl))
            return true; // present document, but this key isn't mentioned — opt-out, not opt-in.

        // A validator-approved published document (TASK-532) always stores a boolean here;
        // treating "anything that isn't literally false" as enabled keeps this fail-open even
        // against an unexpected stored type, matching the rest of this default's posture.
        return flagEl.ValueKind != JsonValueKind.False;
    }
}
