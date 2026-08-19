# QR / deep-link retailer onboarding (TASK-549)

Contract for scanning an in-store QR code (or tapping a shared link) to join a retailer's
loyalty program on the shared consumer mobile app. Two halves:

- **Web fallback** (this repo, `frontend/`) — implemented, live for any visitor without the app.
- **Native deep-link handler** (mobile app) — **not implemented**. This document is the proposed
  contract for the mobile side to register against; it is out of scope for this task and owned by
  the mobile workstream (`.claude/tasks/mobile-roadmap.md`).

## Web fallback URL

```
https://<domain>/join/{slug}
```

`{slug}` is the tenant's public retailer slug (`Tenant.Slug` — case-insensitive lookup, see
`ITenantRepository.GetBySlugAsync`, TASK-548). Route implementation:
`frontend/app/[locale]/join/[slug]/page.tsx`. Locale-aware like the marketing landing page: `uk`
(default) has no URL prefix, `en` is prefixed (`/en/join/{slug}`).

The page renders a public retailer preview by calling the anonymous
`GET /api/v1/retailers/{slug}/public` (TASK-549 backend half,
`.claude/logs/tasks/549_2026-08-18_qr-onboarding-backend_backend-developer.md`):

```json
{ "name": "Свіжий Кут", "slug": "svizhyi-kut", "logoUrl": "https://.../logo.png", "joinable": true }
```

**404 policy — do not try to distinguish cases.** Unknown slug, inactive tenant, missing loyalty
module, and a tenant that has paused its program are all identical `404 { "error": "Retailer not
found." }`, by deliberate backend design (enumeration-safety, TASK-548/549). The web fallback shows
one generic "this link isn't valid / this retailer isn't available right now" state for every 404
and for any network/server error — it never attempts to infer or display *why*. Any future native
handler must follow the same rule: do not build UI that implies a distinction the backend
intentionally does not expose.

The page always offers an "Open in app" action pointing at the deep link below, plus an honest "not
yet available for download" state instead of App Store / Google Play badges — no mobile store
listing exists yet (`.claude/tasks/mobile-roadmap.md` TASK-440 is blocked on store credentials and
approved icon/splash assets). Do not link to a real or placeholder store URL until TASK-440 unblocks
and a real listing exists.

## Proposed native deep-link contract

Not implemented. Proposed for the mobile app to register against on the same `/join/{slug}` path
used by the web fallback, so one QR code / one shared link works whether or not the app is
installed:

| Mechanism | Value |
|---|---|
| Custom scheme | `shelfguard://join/{slug}` |
| iOS Universal Links | `https://<domain>/join/{slug}` (associated domain, `apple-app-site-association`) |
| Android App Links | `https://<domain>/join/{slug}` (`assetlinks.json`, verified intent filter) |

Universal/App Links are the preferred mechanism where supported (no interstitial, works even before
first app install on iOS/Android via the Play Store deferred-deep-link flow); the custom scheme is
the fallback for contexts that only support `x-scheme://` handling. The web fallback's "Open in app"
button currently links to the custom scheme only (`buildDeepLink()` in `page.tsx`) since no native
handler exists yet to receive a Universal/App Link — this should be revisited once TASK-440's store
listings exist and Universal/App Link association files can be published.

## Expected end-to-end flow

1. **Open app** — user scans the QR code or taps the shared link.
   - App installed + link handled natively → straight to step 2 inside the app.
   - App not installed, or link falls through to the browser → web fallback renders (this task).
2. **Resolve tenant by slug** — `GET /api/v1/retailers/{slug}/public` (anonymous). Native and web
   both use this exact endpoint/contract for the pre-auth preview step; do not add a second,
   parallel "preview" endpoint.
3. **Show retailer** — name, logo, join CTA. 404/error → generic unavailable state (see policy
   above), not a crash and not a distinguishing message.
4. **Join** — requires an authenticated consumer session. Goes through the existing, unchanged,
   auth-required `POST /api/v1/retailers/{slug}/join` (TASK-548). The anonymous `/public` endpoint
   is strictly the pre-auth "who is this retailer" preview — it never performs the join itself. If
   the visitor has no consumer session yet, the native app is expected to route through its normal
   consumer registration/login before calling `join`; the web fallback does not attempt this (it has
   no consumer auth UI) and stops at "Open in app" / "join from the app" messaging.
5. **Set active** — after a successful join, the app is expected to make this tenant the active
   retailer context for the session (existing multi-tenant "preferred store"/active-tenant behavior
   already used elsewhere in the consumer app — out of scope to redefine here).

## Out of scope here

- Any native mobile code, deep-link registration, or Universal/App Link association files —
  tracked separately under the mobile roadmap, not this task.
- Changing `POST /api/v1/retailers/{slug}/join` or its auth requirement.
- A real App Store / Google Play listing — blocked on TASK-440.
