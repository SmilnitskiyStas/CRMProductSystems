# Stage 13 — Caching & Offline Behavior

Date: 2026-08-18

## Delivered

- Extended the existing tenant-scoped last-valid mobile-config cache with a `cachedAt` timestamp.
- The cached document already contains the complete validated configuration, including tenant
  identity, theme, navigation, features, and server-driven pages.
- Preserved backward compatibility with legacy config-only cache entries; they remain readable
  without inventing an inaccurate timestamp.
- Every cache read revalidates the document and tenant ID. Corrupt, incompatible, and cross-tenant
  entries are removed.
- A successful fresh load atomically replaces the same tenant's cache.
- A failed load opens the same tenant's last-valid config; if none exists, mobile uses a clearly
  distinguished safe default.
- Added a non-blocking bottom banner for cached-offline, failed-refresh, and no-cache safe-default
  states. Navigation, theme, and content rendering remain usable behind it.
- The banner includes the last successful update time when known and does not flash during initial
  loading.
- Preview drafts remain entirely outside the production last-valid cache.

## Compatibility and isolation

Cache keys remain tenant-scoped and versioned. Tenant switching cannot render another tenant's
cached theme or navigation. Existing cache entries from before Stage 13 are migrated lazily on the
next successful fetch.

## Verification

- `npm run type-check` — passed.
- `npm run lint -- --quiet` — passed with no errors.
- `npm run test:ci` — 52 suites and 224 tests passed.
- `npx expo export --platform android --output-dir .expo-export-stage13` — passed.
- Tests cover timestamp persistence, legacy reads, invalid/cross-tenant cleanup, preview exclusion,
  cached offline messaging, safe-default messaging, and startup-flash suppression.

No dependency or native Expo configuration changed; a native rebuild is not required.

## Remaining production integration

The caching/offline policy is complete, but production published-config loading still depends on
the previously documented schema-v1 alignment. Until that is completed, the provider continues
using the Stage 1 repository path rather than silently accepting an incompatible backend document.

## Next stage

Stage 14 should add a tenant-aware analytics abstraction for the required consumer events, with an
explicit sensitive-field allowlist and no-op/local development transport until ingestion exists.
