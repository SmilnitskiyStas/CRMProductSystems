using ShelfGuard.Application.Features.MarketingAnalytics.PriceSegments.Dtos;
using ShelfGuard.Domain.Constants;

namespace ShelfGuard.Application.Features.MarketingAnalytics.PriceSegments;

/// <summary>
/// Pure C# template engine for Фаза 2 — mirrors <see cref="MarketingAnalytics.RecommendationTemplates"/>'s
/// shape (one method per key, live-KPI substitution, Тригер/Дія/Оффер/Застереження), but covers
/// THREE separate enums (<see cref="PriceAudienceKey"/> comparison audiences,
/// <see cref="PriceSegmentKey"/> all-time tiers, <see cref="FrequencyAudienceKey"/> reactivation
/// audiences) sharing the one <see cref="PriceSegmentRecommendationDto"/> wire shape (design doc
/// §7 item 11). This is the default half of the hybrid AI approach — "Пояснити детальніше"
/// additionally calls <see cref="Interfaces.IPriceSegmentAdvisor"/> (Claude) on top of this
/// template text, only on explicit user request.
///
/// Source text for each branch is analysis doc `docs/uployal/PRICE_SEGMENTS_ANALYSIS.md`
/// §7.1/§7.2/§7.3 (price audiences), §14.2/§14.3/§14.4 (all-time tiers), §17.1/§17.2/§17.3
/// (frequency) — <see cref="PriceAudienceKey.Stable"/> and <see cref="FrequencyAudienceKey.Other"/>
/// have NO competitor card to source from (analysis §7.4/§25.3, §17.5) since the competitor never
/// exposes either as a real audience; both get original, conservative "no action needed, just
/// monitor" copy consistent with this codebase's decision to give them full parity anyway
/// (design brief item 6 / TASK-420 design doc §3).
/// </summary>
public static class PriceSegmentRecommendationTemplates
{
    // ── Comparison mode: price audiences (analysis doc §7) ──────────────────────────────────

    public static PriceSegmentRecommendationDto BuildPriceAudience(PriceAudienceRecommendationInputDto i) => i.Audience switch
    {
        PriceAudienceKey.RealGrowth => new PriceSegmentRecommendationDto(
            TriggerUa:
                $"{Count(i.CustomerCount)} клієнтів ({Pct(i.SharePercentOfAnalyzed)} проаналізованих) перейшли у вищий " +
                "ціновий сегмент, і кількість товарів у чеку теж зросла — це реальний купівельний апетит, а не лише " +
                "зміна цін.",
            ActionUa: "Апсел, преміальні позиції, супутні товари, підтримка лояльності.",
            OfferUa: $"Преміальний асортимент і бандли — середній LTV цієї аудиторії вже {Money(i.AverageLtv)}, є ресурс для більшого чека.",
            CautionUa: "Не пропонувати знижку — це вже позитивна динаміка, знижка тут лише зменшить маржу без потреби."),

        PriceAudienceKey.PriceGrowth => new PriceSegmentRecommendationDto(
            TriggerUa:
                $"{Count(i.CustomerCount)} клієнтів ({Pct(i.SharePercentOfAnalyzed)} проаналізованих) перейшли у вищий " +
                "ціновий сегмент, але кількість товарів у чеку НЕ зросла — зростання чека, ймовірно, викликане цінами, " +
                "а не купівельним апетитом.",
            ActionUa: "Підтримувати ціннісну пропозицію, стимулювати додавання позицій у чек, пропонувати комплекти.",
            OfferUa: "Вигідніша друга одиниця товару або бандл — підштовхнути до реального зростання кількості позицій, не лише суми.",
            CautionUa: $"Зростання реальної лояльності не підтверджене (середній LTV {Money(i.AverageLtv)}) — це може бути суто інфляційний ефект."),

        PriceAudienceKey.Declining => new PriceSegmentRecommendationDto(
            TriggerUa:
                $"{Count(i.CustomerCount)} клієнтів ({Pct(i.SharePercentOfAnalyzed)} проаналізованих) перейшли у нижчий " +
                "ціновий сегмент — почали купувати дешевше, ніж раніше.",
            ActionUa: "Цільова знижка або бандл; першочергова робота з клієнтами найбільшого LTV.",
            OfferUa: $"Персональна пропозиція, орієнтована на повернення до попереднього сегмента (середній LTV {Money(i.AverageLtv)} — є що втрачати).",
            CautionUa: "Це ранній сигнал економії або можливого відтоку — втручання варте того, поки клієнт ще купує."),

        PriceAudienceKey.Stable => new PriceSegmentRecommendationDto(
            TriggerUa:
                $"{Count(i.CustomerCount)} клієнтів ({Pct(i.SharePercentOfAnalyzed)} проаналізованих) залишаються у " +
                "своєму звичному ціновому сегменті — без зростання чи падіння.",
            ActionUa: "Підтримувати задоволеність, не витрачати ресурс на цінові пропозиції — це найпередбачуваніша частина бази.",
            OfferUa: "Без цінової пропозиції — сегмент не сигналізує ні про ріст апетиту, ні про економію.",
            CautionUa: "Моніторити наступний період: стабільність може означати і задоволеність, і байдужість."),

        _ => throw new ArgumentOutOfRangeException(nameof(i), i.Audience, null),
    };

    // ── All-time mode: price tiers (analysis doc §14) ───────────────────────────────────────

