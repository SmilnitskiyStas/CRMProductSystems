// WMO weather-interpretation-code buckets (Open-Meteo `weathercode`).
// https://open-meteo.com/en/docs — "Weather variable documentation".
//
// This is a data module: the inline uk/en labels below are the source of truth for
// non-component callers. Components should resolve the label through i18n instead —
// `weatherCodeBucket(code)` + `Dashboard.events.weather.codes.<bucket>` — so no
// Ukrainian string ends up hard-coded in a .tsx (project convention).

export type WeatherBucket =
  | "clear"
  | "partlyCloudy"
  | "cloudy"
  | "fog"
  | "drizzle"
  | "rain"
  | "snow"
  | "showers"
  | "snowShowers"
  | "thunder"
  | "thunderHail";

interface BucketInfo {
  labelUk: string;
  labelEn: string;
  emoji: string;
}

const BUCKETS: Record<WeatherBucket, BucketInfo> = {
  clear: { labelUk: "Ясно", labelEn: "Clear", emoji: "☀️" },
  partlyCloudy: { labelUk: "Мінлива хмарність", labelEn: "Partly cloudy", emoji: "🌤️" },
  cloudy: { labelUk: "Хмарно", labelEn: "Cloudy", emoji: "☁️" },
  fog: { labelUk: "Туман", labelEn: "Fog", emoji: "🌫️" },
  drizzle: { labelUk: "Мряка", labelEn: "Drizzle", emoji: "🌦️" },
  rain: { labelUk: "Дощ", labelEn: "Rain", emoji: "🌧️" },
  snow: { labelUk: "Сніг", labelEn: "Snow", emoji: "🌨️" },
  showers: { labelUk: "Зливи", labelEn: "Rain showers", emoji: "🌧️" },
  snowShowers: { labelUk: "Снігопад", labelEn: "Snow showers", emoji: "🌨️" },
  thunder: { labelUk: "Гроза", labelEn: "Thunderstorm", emoji: "⛈️" },
  thunderHail: { labelUk: "Гроза з градом", labelEn: "Thunderstorm with hail", emoji: "⛈️" },
};

const CODE_TO_BUCKET: Record<number, WeatherBucket> = {
  0: "clear",
  1: "partlyCloudy",
  2: "partlyCloudy",
  3: "cloudy",
  45: "fog",
  48: "fog",
  51: "drizzle",
  53: "drizzle",
  55: "drizzle",
  56: "drizzle",
  57: "drizzle",
  61: "rain",
  63: "rain",
  65: "rain",
  66: "rain",
  67: "rain",
  71: "snow",
  73: "snow",
  75: "snow",
  77: "snow",
  80: "showers",
  81: "showers",
  82: "showers",
  85: "snowShowers",
  86: "snowShowers",
  95: "thunder",
  96: "thunderHail",
  99: "thunderHail",
};

/** WMO code → coarse bucket, or `null` for an unknown / missing code. */
export function weatherCodeBucket(code: number | null): WeatherBucket | null {
  if (code == null) return null;
  return CODE_TO_BUCKET[code] ?? null;
}

/** Emoji for a bucket — safe to render in a .tsx (it carries no localizable text). */
export function weatherBucketEmoji(bucket: WeatherBucket): string {
  return BUCKETS[bucket].emoji;
}

/** i18n key (relative to `Dashboard.events.weather`) for a bucket's label. */
export function weatherBucketLabelKey(bucket: WeatherBucket): string {
  return `codes.${bucket}`;
}

/**
 * Self-contained WMO code → `{ label, emoji }` lookup for non-component callers.
 * `label` defaults to Ukrainian; pass `"en"` for the English label. Components should
 * prefer i18n (see `weatherCodeBucket` / `weatherBucketLabelKey`).
 */
export function weatherCodeInfo(
  code: number | null,
  locale: "uk" | "en" = "uk",
): { label: string; emoji: string } | null {
  const bucket = weatherCodeBucket(code);
  if (!bucket) return null;
  const info = BUCKETS[bucket];
  return { label: locale === "en" ? info.labelEn : info.labelUk, emoji: info.emoji };
}
