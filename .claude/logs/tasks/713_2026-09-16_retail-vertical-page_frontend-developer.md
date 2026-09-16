# TASK-713 — Retail industry-vertical marketing page

**Status:** review · main session (frontend-developer role) · not pushed

**Scope cut mid-task (user correction):** the original brief asked for 4 vertical pages
(retail/auto-service/production/warehouses). Partway through — after research/planning
but **before any files were written** — the user corrected scope: only Retail is finished
enough in the product to publish a marketing page for; auto-service/production/warehouses
are future work once that product work actually lands, and publishing pages for them now
would misrepresent the product. Only Retail was built. No auto-service/production/warehouses
files, i18n keys, or nav entries were ever created, so there was nothing to revert.

## Changes

- `frontend/app/[locale]/retail/page.tsx` — new page, same SSG pattern as the homepage
  (`generateStaticParams` for both locales, `generateMetadata` with its own title/description/
  OG image, same `metadataBase`, `setRequestLocale`, `data-landing` wrapper + `LandingHeader`/
  `LandingFooter`). Composes: Hero → feature grid → 4-step "how it works" walkthrough → short
  back-to-home cross-link → `LeadSection` (reused unmodified, same `/api/public/leads` POST,
  no backend change).
- New components in `frontend/features/landing/components/`: `RetailHeroSection.tsx`,
  `RetailFeaturesSection.tsx`, `RetailExampleSection.tsx`, `RetailBackHomeSection.tsx` — all
  reuse the existing visual system (`Reveal`, `BrowserFrame`, same card/step styling as
  `FeaturesSection`/`HowItWorksSection`). Hero reuses `/landing/dashboard-2.jpg` (store zone
  map) instead of the homepage's `dashboard-1.jpg`, so the two heroes don't look identical.
- `frontend/features/landing/components/LandingHeader.tsx` — added one locale-aware nav link
  ("Мережі магазинів" / "Retail chains") to both the desktop nav row and the mobile drawer,
  using the already-imported `Link as LocaleLink` from `@/i18n/navigation` (not a plain `<a>`,
  since it's a real route, not an in-page anchor). The existing 4 anchor links
  (#features/#how-it-works/#pricing/#faq) were left untouched, per brief.
- `frontend/messages/{uk,en}.json` — `Landing.header.retailNavLabel` + new
  `Landing.verticals.retail.{meta,hero,features,example,backHome}` namespace (uk written
  first as real Ukrainian copy, en as a proper translation, not placeholder). Content grounded
  in v4-spec.md's Inventory (FEFO/batches/transfers) + Procurement (suppliers/AI reordering) +
  POS (sales/PRRO) modules, reframed for a chain operator. No fabricated customer names,
  testimonials, or growth stats anywhere — confirmed against the brief's explicit constraint.
- `frontend/middleware.ts` — **bug found during verification, fixed in scope**: the custom
  middleware wrapper only routes an allowlist of paths (`INTL_PATH_PREFIXES`) through
  next-intl's locale middleware; `/retail` wasn't in it, so the unprefixed (uk, default-locale)
  URL 404'd while `/en/retail` worked. Added `/retail` to the allowlist + a comment warning
  that any future top-level page under `app/[locale]/` needs the same treatment.
- `.claude/docs/frontend-structure.md` — routes table: added `/retail` row only, with a note
  that the other 3 verticals were intentionally not built.

## Verification

- `npx tsc --noEmit` — clean.
- `npm run build` — exit 0. New route present for both locales (`/[locale]/retail` →
  `/uk/retail`, `/en/retail` in the build's internal listing, served as `/retail` and
  `/en/retail` respectively at runtime). Build output has repeated non-fatal
  `ENVIRONMENT_FALLBACK` messages during static generation — pre-existing in this repo across
  many prior frontend task logs (e.g. TASK-628), unrelated to this change.
- Browser (local `next dev`, both viewport sizes):
  - `/retail` (uk, unprefixed) and `/en/retail` both render correctly end-to-end: hero
    (headline + screenshot), 6-card feature grid, 4-step example, back-to-home link (→ `/`),
    `LeadSection` form, footer.
  - Header nav verified from both `/` and `/retail`, desktop row and mobile drawer: new
    "Мережі магазинів"/"Retail chains" link present, correct `href="/retail"` (canonical
    unprefixed form when current locale is uk), navigates correctly, mobile drawer closes on
    click.
  - Language switch (UA/EN) verified from the retail page in both directions.
  - Noted but out of scope: switching locale via the header's UA/EN pill lands on a
    prefixed `/uk/...` URL instead of self-canonicalizing to the unprefixed form — confirmed
    this is pre-existing behavior on the homepage too (not introduced by this task), content
    still renders correctly either way, not fixed here.
  - A transient client-side redirect loop between `/en` and `/en/retail` appeared once, right
    after editing `middleware.ts` while the dev server was already running — resolved by a
    clean server restart (stale middleware hot-reload, not a real app bug); did not reappear.
- Lead form submission itself not exercised (no backend running in this session) — form is
  `LeadSection`/`LeadForm` reused verbatim, unchanged from the already-shipped homepage.

## Not built (by design, per corrected scope)

`/auto-service`, `/production`, `/warehouses` vertical pages, their i18n namespaces, and any
header nav entries for them — deferred until those product areas are actually ready to market.
