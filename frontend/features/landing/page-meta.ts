import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { SITE_URL, localizedPath } from "@/lib/site";

// Shared generateMetadata body for the public landing pages. `namespace` is the
// "Landing.…meta" key group, `path` the unprefixed route ("/" for home).
// Emits a self-referencing absolute canonical, uk/en/x-default alternates and
// og:url on the same scheme (SEO-02).
export async function buildPageMetadata(
  locale: string,
  namespace: string,
  path: string,
): Promise<Metadata> {
  const t = await getTranslations({ locale, namespace });
  const tSite = await getTranslations({ locale, namespace: "Landing.meta" });
  const url = `${SITE_URL}${localizedPath(locale, path)}`;

  return {
    metadataBase: new URL(SITE_URL),
    title: t("title"),
    description: t("description"),
    alternates: {
      canonical: url,
      languages: {
        uk: `${SITE_URL}${localizedPath("uk", path)}`,
        en: `${SITE_URL}${localizedPath("en", path)}`,
        "x-default": `${SITE_URL}${localizedPath("uk", path)}`,
      },
    },
    openGraph: {
      title: t("title"),
      description: t("description"),
      url,
      siteName: tSite("ogSiteName"),
      locale: locale === "en" ? "en_US" : "uk_UA",
      type: "website",
      images: [
        {
          url: "/landing/dashboard-1.jpg",
          width: 1280,
          height: 574,
          alt: tSite("ogImageAlt"),
        },
      ],
    },
    twitter: {
      card: "summary_large_image",
      title: t("title"),
      description: t("description"),
    },
  };
}
