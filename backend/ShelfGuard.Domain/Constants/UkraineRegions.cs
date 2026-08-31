namespace ShelfGuard.Domain.Constants;

/// <summary>
/// A single Ukraine region: an oblast-level administrative unit or a major city.
/// </summary>
/// <param name="Code">
/// Stable region code. Oblast-level units use ISO 3166-2:UA (e.g. <c>UA-32</c>);
/// cities use <c>{oblastCode}-{TRANSLIT}</c> (e.g. <c>UA-18-ZHYTOMYR</c>).
/// </param>
/// <param name="NameUa">Ukrainian display name.</param>
/// <param name="Kind"><c>"oblast"</c> or <c>"city"</c>.</param>
/// <param name="ParentCode">Owning oblast code for a city; <c>null</c> for an oblast.</param>
public sealed record RegionDefinition(string Code, string NameUa, string Kind, string? ParentCode);

/// <summary>
/// Backend source-of-truth registry of Ukraine regions (oblast-level units + major cities),
/// mirrored on <see cref="SupplierItemCategories"/>. The frontend and mobile clients render
/// region pickers from <c>GET /api/geo/regions</c> instead of hardcoding this list, so this
/// registry — and the <see cref="Validate"/> / <see cref="TryMatchFreeText"/> helpers below —
/// is the only place the taxonomy lives.
///
/// <para>
/// Oblast codes are ISO 3166-2:UA. <b>UA-30 is the city of Kyiv (м. Київ); UA-32 is Kyiv oblast
/// (Київська область)</b> — a classic confusion point, keep them distinct everywhere. UA-30 has
/// no separate <c>city</c> row (the code already is the city).
/// </para>
/// <para>
/// UA-40 (Севастополь) and UA-43 (Автономна Республіка Крим) are included with these neutral
/// administrative labels purely so a supplier can explicitly mark them "not served" in delivery
/// coverage. This registry encodes no political status.
/// </para>
/// </summary>
public static class UkraineRegions
{
    public const string KindOblast = "oblast";
    public const string KindCity   = "city";

    public static readonly IReadOnlyList<RegionDefinition> All = new[]
    {
        // ── 27 ISO 3166-2:UA oblast-level units ──────────────────────────────
        new RegionDefinition("UA-05", "Вінницька", KindOblast, null),
        new RegionDefinition("UA-07", "Волинська", KindOblast, null),
        new RegionDefinition("UA-09", "Луганська", KindOblast, null),
        new RegionDefinition("UA-12", "Дніпропетровська", KindOblast, null),
        new RegionDefinition("UA-14", "Донецька", KindOblast, null),
        new RegionDefinition("UA-18", "Житомирська", KindOblast, null),
        new RegionDefinition("UA-21", "Закарпатська", KindOblast, null),
        new RegionDefinition("UA-23", "Запорізька", KindOblast, null),
        new RegionDefinition("UA-26", "Івано-Франківська", KindOblast, null),
        new RegionDefinition("UA-30", "м. Київ", KindOblast, null),
        new RegionDefinition("UA-32", "Київська", KindOblast, null),
        new RegionDefinition("UA-35", "Кіровоградська", KindOblast, null),
        new RegionDefinition("UA-40", "Севастополь", KindOblast, null),
        new RegionDefinition("UA-43", "Автономна Республіка Крим", KindOblast, null),
        new RegionDefinition("UA-46", "Львівська", KindOblast, null),
        new RegionDefinition("UA-48", "Миколаївська", KindOblast, null),
        new RegionDefinition("UA-51", "Одеська", KindOblast, null),
        new RegionDefinition("UA-53", "Полтавська", KindOblast, null),
        new RegionDefinition("UA-56", "Рівненська", KindOblast, null),
        new RegionDefinition("UA-59", "Сумська", KindOblast, null),
        new RegionDefinition("UA-61", "Тернопільська", KindOblast, null),
        new RegionDefinition("UA-63", "Харківська", KindOblast, null),
        new RegionDefinition("UA-65", "Херсонська", KindOblast, null),
        new RegionDefinition("UA-68", "Хмельницька", KindOblast, null),
        new RegionDefinition("UA-71", "Черкаська", KindOblast, null),
        new RegionDefinition("UA-74", "Чернігівська", KindOblast, null),
        new RegionDefinition("UA-77", "Чернівецька", KindOblast, null),

        // ── 24 major cities (oblast centres; UA-30 = Kyiv has no city row) ────
        new RegionDefinition("UA-05-VINNYTSIA", "Вінниця", KindCity, "UA-05"),
        new RegionDefinition("UA-07-LUTSK", "Луцьк", KindCity, "UA-07"),
        new RegionDefinition("UA-09-LUHANSK", "Луганськ", KindCity, "UA-09"),
        new RegionDefinition("UA-12-DNIPRO", "Дніпро", KindCity, "UA-12"),
        new RegionDefinition("UA-12-KRYVYI-RIH", "Кривий Ріг", KindCity, "UA-12"),
        new RegionDefinition("UA-14-DONETSK", "Донецьк", KindCity, "UA-14"),
        new RegionDefinition("UA-18-ZHYTOMYR", "Житомир", KindCity, "UA-18"),
        new RegionDefinition("UA-21-UZHHOROD", "Ужгород", KindCity, "UA-21"),
        new RegionDefinition("UA-23-ZAPORIZHZHIA", "Запоріжжя", KindCity, "UA-23"),
        new RegionDefinition("UA-26-IVANO-FRANKIVSK", "Івано-Франківськ", KindCity, "UA-26"),
        new RegionDefinition("UA-35-KROPYVNYTSKYI", "Кропивницький", KindCity, "UA-35"),
        new RegionDefinition("UA-46-LVIV", "Львів", KindCity, "UA-46"),
        new RegionDefinition("UA-48-MYKOLAIV", "Миколаїв", KindCity, "UA-48"),
        new RegionDefinition("UA-51-ODESA", "Одеса", KindCity, "UA-51"),
        new RegionDefinition("UA-53-POLTAVA", "Полтава", KindCity, "UA-53"),
        new RegionDefinition("UA-56-RIVNE", "Рівне", KindCity, "UA-56"),
        new RegionDefinition("UA-59-SUMY", "Суми", KindCity, "UA-59"),
        new RegionDefinition("UA-61-TERNOPIL", "Тернопіль", KindCity, "UA-61"),
        new RegionDefinition("UA-63-KHARKIV", "Харків", KindCity, "UA-63"),
        new RegionDefinition("UA-65-KHERSON", "Херсон", KindCity, "UA-65"),
        new RegionDefinition("UA-68-KHMELNYTSKYI", "Хмельницький", KindCity, "UA-68"),
        new RegionDefinition("UA-71-CHERKASY", "Черкаси", KindCity, "UA-71"),
        new RegionDefinition("UA-74-CHERNIHIV", "Чернігів", KindCity, "UA-74"),
        new RegionDefinition("UA-77-CHERNIVTSI", "Чернівці", KindCity, "UA-77"),
    };

