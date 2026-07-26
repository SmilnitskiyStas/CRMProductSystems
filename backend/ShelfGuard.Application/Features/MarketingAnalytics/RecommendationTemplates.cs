using ShelfGuard.Application.Features.MarketingAnalytics.Dtos;
using ShelfGuard.Domain.Constants;

namespace ShelfGuard.Application.Features.MarketingAnalytics;

/// <summary>
/// Pure C# template engine — one method per <see cref="RfmSegmentKey"/> — generating the
/// five-block recommendation structure (Тригер/Дія/Оффер/Застереження/Товари) confirmed live
/// on the competitor (docs/uployal/RFM_ANALYSIS.md §14), substituting the segment's actual
/// live KPIs. This is the DEFAULT of the hybrid AI approach (plan §"Рекомендації — гібридний
/// рушій") — "Пояснити детальніше" additionally calls <c>IMarketingAdvisor.ExplainAsync</c>
/// (Claude) on top of this template text, only on explicit user request.
/// </summary>
public static class RecommendationTemplates
{
    public static RfmRecommendationDto Build(RfmSegmentKey key, RfmRecommendationInputDto input) => key switch
    {
        RfmSegmentKey.Champions => Champions(input),
        RfmSegmentKey.Loyal => Loyal(input),
        RfmSegmentKey.CannotLoseThem => CannotLoseThem(input),
        RfmSegmentKey.AtRisk => AtRisk(input),
        RfmSegmentKey.New => New(input),
        RfmSegmentKey.PotentialLoyalist => PotentialLoyalist(input),
        RfmSegmentKey.Promising => Promising(input),
        RfmSegmentKey.AboutToSleep => AboutToSleep(input),
        RfmSegmentKey.Hibernating => Hibernating(input),
        RfmSegmentKey.Lost => Lost(input),
        RfmSegmentKey.NeedsAttention => NeedsAttention(input),
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
    };

    // ── #1 Champions — RFM_ANALYSIS.md §14.1 is the direct source for this one ──────────────

    private static RfmRecommendationDto Champions(RfmRecommendationInputDto i) => new(
        TriggerUa:
            $"Близько {Count(i.CustomerCount)} осіб — {Pct(i.SharePercentOfPeriodCustomers)} покупців періоду, але " +
            $"{Pct(i.SharePercentOfPeriodRevenue)} обороту. Середня давність останньої покупки — лише " +
            $"{Days(i.AverageRecencyDays)}. Основний ризик — зайві знижки тим, хто й так активно купує.",
        ActionUa:
            $"Утримання цінністю, а не ціною: ранній доступ до новинок, статус і визнання, реферальна " +
            $"програма. Найкращий контакт — {DayHourUa(i)}.",
        OfferUa:
            $"Без знижки. Допродаж до \"{Anchor(i)}\" через афінність — інвестиція в досвід виправдана " +
            $"високим LTV ({Money(i.AverageLtv)} у середньому).",
        CautionUa:
            "Не девальвувати сегмент знижками. Зростання давності — ранній сигнал відтоку VIP-ядра.",
        ProductsForPromo: i.TopProductNames);

    // ── #2 Loyal ─────────────────────────────────────────────────────────────

    private static RfmRecommendationDto Loyal(RfmRecommendationInputDto i) => new(
        TriggerUa:
            $"{Count(i.CustomerCount)} стабільних клієнтів ({Pct(i.SharePercentOfPeriodCustomers)} покупців, " +
            $"{Pct(i.SharePercentOfPeriodRevenue)} обороту) з регулярним ритмом покупок і давністю {Days(i.AverageRecencyDays)}.",
        ActionUa:
            $"Утримати ритм: програма лояльності/бонуси за регулярність, персональні нагадування напередодні " +
            $"звичного дня візиту ({DayHourUa(i)}).",
        OfferUa:
            $"Невелика, персональна винагорода за наступну покупку (не масова знижка) — з допродажем " +
            $"\"{Anchor(i)}\".",
        CautionUa:
            "Не переводити в масову розсилку — це вже стабільне ядро, а не аудиторія реактивації.",
        ProductsForPromo: i.TopProductNames);

    // ── #3 CannotLoseThem — RFM_ANALYSIS.md §14.2 is the direct source for this one ─────────

