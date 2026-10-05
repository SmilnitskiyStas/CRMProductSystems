# TASK-721 - Landing leads in provider admin + GA4 with consent

Branch `feat/leads-admin-and-ga` (not committed). Status: review.

## Backend
- `LandingLead` + attribution columns (PageUrl, Locale, Referrer, Utm*, ProcessedAt, ProcessedByUserId, AdminNote), all nullable. No IP stored.
- Migration `20261005070923_ExtendLandingLeadsAttribution` (ADD COLUMN only; table has no RLS/grants, ownership unchanged). Also index on IsProcessed. Applied to local dev DB as user `crm`.
- `LandingLeadService`: source whitelist (landing, retail, features, roadmap, how-it-works, for-whom, join; unknown -> landing), locale whitelist uk/en, metadata truncated (never rejects a lead). Persistence failure is logged with full lead details (`LEAD NOT SAVED`) and rethrown -> 5xx (not 204). TODO replaced: admin menu badge is the notification channel.
- New `ProviderLeadsController` `api/provider/leads`: GET list (status all|new|processed, search name/phone/company via ILIKE, page/pageSize<=100, newest first), GET `count`, PATCH `{id}` (isProcessed, adminNote; records ProcessedAt/By). Logic in `Features/Leads`.
- Auth decision: `AppPolicies.ProviderTeamMember` (provider, provider_admin, provider_agent) - the same policy as ProviderTeamController/AdminServiceDesk. Backend has no per-permission attribute for client management (view_clients/manage_clients are enforced in UI only), so I did not invent one; UI gates the menu item with `["view_clients","manage_clients"]` like `/provider`.
- Tests: LandingLeadServiceTests (+9: attribution, whitelist, db failure propagates, list/count/update) and `LandingLeadRepositoryIntegrationTests` (real Postgres flow). `dotnet test --filter LandingLead`: 27 passed.

## Frontend
- `submitLead` sends source/pageUrl/locale/referrer/utm*; on network/5xx the payload is parked in localStorage (`sg_pending_lead`), form data stays, button turns into "Try again", and a resend/discard banner shows on next visit. 400/429 as before. `generate_lead` GA event with `{source}` only.
- `/provider/leads` page + `LeadsPanel` (cards, tabs New/Processed/All, debounced search, mark processed/unprocessed, note editing, pagination), hooks `useProviderLeads`, api `providerLeads.ts`, types in `features/provider/types.ts`.
- Sidebar: "Заявки" item in admin group, red badge with unprocessed count (60s poll, hidden at 0), only with view_clients/manage_clients.
- i18n: `Dashboard.providerLeads.*`, `Dashboard.sidebar.groups.admin.leads`, `Landing.cookies.*`, `Landing.footer.cookieSettings`, `Landing.leadForm.{retry,pending*}` in uk + en.
- GA4: `features/landing/lib/analytics.ts` (`trackEvent`, consent store), `GoogleAnalytics` (next/script afterInteractive, Consent Mode v2 defaults denied before config, send_page_view false, SPA page_view only with consent), `CookieBanner` (equal Accept/Decline, localStorage `sg_cookie_consent`), footer `CookieSettingsLink`. Mounted in `app/[locale]/layout.tsx` only. All no-op when `NEXT_PUBLIC_GA_ID` is empty.
- Build plumbing: Dockerfile ARG/ENV, `docker-compose.production.yml` + `docker-compose.staging.yml` build arg, `.env.production.example`.

## Verification
`tsc --noEmit` clean, `npm run lint` clean, `npm run build` OK with and without `NEXT_PUBLIC_GA_ID` (public routes still SSG).

## Deploy notes
- Server `.env` must contain `NEXT_PUBLIC_GA_ID=G-XXXXXXXXXX` BEFORE the deploy builds the web image (baked at build time; changing it later needs a web rebuild). Empty = analytics and banner fully off.
- Migration is additive; deploy.sh applies it as usual. Regenerate `openapi.json` if the project keeps it in sync (new provider leads endpoints).
- Cookie banner copy mentions Google Analytics; no separate privacy page added.
