# TASK-738 — SEO-основа публічного сайту (ТЗ ShelfGuard_SEO_TZ_2026-10-20)

Гілка `claude/seo-landing-c418e4`, не закомічено.

Зроблено (frontend):
- SEO-01: `middleware.ts` — `/uk` і `/uk/*` → 308 на безпрефіксний URL (query зберігається), до intl-rewrite.
- SEO-02: `features/landing/page-meta.ts` — абсолютний self-canonical, hreflang uk/en/x-default, og:url, twitter; головна і /retail переведені на нього. `alternateLinks: false` у `i18n/routing.ts` (прибрано HTTP Link hreflang, щоб не суперечив HTML).
- SEO-03/04: `app/sitemap.ts` (6 шляхів × 2 мови, без lastmod), `app/robots.ts` (+Sitemap, disallow /api/).
- SEO-04: `X-Robots-Tag: noindex` у middleware для всього поза публічними сторінками (login, forgot-password, dashboard). `/join/*` уже мав noindex.
- SEO-06: JSON-LD на головній (Organization, WebSite, SoftwareApplication, без цін/рейтингів).
- Домен: `lib/site.ts` (`NEXT_PUBLIC_SITE_URL`, дефолт agrusystems.pp.ua).

Перевірка: tsc ✔, lint ✔, build ✔; на `next start`: /uk→308, robots/sitemap 200, canonical/hreflang/og:url узгоджені.

Не зроблено: SSR `<html lang>` для /en (root layout client, lang виставляється ефектом); BreadcrumbList; Lighthouse-заміри; canonical www/HTTP→HTTPS (nginx, на проді).