    private static readonly IReadOnlyDictionary<string, RegionDefinition> ByCode =
        All.ToDictionary(r => r.Code, r => r);

    /// <summary>Free-text tokens stripped before matching (lowercase).</summary>
    private static readonly string[] FreeTextNoise = { "область", "обл.", "обл", "місто", "м." };

    /// <summary>
    /// Explicit aliases checked before name matching. Keys are already normalized
    /// (lowercased, noise-stripped, whitespace-collapsed).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> FreeTextAliases = new Dictionary<string, string>
    {
        ["київська"]       = "UA-32",
        ["київ"]           = "UA-30",
        ["м. київ"]        = "UA-30",
        ["місто київ"]     = "UA-30",
        ["дніпро"]         = "UA-12",
        ["дніпропетровськ"] = "UA-12",
        ["крим"]           = "UA-43",
        ["ар крим"]        = "UA-43",
    };

    private static readonly IReadOnlyDictionary<string, string> ByNormalizedOblastName =
        All.Where(r => r.Kind == KindOblast)
           .GroupBy(r => NormalizeFreeText(r.NameUa.ToLowerInvariant()))
           .ToDictionary(g => g.Key, g => g.First().Code);

    private static readonly IReadOnlyDictionary<string, string> ByNormalizedCityName =
        All.Where(r => r.Kind == KindCity)
           .GroupBy(r => NormalizeFreeText(r.NameUa.ToLowerInvariant()))
           .ToDictionary(g => g.Key, g => g.First().Code);

    /// <summary>Exact region lookup by code. Returns <c>null</c> for an unknown code.</summary>
    public static RegionDefinition? Find(string code) =>
        code is not null && ByCode.TryGetValue(code, out var def) ? def : null;

    /// <summary>True when <paramref name="code"/> is a known oblast or city code.</summary>
    public static bool IsValid(string code) =>
        code is not null && ByCode.ContainsKey(code);

    /// <summary>
    /// Validates a set of region codes. Returns an empty list when all are known;
    /// otherwise one error string per unknown/blank code. Mirrors
    /// <see cref="SupplierItemCategories.Validate"/>'s shape.
    /// </summary>
    public static List<string> Validate(IEnumerable<string>? codes)
    {
        var errors = new List<string>();
        if (codes is null)
            return errors;

        foreach (var code in codes)
        {
            if (string.IsNullOrWhiteSpace(code) || !IsValid(code))
                errors.Add($"Невідомий код регіону: '{code}'.");
        }

        return errors;
    }

    /// <summary>
    /// Best-effort mapping of free-text region strings (legacy <c>DeliveryRegions</c>,
    /// manual input) to a region code. Lowercases, trims, strips «область»/«обл.»/«обл»/
    /// «місто»/«м.», then matches an explicit alias, an already-valid code, an oblast name,
    /// or a city name — in that order. Returns <c>null</c> when nothing matches (expected for
    /// entries like «Вся Україна» / «за домовленістю»). Kept deliberately simple and
    /// predictable for the one-shot backfill task.
    /// </summary>
    public static string? TryMatchFreeText(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var lower      = raw.Trim().ToLowerInvariant();
        var normalized = NormalizeFreeText(lower);

        if (FreeTextAliases.TryGetValue(lower, out var aliasCode))
            return aliasCode;
        if (FreeTextAliases.TryGetValue(normalized, out aliasCode))
            return aliasCode;

        var upper = raw.Trim().ToUpperInvariant();
        if (IsValid(upper))
            return upper;

        if (ByNormalizedOblastName.TryGetValue(normalized, out var oblastCode))
            return oblastCode;

        if (ByNormalizedCityName.TryGetValue(normalized, out var cityCode))
            return cityCode;

        return null;
    }

    private static string NormalizeFreeText(string lower)
    {
        var s = lower;
        foreach (var noise in FreeTextNoise)
            s = s.Replace(noise, " ");

        return string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
