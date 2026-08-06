using ShelfGuard.Application.Features.MarketingAnalytics.PostCampaign.Dtos;

namespace ShelfGuard.Application.Features.MarketingAnalytics.PostCampaign;

/// <summary>
/// Pure C# template engine (TASK-472, `docs/uployal/AUDIENCE_ANALYSIS.md` §17) — same shape as
/// Фаза 1/2's <c>RecommendationTemplates</c>/<c>PriceSegmentRecommendationTemplates</c>: pick a
/// branch from live KPIs, substitute them into fixed Ukrainian copy. This is the DEFAULT of the
/// hybrid AI approach — "Пояснити детальніше" additionally calls
/// <see cref="Domain.Interfaces.IPostCampaignAdvisor.ExplainAsync"/> (Claude) on top of this
/// template text, only on explicit user request.
///
/// Two entry points, not one, because the two source signals live in two different report tabs:
/// <see cref="BuildSummary"/> covers the brief's first two branching axes (reactivation rate,
/// turnover delta) using only the Summary tab's own already-computed KPIs; <see cref="BuildMigration"/>
/// covers the third (RFM up vs down) using the Migration tab's own already-computed up/down
/// counts. Computing the RFM migration matrix a second time just to fold its counts into the
/// Summary response would cost two extra live NTILE queries per Summary request for a KPI the
/// Migration response already carries at zero extra cost — see this task's log for the full
/// reasoning.
/// </summary>
public static class PostCampaignRecommendationTemplates
{
    // Named thresholds — the source doc explicitly states the competitor's own cutoff between
    // "successful" and "weak" reactivation is not disclosed (§17.3: "точний поріг... не
    // розкритий"). These are reasonable, documented v1 defaults per this task's brief, which
    // defers exactly this kind of undisclosed threshold to a product decision.
    public const decimal HighReactivationRateThresholdPercent = 30m;

    public static PostCampaignRecommendationDto BuildSummary(PostCampaignSummaryRecommendationInputDto i)
    {
        // Priority order mirrors RfmSegmentClassifier's own "first matching rule wins" discipline.

        // #1 Successful reactivation — the strongest positive signal (source doc §17.2).
        if (i.Reactivated > 0 && i.ReactivationRatePercent is { } rr && rr >= HighReactivationRateThresholdPercent)
            return new PostCampaignRecommendationDto(
                TriggerUa:
                    $"Реактивовано {Count(i.Reactivated)} раніше неактивних клієнтів — {Pct(rr)} від усіх, хто не " +
                    $"купував до кампанії. Сегмент приніс {Money(i.MoneyAfter)} після запуску.",
                ActionUa:
                    "Реактивація спрацювала — масштабуйте цей самий сценарій (канал, оффер, час відправлення) на " +
                    "схожі \"сплячі\" сегменти клієнтів, поки оффер ще актуальний.",
                OfferUa:
                    "Закріпіть повернення: невеликий бонус на НАСТУПНУ покупку цих клієнтів, а не одноразова знижка.",
                CautionUa:
                    "Це порівняння до/після без контрольної групи — частина ефекту могла статись і без кампанії. " +
                    "Не робіть висновок про точний ROI без окремого A/B тесту.");

        // #2 Turnover dropped — a real warning, ranked above the "weak reactivation" default.
        if (i.MoneyDeltaPercent is { } deltaDown && deltaDown < 0m)
            return new PostCampaignRecommendationDto(
                TriggerUa:
                    $"Оборот сегмента впав на {Pct(Math.Abs(deltaDown))} проти періоду до кампанії " +
                    $"({Money(i.MoneyAfter)} після). {Count(i.Dropped)} клієнтів перестали купувати.",
                ActionUa:
                    "Мета кампанії не досягнута за грошовим показником — перегляньте механіку, канал і оффер перед " +
                    "повторним запуском на цю ж аудиторію.",
                OfferUa: "Розгляньте сильніший, більш персоналізований стимул для наступної спроби.",
                CautionUa:
                    "Падіння може частково пояснюватись сезонністю чи зовнішніми факторами, а не лише кампанією — " +
                    "порівняйте із загальною динамікою тенанта за той самий період.");

        // #3 Weak/no reactivation opportunity, but revenue held or grew — still a real,
        // positive contribution to call out (source doc §17.1).
        if (i.MoneyAfter > 0m)
            return new PostCampaignRecommendationDto(
                TriggerUa:
                    $"Сегмент приніс {Money(i.MoneyAfter)} після кампанії від {Count(i.BuyersAfter)} покупців" +
                    (i.ReactivationRatePercent is { } weakRr
                        ? $"; реактивація — лише {Pct(weakRr)} від неактивних до кампанії."
                        : "; усі покупці сегмента вже були активні до кампанії."),
                ActionUa:
                    i.ReactivationRatePercent is not null
                        ? "Реактивація слабка — спробуйте інший канал комунікації, інший оффер або сильніший " +
                          "стимул у наступній спробі на цю ж \"сплячу\" аудиторію."
                        : "Утримання вже активних клієнтів — не привід зупинятись: заплануйте наступний контакт, " +
                          "поки інтерес свіжий.",
                OfferUa: "Персональна пропозиція для клієнтів, що ще не повернулись, замість повторної масової розсилки.",
                CautionUa:
                    "Це порівняння до/після без контрольної групи — не стверджуйте точний причинний ефект " +
                    "кампанії без окремого A/B тесту.");

        // #4 Catch-all — zero revenue, zero reactivation (e.g. an all-unknown-ID segment or a
        // genuinely inactive audience).
        return new PostCampaignRecommendationDto(
            TriggerUa:
                $"Сегмент ({Count(i.MatchedCount)} клієнтів) не показав жодної активності після кампанії — " +
                "нуль покупців, нуль обороту.",
            ActionUa:
                "Перевірте якість самого списку ID (можливо, частина клієнтів невідома системі) і чи дійсно " +
                "кампанія дійшла до цієї аудиторії.",
            OfferUa: "Не інвестуйте у повторний запуск на цю ж аудиторію без перевірки причини нульового результату.",
            CautionUa: "Нульовий результат може означати як провал кампанії, так і проблему з самим списком ID.");
    }