    private static RfmRecommendationDto CannotLoseThem(RfmRecommendationInputDto i) => new(
        TriggerUa:
            $"{Count(i.CustomerCount)} колишніх VIP ({Pct(i.SharePercentOfPeriodRevenue)} обороту), середня " +
            $"давність {Days(i.AverageRecencyDays)}, середній LTV {Money(i.AverageLtv)} — найцінніша аудиторія " +
            "для повернення.",
        ActionUa: "Пріоритет №1 для ручної роботи: персональний контакт або дзвінок менеджера, не масова розсилка.",
        OfferUa:
            $"Ексклюзивна умова + з'ясування причини відтоку. Товар-якір — \"{Anchor(i)}\"" +
            (i.TopProductCoveragePercent is { } cov ? $" ({Pct(cov)} сегмента)" : string.Empty) + ".",
        CautionUa:
            "Не використовувати масову розсилку — висока цінність клієнтів виправдовує індивідуальну роботу.",
        ProductsForPromo: i.TopProductNames);

    // ── #4 AtRisk ────────────────────────────────────────────────────────────

    private static RfmRecommendationDto AtRisk(RfmRecommendationInputDto i) => new(
        TriggerUa:
            $"{Count(i.CustomerCount)} раніше лояльних клієнтів ({Pct(i.SharePercentOfPeriodRevenue)} обороту) " +
            $"почали зникати — давність уже {Days(i.AverageRecencyDays)}, LTV {Money(i.AverageLtv)}.",
        ActionUa:
            $"Win-back кампанія до того, як вони перейдуть у \"Не можна втратити\": персоналізований лист/дзвінок, " +
            $"нагадування про \"{Anchor(i)}\".",
        OfferUa: "Помірна, часово обмежена пропозиція — досить, щоб повернути візит, без масової знижки.",
        CautionUa: "Реагувати зараз — вікно для дешевшого повернення звужується з кожним тижнем без візиту.",
        ProductsForPromo: i.TopProductNames);

    // ── #5 New ───────────────────────────────────────────────────────────────

    private static RfmRecommendationDto New(RfmRecommendationInputDto i) => new(
        TriggerUa:
            $"{Count(i.CustomerCount)} клієнтів з першою покупкою за останні " +
            $"{RfmSegmentClassifier.NewMaxDaysSinceFirstPurchase} днів ({Pct(i.SharePercentOfPeriodCustomers)} покупців).",
        ActionUa:
            "Другий контакт вирішує все: onboarding-серія (вітання, як користуватись програмою лояльності, " +
            "нагадування про повторний візит).",
        OfferUa: $"Невеликий бонус саме на другу покупку, з допродажем \"{Anchor(i)}\" — товар, з яким уже знайомі.",
        CautionUa: "Без агресивних знижок з першого дня — це формує очікування постійних знижок, а не лояльність.",
        ProductsForPromo: i.TopProductNames);

    // ── #6 PotentialLoyalist ─────────────────────────────────────────────────

    private static RfmRecommendationDto PotentialLoyalist(RfmRecommendationInputDto i) => new(
        TriggerUa:
            $"{Count(i.CustomerCount)} кандидатів у стабільне ядро — купують недавно й непогано, " +
            $"але ще нерегулярно ({Pct(i.SharePercentOfPeriodCustomers)} покупців).",
        ActionUa:
            $"Підштовхнути до регулярності: запрошення в програму лояльності, нагадування про " +
            $"\"{Anchor(i)}\" у звичний час ({DayHourUa(i)}).",
        OfferUa: "Бонус за третій/четвертий візит поспіль (частота, не разова знижка).",
        CautionUa: "Разова глибока знижка тут ризикує закріпити \"купую лише на акції\" замість регулярності.",
        ProductsForPromo: i.TopProductNames);

    // ── #7 Promising ─────────────────────────────────────────────────────────

    private static RfmRecommendationDto Promising(RfmRecommendationInputDto i) => new(
        TriggerUa:
            $"{Count(i.CustomerCount)} клієнтів купують недавно, але рідко й на невелику суму " +
            $"({Pct(i.SharePercentOfPeriodRevenue)} обороту).",
        ActionUa: $"Збільшити частоту та середній чек: крос-сел до \"{Anchor(i)}\", нагадування про повторний візит.",
        OfferUa: "Невелика знижка на другий товар у чеку (бандл), а не на весь чек.",
        CautionUa: "Не витрачати ресурс на масштабну персональну роботу — сегмент ще занадто малоцінний для цього.",
        ProductsForPromo: i.TopProductNames);

