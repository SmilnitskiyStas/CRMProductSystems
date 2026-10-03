# TASK-719 — Landing redesign + /features /roadmap /how-it-works /for-whom (frontend)

Status: review · frontend-only · not committed

## Changed
- New SSG pages (uk + en): `app/[locale]/{features,roadmap,how-it-works,for-whom}/page.tsx`; `middleware.ts` INTL_PATH_PREFIXES extended.
- New components in `features/landing/components/`: Section (Container/Section/SectionHeading/PageHero/TeaserLink/StatusBadge), FeatureGroup, RoadmapColumns, StepsTimeline, VerticalCard, PageCtaStrip, LandingShell, TrustStrip, MiniRoadmapSection; helper `features/landing/page-meta.ts`.
- Homepage: stronger hero + trust strip (FEFO, Checkbox, AI, IoT), teasers linking to new pages, mini-roadmap section; Loyalty/MarketingAnalytics/Production sections removed from the homepage (covered on /features; component files kept).
- Header: page links (Features, How it works, For whom, Roadmap, Pricing), desktop nav from lg, mobile drawer; footer: brand + 3 nav columns + contact + language/login.
- `landing.css`: palette as `--l-*` CSS variables.
- Messages: `Landing.header.nav`, `Landing.footer`, `Landing.trust`, `Landing.teasers`, `Landing.miniRoadmap`, `Landing.pages.*` (uk + en).

## Content rules
Only shipped capabilities; no clients/numbers/dates/prices. Roadmap "In progress"/"Planned" driven by `Landing.pages.roadmap.columns.{inProgress,planned}.items` (currently `[]`, columns hidden until filled; items `{title, text?}`).

## Verify
`tsc --noEmit` clean, `npm run lint` clean, `npm run build` OK (all new routes SSG for uk/en). No visual browser pass done.