    public static PostCampaignRecommendationDto BuildMigration(PostCampaignMigrationRecommendationInputDto i)
    {
        if (i.UpCount > i.DownCount && i.UpCount > 0)
            return new PostCampaignRecommendationDto(
                TriggerUa:
                    $"{Count(i.UpCount)} клієнтів піднялись у кращий RFM-сегмент після кампанії " +
                    $"(проти {Count(i.DownCount)}, що знизились).",
                ActionUa: "Запропонуйте цим клієнтам наступний рівень програми лояльності, щоб закріпити нову поведінку.",
                OfferUa: "Персональне визнання нового статусу (бейдж, привітання) замість чергової знижки.",
                CautionUa:
                    "RFM-сегмент відображає поведінку в конкретному вікні — перевірте стійкість через наступний період.");

        if (i.DownCount > i.UpCount && i.DownCount > 0)
            return new PostCampaignRecommendationDto(
                TriggerUa:
                    $"{Count(i.DownCount)} клієнтів знизились у гірший RFM-сегмент після кампанії " +
                    $"(проти {Count(i.UpCount)}, що піднялись) — сегмент радше згасає.",
                ActionUa: "Перегляньте оффер і таймінг комунікації для цієї аудиторії перед наступною кампанією.",
                OfferUa: "Цільовий win-back для клієнтів, що знизились до \"Під ризиком\"/\"Сплячі\".",
                CautionUa: "Зниження може бути частиною ширшого тренду тенанта, а не наслідком саме цієї кампанії.");

        return new PostCampaignRecommendationDto(
            TriggerUa:
                $"RFM-склад сегмента практично не змінився: {Count(i.StableCount)} без змін, {Count(i.UpCount)} " +
                $"угору, {Count(i.DownCount)} вниз.",
            ActionUa: "Кампанія не змінила якісний профіль сегмента — оцінюйте ефект переважно за оборотом і реактивацією.",
            OfferUa: "Стандартна комунікація для збереження поточного рівня активності.",
            CautionUa: "Стабільний RFM-розподіл не означає відсутність ефекту — перевірте грошові показники окремо.");
    }

    private static string Count(int n) => n.ToString("N0");
    private static string Pct(decimal p) => p.ToString("0.#") + "%";
    private static string Money(decimal m) => m.ToString("N0") + " ₴";
}
