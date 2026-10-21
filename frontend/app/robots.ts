import type { MetadataRoute } from "next";
import { SITE_URL } from "@/lib/site";

// Service pages (/login, ...) stay crawlable on purpose so bots can read their
// noindex header; authenticated areas are protected by auth, not by robots.
export default function robots(): MetadataRoute.Robots {
  return {
    rules: { userAgent: "*", allow: "/", disallow: ["/api/"] },
    sitemap: `${SITE_URL}/sitemap.xml`,
  };
}
