import type { MetadataRoute } from "next";
import { PUBLIC_PATHS, SITE_URL, localizedPath } from "@/lib/site";

// No lastModified on purpose: no reliable content-change date is available,
// and a per-build "now" would be misleading (SEO-03).
export default function sitemap(): MetadataRoute.Sitemap {
  return PUBLIC_PATHS.flatMap((path) => {
    const uk = `${SITE_URL}${localizedPath("uk", path)}`;
    const en = `${SITE_URL}${localizedPath("en", path)}`;
    const languages = { uk, en, "x-default": uk };
    return [
      { url: uk, alternates: { languages } },
      { url: en, alternates: { languages } },
    ];
  });
}