    public static PriceSegmentRecommendationDto BuildAllTimeSegment(AllTimeSegmentRecommendationInputDto i) => i.Segment switch
    {
        PriceSegmentKey.Tier1 => new PriceSegmentRecommendationDto(
            TriggerUa:
                $"{Count(i.CustomerCount)} клієнтів у сегменті \"{i.RangeLabelUa}\" — невеликий типовий чек, середній LTV {Money(i.AverageLtv)}.",
            ActionUa: "Дешевше підняти чек наявного клієнта, ніж залучити нового.",
            OfferUa: "Допродаж на касі, комплекти, друга одиниця товару, бонус за перевищення звичної суми чека.",
            CautionUa: "Не інвестувати в персональну роботу — сегмент найкраще реагує на масові, дешеві механіки."),

        PriceSegmentKey.Tier7 => new PriceSegmentRecommendationDto(
            TriggerUa:
                $"{Count(i.CustomerCount)} клієнтів у топовому сегменті \"{i.RangeLabelUa}\" — середній LTV {Money(i.AverageLtv)}, " +
                "найвища цінність на клієнта в мережі.",
            ActionUa: "Персональні умови, ексклюзивні пропозиції, пріоритетне утримання.",
            OfferUa: "Індивідуальний менеджер або VIP-програма — витрати на утримання виправдані високим LTV.",
            CautionUa: "Додатково контролювати активність цієї аудиторії на вкладці частоти — втрата VIP-клієнта коштує найдорожче."),

        PriceSegmentKey.Tier2 or PriceSegmentKey.Tier3 or PriceSegmentKey.Tier4
            or PriceSegmentKey.Tier5 or PriceSegmentKey.Tier6 => new PriceSegmentRecommendationDto(
            TriggerUa:
                $"{Count(i.CustomerCount)} клієнтів у сегменті \"{i.RangeLabelUa}\" — середній LTV {Money(i.AverageLtv)}.",
            ActionUa: "Є потенціал перевести клієнта на наступний ціновий рівень — цільові пропозиції під конкретний сегмент.",
            OfferUa: "Бонус за перевищення звичного чека — підштовхує саме до переходу в наступний сегмент, а не до разової знижки.",
            CautionUa: "Уникати універсальної знижки для всіх середніх сегментів одразу — пропозиція має бути прив'язана до конкретного тіру."),

        _ => throw new ArgumentOutOfRangeException(nameof(i), i.Segment, null),
    };

    // ── Frequency & reactivation (analysis doc §17) ─────────────────────────────────────────

    public static PriceSegmentRecommendationDto BuildFrequencyAudience(FrequencyAudienceRecommendationInputDto i) => i.Audience switch
    {
        FrequencyAudienceKey.Sleeping => new PriceSegmentRecommendationDto(
            TriggerUa:
                $"{Count(i.CustomerCount)} клієнтів ({Pct(i.SharePercent)} бази) повністю припинили купувати — раніше " +
                $"купували в середньому {Freq(i.AverageFrequencyPrevious)} разів за період, середній LTV {Money(i.AverageLtv)}.",
            ActionUa: "Терміновий win-back; починати з клієнтів найбільшого LTV або найбільшої попередньої частоти.",
            OfferUa: "Сильний бонус на повернення — виправданий високою історичною цінністю цієї аудиторії.",
            CautionUa: "Що довше клієнт неактивний, то дорожче коштує повернення — діяти без затримки."),

        FrequencyAudienceKey.Declining => new PriceSegmentRecommendationDto(
            TriggerUa:
                $"{Count(i.CustomerCount)} клієнтів ({Pct(i.SharePercent)} бази) купують помітно рідше — частота впала з " +
                $"{Freq(i.AverageFrequencyPrevious)} до {Freq(i.AverageFrequencyCurrent)} за період, середній LTV {Money(i.AverageLtv)}.",
            ActionUa: "М'яке нагадування (Telegram/push), персональна пропозиція.",
            OfferUa: "Невеликий бонус саме на наступну покупку — недорога механіка, поки клієнт ще активний.",
            CautionUa: "Це рання стадія відтоку — дешевше втримати зараз, ніж повертати після переходу у \"Сплячі\"."),

        FrequencyAudienceKey.Growing => new PriceSegmentRecommendationDto(
            TriggerUa:
                $"{Count(i.CustomerCount)} клієнтів ({Pct(i.SharePercent)} бази) нарощують частоту покупок — з " +
                $"{Freq(i.AverageFrequencyPrevious)} до {Freq(i.AverageFrequencyCurrent)} за період.",
            ActionUa: "Закріплювати звичку: бонус за серію покупок, підписка або абонемент, ранній доступ до акцій.",
            OfferUa: "Програма лояльності з нагородою саме за регулярність, а не за суму разової покупки.",
            CautionUa: "Не переводити одразу в масову розсилку зі знижками — ризик підмінити звичку купівлею лише на акції."),

        FrequencyAudienceKey.Other => new PriceSegmentRecommendationDto(
            TriggerUa:
                $"{Count(i.CustomerCount)} клієнтів ({Pct(i.SharePercent)} бази) без вираженої динаміки частоти — " +
                "не сплять, не зростають і не падають достатньо, щоб потрапити в один зі спеціалізованих режимів.",
            ActionUa: "Стандартна комунікація в межах загальної розсилки, без спецрежиму.",
            OfferUa: "Загальна сезонна/асортиментна пропозиція — той самий канал, що й для всієї активної бази.",
            CautionUa: "Спершу подивитись, куди рухається ця аудиторія наступного періоду, перш ніж виділяти на неї окремий ресурс."),

        _ => throw new ArgumentOutOfRangeException(nameof(i), i.Audience, null),
    };

    // ── formatting helpers (same shapes as MarketingAnalytics.RecommendationTemplates) ──────

    private static string Count(int n) => n.ToString("N0");
    private static string Pct(decimal p) => p.ToString("0.#") + "%";
    private static string Money(decimal m) => m.ToString("N0") + " ₴";
    private static string Freq(decimal f) => f.ToString("0.#");
}
