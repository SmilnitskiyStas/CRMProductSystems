namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Фаза 4 marketing-analytics plan (`deep-cooking-nygaard.md` §"Фази 2-4", TASK-471) —
/// post-campaign audience analysis. A marketer uploads an arbitrary list of customer
/// identifiers (Customer.Id GUIDs or phone numbers) sourced from *outside* this system (an
/// SMS blast, a raffle list, an export from Фаза 3's AudienceBuilder) and the system compares
/// their behavior across equal before/after date windows around a campaign.
///
/// Unlike Фаза 1-3 (fully stateless — every dashboard number computed live from
/// pos_transactions/items/customers on every request, nothing persisted), Фаза 4 must persist
/// the uploaded list, its import-validation results, and the frozen date windows. Per the
/// source doc (`docs/uployal/AUDIENCE_ANALYSIS.md` §7, "Чернетка та застосований сегмент"),
/// "draft" (what's currently typed/uploaded) and "analyzed" (what the current report is based
/// on) are two distinct, explicitly-tracked states — re-typing/re-uploading must NOT silently
/// invalidate an already-computed report until the user explicitly re-runs analysis.
///
/// <see cref="AfterStart"/>/<see cref="AfterEnd"/>/<see cref="BeforeStart"/>/
/// <see cref="BeforeEnd"/> being null together with a null <see cref="AnalyzedAt"/> IS the
/// "draft" state (imported but not yet analyzed) — non-null means "frozen, computed snapshot".
/// Every report tab (summary/daily-turnover/rfm-activity/customers/migration) is a separate GET
/// against this same already-resolved segment; they must not each re-receive/re-validate the
/// full uploaded id list. Changing just the date window on an already-analyzed segment re-runs
/// computation without re-uploading — <see cref="SegmentHash"/> is recomputed whenever the
/// analyzed window changes, same purpose/shape as Фаза 1's FiltersHash.
///
/// Staff-only marketing tool — no consumer JWT ever touches this table, same posture as
/// <see cref="PriceSegmentSettings"/> (TASK-419): canonical RLS triad only, no
/// consumer_self_access (see AddPostCampaignSegmentSchema migration for RLS detail).
/// </summary>
public sealed class PostCampaignSegment
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }

    /// <summary>Staff user who uploaded/owns this segment. Restrict — mirrors UserPermissionGrant.GrantedByUserId.</summary>
    public Guid CreatedByUserId { get; init; }

    /// <summary>Optional marketer-given label for the segment (e.g. "SMS блиц — серпень").</summary>
    public string? Name { get; set; }

    // ── Import/validation results, set once at upload time ──────────────────
    /// <summary>Raw count of tokens submitted (list rows or file rows), before dedup/validation.</summary>
    public int UploadedCount { get; set; }
    /// <summary>Tokens that resolved to a real, existing Customer.Id in this tenant.</summary>
    public int MatchedCount { get; set; }
    /// <summary>Tokens that were exact repeats of an already-seen valid token.</summary>
    public int DuplicateCount { get; set; }
    /// <summary>Well-formed tokens (valid GUID/phone shape) that do not match any customer in this tenant.</summary>
    public int UnknownCount { get; set; }
    /// <summary>Tokens that failed strict format validation entirely (never digits extracted from arbitrary text).</summary>
    public int InvalidCount { get; set; }

    /// <summary>
    /// First ~20 unmatched tokens, capped, for the on-screen/downloadable error report — per
    /// the source doc's own UX precedent of surfacing "перші 20 помилок".
    /// </summary>
    public List<string> UnknownTokensSample { get; set; } = [];
    /// <summary>First ~20 tokens that failed strict format validation, capped, same UX precedent.</summary>
    public List<string> InvalidTokensSample { get; set; } = [];

    // ── Analysis window — null = draft, non-null = frozen/analyzed snapshot ─
    public DateOnly? AfterStart { get; set; }
    public DateOnly? AfterEnd { get; set; }
    public DateOnly? BeforeStart { get; set; }
    public DateOnly? BeforeEnd { get; set; }

    /// <summary>Versioning token, recomputed whenever the analyzed window changes — same purpose/shape as Фаза 1's FiltersHash.</summary>
    public string SegmentHash { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    /// <summary>Null while draft; set the moment "Аналізувати сегмент" successfully runs.</summary>
    public DateTimeOffset? AnalyzedAt { get; set; }

    public Tenant? Tenant { get; init; }
    public User? CreatedByUser { get; init; }
    public ICollection<PostCampaignSegmentMember> Members { get; init; } = new List<PostCampaignSegmentMember>();
}
