// Single source of truth for the public site origin (SEO-02/03). Override with
// NEXT_PUBLIC_SITE_URL when the domain changes; never hardcode the host elsewhere.
export const SITE_URL = (process.env.NEXT_PUBLIC_SITE_URL ?? "https://agrusystems.pp.ua").replace(
  /\/+$/,
  "",
);

// Indexable public routes (unprefixed = uk, `/en` prefix = en).
export const PUBLIC_PATHS = ["/", "/features", "/how-it-works", "/for-whom", "/retail", "/roadmap"];

export function localizedPath(locale: string, path: string): string {
  if (locale === "en") return path === "/" ? "/en" : `/en${path}`;
  return path;
}
