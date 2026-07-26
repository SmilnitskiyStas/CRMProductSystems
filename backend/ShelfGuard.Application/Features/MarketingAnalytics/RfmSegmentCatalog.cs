using ShelfGuard.Domain.Constants;

namespace ShelfGuard.Application.Features.MarketingAnalytics;

/// <summary>
/// Ukrainian display copy for each <see cref="RfmSegmentKey"/> — labels/short descriptions
/// copied verbatim from docs/uployal/RFM_ANALYSIS.md §7 (the competitor's own segment table),
/// not re-authored, so the UI matches the reference product's business language.
/// </summary>
public static class RfmSegmentCatalog
{
    public static string LabelUa(RfmSegmentKey key) => key switch
    {
        RfmSegmentKey.Champions => "Чемпіони",
        RfmSegmentKey.Loyal => "Лояльні",
        RfmSegmentKey.CannotLoseThem => "Не можна втратити",
        RfmSegmentKey.AtRisk => "Під ризиком",
        RfmSegmentKey.New => "Нові",
        RfmSegmentKey.PotentialLoyalist => "Потенційно лояльні",
        RfmSegmentKey.Promising => "Перспективні",
        RfmSegmentKey.AboutToSleep => "Засинають",
        RfmSegmentKey.Hibernating => "Сплячі",
        RfmSegmentKey.Lost => "Втрачені",
        RfmSegmentKey.NeedsAttention => "Потребують уваги",
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
    };

    /// <summary>Short "чому цей сегмент" interpretation — RFM_ANALYSIS.md §7's "Бізнес-сенс" column.</summary>
    public static string ShortDescriptionUa(RfmSegmentKey key) => key switch
    {
        RfmSegmentKey.Champions => "Найактивніші, найчастіші та найцінніші покупці",
        RfmSegmentKey.Loyal => "Регулярні клієнти з високою стабільністю",
        RfmSegmentKey.CannotLoseThem => "Раніше найцінніші клієнти з ознаками відтоку",
        RfmSegmentKey.AtRisk => "Раніше лояльні клієнти, які давно не купували",
        RfmSegmentKey.New => "Нещодавно залучені клієнти",
        RfmSegmentKey.PotentialLoyalist => "Клієнти, яких можна перевести в стабільне ядро",
        RfmSegmentKey.Promising => "Купують недавно, але недостатньо часто",
        RfmSegmentKey.AboutToSleep => "Ознаки раннього зниження активності",
        RfmSegmentKey.Hibernating => "Давня неактивна база з низькою поточною ймовірністю покупки",
        RfmSegmentKey.Lost => "Одноразові давні покупці",
        RfmSegmentKey.NeedsAttention => "Середні показники без вираженої сили або критичного ризику",
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
    };

    public const string NoPurchaseLabelUa = "Без покупок";
    public const string NoPurchaseShortDescriptionUa = "Зареєстровані, але не конвертовані; окремо від RFM";

    /// <summary>All 11 keys in classification-priority order — used to guarantee the overview
    /// grid always returns exactly 11 cards, including zero-count segments (QA checklist:
    /// "порожній сегмент не ламає сторінку").</summary>
    public static readonly IReadOnlyList<RfmSegmentKey> AllKeysInPriorityOrder =
    [
        RfmSegmentKey.Champions, RfmSegmentKey.Loyal, RfmSegmentKey.CannotLoseThem, RfmSegmentKey.AtRisk,
        RfmSegmentKey.New, RfmSegmentKey.PotentialLoyalist, RfmSegmentKey.Promising, RfmSegmentKey.AboutToSleep,
        RfmSegmentKey.Hibernating, RfmSegmentKey.Lost, RfmSegmentKey.NeedsAttention,
    ];
}
