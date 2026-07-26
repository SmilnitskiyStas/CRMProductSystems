using System.Text.Json.Serialization;

namespace ShelfGuard.Domain.Constants;

/// <summary>
/// The 11 transactional RFM segments (TASK-406, marketing-analytics plan §"Фаза 1").
/// Deliberately prefixed/named "Rfm..." everywhere (enum, DTOs) — NEVER bare "Segment..." —
/// because <see cref="Entities.Item"/>'s <c>Segment</c> navigation property already means the
/// promo-cannibalization <see cref="Entities.ProductSegment"/>, an unrelated concept.
///
/// "Without purchase" (a <see cref="Entities.Customer"/> with zero lifetime transactions) is
/// deliberately NOT a member of this enum — it never enters R/F/M scoring at all and is
/// surfaced as its own separate card (see RfmOverviewDto.NoPurchase), per
/// docs/uployal/RFM_ANALYSIS.md §7/§8's "Без покупок" row living outside the 11-segment table.
///
/// Numeric order below is the classification PRIORITY order (see RfmSegmentClassifier) —
/// first matching rule wins, exactly as tabulated in the approved plan. Do not reorder these
/// members without also reordering the classifier's if-chain; they are meant to stay in sync.
///
/// [JsonConverter] here is scoped to ONLY this type (not a global JsonSerializerOptions
/// change, which would risk touching enums elsewhere in the app) — every other wire value in
/// this codebase is a plain string (Status, ManagementType, ItemType, ...), never a raw C#
/// enum ordinal, so this keeps RfmSegmentKey consistent with that convention: serializes/binds
/// as "Champions"/"Loyal"/etc, not 1/2/etc. ASP.NET Core's route/query enum model binder is
/// case-insensitive, so `GET .../segments/champions` and `.../Champions` both work.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RfmSegmentKey
{
    Champions = 1,
    Loyal = 2,
    CannotLoseThem = 3,
    AtRisk = 4,
    New = 5,
    PotentialLoyalist = 6,
    Promising = 7,
    AboutToSleep = 8,
    Hibernating = 9,
    Lost = 10,
    NeedsAttention = 11,
}
