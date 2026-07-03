namespace ShelfGuard.Domain.Constants;

/// <summary>
/// Field type for a category-specific SupplierItem attribute (ADR-017 §4).
/// </summary>
public enum SupplierItemFieldType
{
    Text,
    Number,
    Date,
    Bool,
    Select,
}

/// <summary>
/// Definition of a single category-specific attribute field.
/// </summary>
public sealed record SupplierItemCategoryField(
    string Key,
    string LabelUa,
    SupplierItemFieldType Type,
    bool Required,
    IReadOnlyList<string>? Options = null);

/// <summary>
/// Definition of a fixed SupplierItem category and its attribute fields.
/// </summary>
public sealed record SupplierItemCategoryDefinition(
    string Key,
    string LabelUa,
    IReadOnlyList<SupplierItemCategoryField> Fields);

/// <summary>
/// Backend source-of-truth registry of SupplierItem categories and their
/// category-specific attribute fields (ADR-017 §4). The frontend renders the
/// item form dynamically from GET /api/marketplace/item-categories instead of
/// hardcoding this list, so this registry — and the Validate() method below —
/// is the only place required-field rules live.
/// </summary>
public static class SupplierItemCategories
{
    public const string Food         = "food";
    public const string AutoParts    = "auto_parts";
    public const string Medical      = "medical";
    public const string Construction = "construction";

    public static readonly IReadOnlyList<SupplierItemCategoryDefinition> All = new[]
    {
        new SupplierItemCategoryDefinition(
            Food, "Продукти харчування",
            new[]
            {
                new SupplierItemCategoryField("weight_volume", "Вага/об'єм", SupplierItemFieldType.Text, Required: true),
                new SupplierItemCategoryField("expiry_date", "Термін придатності", SupplierItemFieldType.Date, Required: true),
                new SupplierItemCategoryField("batch_number", "Номер партії", SupplierItemFieldType.Text, Required: false),
            }),
        new SupplierItemCategoryDefinition(
            AutoParts, "Автозапчастини",
            new[]
            {
                new SupplierItemCategoryField("oem_number", "OEM-номер", SupplierItemFieldType.Text, Required: true),
                new SupplierItemCategoryField("compatible_models", "Сумісні моделі", SupplierItemFieldType.Text, Required: false),
                new SupplierItemCategoryField("part_number", "Номер деталі", SupplierItemFieldType.Text, Required: false),
            }),
        new SupplierItemCategoryDefinition(
            Medical, "Медикаменти/медтовари",
            new[]
            {
                new SupplierItemCategoryField("dosage", "Дозування", SupplierItemFieldType.Text, Required: true),
                new SupplierItemCategoryField("expiry_date", "Термін придатності", SupplierItemFieldType.Date, Required: true),
                new SupplierItemCategoryField("prescription_status", "Рецептурний статус", SupplierItemFieldType.Select, Required: true,
                    Options: new[] { "ОТС", "рецептурний" }),
                new SupplierItemCategoryField("storage_conditions", "Умови зберігання", SupplierItemFieldType.Text, Required: false),
            }),
        new SupplierItemCategoryDefinition(
            Construction, "Будматеріали",
            new[]
            {
                new SupplierItemCategoryField("unit", "Одиниця виміру", SupplierItemFieldType.Text, Required: true),
                new SupplierItemCategoryField("package_weight_volume", "Вага/об'єм упаковки", SupplierItemFieldType.Text, Required: false),
                new SupplierItemCategoryField("certification_class", "Клас сертифікації", SupplierItemFieldType.Text, Required: false),
            }),
    };

    private static readonly IReadOnlyDictionary<string, SupplierItemCategoryDefinition> ByKey =
        All.ToDictionary(c => c.Key, c => c);

    public static SupplierItemCategoryDefinition? Find(string key) =>
        ByKey.TryGetValue(key, out var def) ? def : null;

    /// <summary>
    /// Validates category-specific attributes. Returns an empty list when valid.
    /// <c>category == null</c> is always valid regardless of attributes (ADR-017 §5 —
    /// backward compat, items without a category skip validation entirely, permanently).
    /// An unknown category key is rejected with a single error.
    /// </summary>
    public static List<string> Validate(string? category, Dictionary<string, object?>? attributes)
    {
        var errors = new List<string>();

        if (category is null)
            return errors;

        var definition = Find(category);
        if (definition is null)
        {
            errors.Add($"Невідома категорія товару: '{category}'.");
            return errors;
        }

        foreach (var field in definition.Fields)
        {
            if (!field.Required) continue;

            var hasValue =
                attributes is not null &&
                attributes.TryGetValue(field.Key, out var value) &&
                value is not null &&
                !(value is string s && string.IsNullOrWhiteSpace(s));

            if (!hasValue)
                errors.Add($"Поле '{field.LabelUa}' обов'язкове для категорії '{definition.LabelUa}'.");
        }

        return errors;
    }
}