    // ── #8 AboutToSleep ──────────────────────────────────────────────────────

    private static RfmRecommendationDto AboutToSleep(RfmRecommendationInputDto i) => new(
        TriggerUa:
            $"{Count(i.CustomerCount)} клієнтів знижують частоту покупок — ранній сигнал, ще до " +
            "переходу в \"Сплячі\".",
        ActionUa: $"Легке нагадування (Telegram/push) з допродажем \"{Anchor(i)}\" — поки що дешевше за win-back.",
        OfferUa: "Помірний бонус на повернення протягом найближчих 1-2 тижнів.",
        CautionUa: "Зволікання переводить сегмент у \"Сплячі\", де повернення коштуватиме значно дорожче.",
        ProductsForPromo: i.TopProductNames);

    // ── #9 Hibernating ───────────────────────────────────────────────────────

    private static RfmRecommendationDto Hibernating(RfmRecommendationInputDto i) => new(
        TriggerUa:
            $"{Count(i.CustomerCount)} давно неактивних клієнтів ({Pct(i.SharePercentOfPeriodRevenue)} обороту) " +
            "з низькою поточною ймовірністю покупки.",
        ActionUa: "Дешевий масовий канал (email/Telegram), не персональний контакт — цінність поки не виправдовує його.",
        OfferUa: $"Помітна \"ми скучили\" пропозиція з нагадуванням про \"{Anchor(i)}\".",
        CautionUa: "Не інвестувати в персональну роботу — низький очікуваний повернення на витрачений час менеджера.",
        ProductsForPromo: i.TopProductNames);

    // ── #10 Lost ─────────────────────────────────────────────────────────────

    private static RfmRecommendationDto Lost(RfmRecommendationInputDto i) => new(
        TriggerUa:
            $"{Count(i.CustomerCount)} клієнтів з рівно однією покупкою за все життя, більше " +
            $"{RfmSegmentClassifier.LostMinMonthsSinceOnlyPurchase} місяців тому.",
        ActionUa: "Найдешевший можливий канал (масова розсилка) або взагалі виключити з активних кампаній.",
        OfferUa: "Разова \"повернись\" пропозиція без персоналізації — витрати на контакт мають бути мінімальні.",
        CautionUa: "Не інвестувати в цей сегмент понад мінімум — один одноразовий чек не підтверджує реальний інтерес.",
        ProductsForPromo: i.TopProductNames);

    // ── #11 NeedsAttention ───────────────────────────────────────────────────

    private static RfmRecommendationDto NeedsAttention(RfmRecommendationInputDto i) => new(
        TriggerUa:
            $"{Count(i.CustomerCount)} клієнтів із середніми показниками в усьому — без вираженої сили чи " +
            "критичного ризику.",
        ActionUa: $"Стандартна, недорога комунікація: інформаційна розсилка з \"{Anchor(i)}\", без спецрежиму.",
        OfferUa: "Загальна сезонна/асортиментна пропозиція — той самий канал, що й для всієї бази.",
        CautionUa: "Не витрачати на сегмент індивідуальний ресурс — спершу подивитись, куди він рухається " +
                   "за наступний період (до \"Потенційно лояльних\" чи до \"Засинають\").",
        ProductsForPromo: i.TopProductNames);

    // ── formatting helpers ───────────────────────────────────────────────────

    private static string Count(int n) => n.ToString("N0");
    private static string Pct(decimal p) => p.ToString("0.#") + "%";
    private static string Money(decimal m) => m.ToString("N0") + " ₴";
    private static string Days(decimal d) => $"{d.ToString("0.#")} дн.";

    private static string Anchor(RfmRecommendationInputDto i) =>
        i.TopProductName ?? "топ-товар сегмента";

    private static string DayHourUa(RfmRecommendationInputDto i)
    {
        var day = i.PeakDayOfWeekIso switch
        {
            1 => "понеділок", 2 => "вівторок", 3 => "середа", 4 => "четвер",
            5 => "п'ятниця", 6 => "субота", 7 => "неділя",
            _ => null,
        };
        var hour = i.PeakHour is { } h ? $"{h:00}:00–{(h + 1) % 24:00}:00" : null;

        return (day, hour) switch
        {
            (not null, not null) => $"{day}, {hour}",
            (not null, null) => day,
            (null, not null) => hour!,
            _ => "у звичний для сегмента час",
        };
    }
}
