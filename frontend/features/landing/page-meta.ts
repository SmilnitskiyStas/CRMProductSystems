import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";

// Shared generateMetadata body for the simple landing sub-pages
// (/features, /roadmap, /how-it-works, /for-whom). `namespace` is the
// "Landing.pages.<page>.meta" key group, `path` the unprefixed route.
export async function buildPageMetadata(
  locale: string,
  namespace: string,
  path: string,
): Promise<Metadata> {
  const t = await getTranslations({ locale, namespace });
  const tSite = await getTranslations({ locale, namespace: "Landing.meta" });

  return {
    metadataBase: new URL("https://agrusystems.pp.ua"),
    title: t("title"),
    description: t("description"),
    openGraph: {
      title: t("title"),
      description: t("description"),
      url: locale === "en" ? `/en${path}` : path,
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
  };
}
