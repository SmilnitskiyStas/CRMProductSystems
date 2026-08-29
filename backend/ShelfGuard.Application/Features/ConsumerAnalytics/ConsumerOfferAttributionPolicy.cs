namespace ShelfGuard.Application.Features.ConsumerAnalytics;

public static class ConsumerOfferAttributionPolicy
{
    public const string ModelVersion = "loyalty-pos-v1";
    public const string Confidence = "probable";

    public static ConsumerOfferAttributionPolicyDto Describe() => new(
        ModelVersion,
        Confidence,
        "Ідентифікований POS-чек із товаром пропозиції",
        [
            "У чеку вказана картка програми лояльності",
            "Товар входить до каталогу або акції",
            "Покупка відбулася у призначеному магазині в період дії",
            "POS-транзакцію не скасовано",
        ],
        "POS-рядок не зберігає ідентифікатор застосованої кампанії, тому результат означає використання пропозиції з високою ймовірністю, а не технічне підтвердження конкретної знижки."
    );
}

public sealed record ConsumerOfferAttributionPolicyDto(
    string ModelVersion,
    string Confidence,
    string Name,
    IReadOnlyList<string> Rules,
    string Limitation);
