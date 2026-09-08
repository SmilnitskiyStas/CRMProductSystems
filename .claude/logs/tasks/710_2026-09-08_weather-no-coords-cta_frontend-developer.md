# TASK-710 — Weather calendar: prominent "no coordinates" callout + deep-link CTA

**Status:** review · main session · verified in browser · follow-up to TASK-708

User feedback on the deployed weather feature: opened `/events`, no temperatures, and the
"no coordinates" hint was a tiny grey line (easy to miss, cropped off their screenshot). They
asked to make it prominent + add a button that jumps straight to setting the coordinates.

## Changes (frontend only)

- `frontend/app/(dashboard)/events/page.tsx`
  - `weatherHint` is now a discriminated value (`{kind:"pickOne"}` | `{kind:"noCoords", locationId, locationName}` | null) instead of a plain string.
  - `pickOne` → unchanged subtle `<p>` (that path is a normal "you're viewing all stores" state).
  - `noCoords` → a callout box (`#1E293B`/`#334155`, MapPin icon): "Щоб бачити погоду в календарі, задайте координати локації «{name}»." + either a blue **«Задати координати»** link → `/locations?edit=<locationId>` (roles that can edit locations — `AT_LEAST_ENTERPRISE_ADMIN`, mirrors `LocationsPage.canManageLocations`) or a muted "Координати додає адміністратор компанії" line for lower roles.
- `frontend/app/(dashboard)/locations/page.tsx`
  - `useSearchParams`/`useRouter` (same pattern as `users/page.tsx`'s `?tab=`). New `useEffect`:
    `/locations?edit=<id>` opens that location's edit dialog once (guarded on `canManageLocations`,
    `dialog === null`, `locations` loaded), then `router.replace("/locations")` drops the param.
- `frontend/messages/{uk,en}.json` — `Dashboard.events.weather`: removed the now-unused
  `hintNoCoords`, added `noCoordsBody` (`{location}`), `noCoordsCta`, `noCoordsNoPermission`. Parity kept.

## Verification

- `npx tsc --noEmit` clean · `npm run lint` clean · `npx next build` ok (`/events` 16.7 kB, `/locations` 8.58 kB).
- Browser (local stack, store «Свіжий Кут» selected):
  - coords nulled → callout renders with the location name + "Coordinates are added by a company admin" (session was store_manager).
  - coords restored → callout gone, temperatures back on the cells.
  - `/locations` still loads with the new `useSearchParams` code.
  - The enterprise-admin CTA + auto-open-dialog path was code-reviewed against the `users/page.tsx`
    `useSearchParams` pattern, not click-tested (session stayed logged in as store_manager; the
    dev backend keeps refreshing the session).

No backend change, no i18n key outside the weather block.
