# Offline read-cache foundation (TASK-461)

This module implements ADR-025's persisted, read-only React Query boundary. It does not queue,
replay, or authorize mutations and it does not implement full offline POS.

## Initial allowlist

- `['schedules', locationId?, weekStart?]`: schedule list summaries; 24-hour soft TTL.
- `['marketplace-suppliers', normalizedFilters]`: public supplier list summaries; 6-hour soft TTL.
- `['production-recipes', includeInactive]`: recipe list summaries; 24-hour soft TTL.

All other query families are denied by default. In particular, auth/session/2FA/recovery data,
permissions/modules, customer PII, loyalty/QR values, stock/batches, POS shift/payment/prices,
notifications, query mutations and detail payloads are not persisted. Each allowed family has a
field-level serializer; unexpected fields are discarded before AsyncStorage is reached.

## Lifecycle and limits

Storage keys include schema version, production environment, tenant ID and user ID. Cache hydration
starts only after the authenticated staff owner is known and completes before that identity is
exposed to private routes. Switching owner clears in-memory React Query state immediately. Explicit
logout and terminal session cleanup remove only the current owner's namespace; a SecureStore owner
pointer permits cleanup when `/auth/me` rejects a restored token before the user object is loaded.

Entries keep `lastSyncedAt`, soft expiry and seven-day hard retention metadata. Soft-expired entries
remain available for TASK-462 to render as explicitly stale; hard-expired entries are not hydrated.
The limits are 256 KiB per entry and 2 MiB per owner cache. Corrupt, foreign-at-current-key and
schema-incompatible records fail closed. Successful online React Query updates replace persisted
summaries. A NetInfo offline-to-online transition invalidates only allowlisted families; NetInfo is
not considered proof that business submission is safe.

TASK-462 owns screen-level offline/stale/last-updated UX. TASK-463 owns Android+iOS process-death,
backup/storage-pressure, privacy and account-switch device acceptance.

## Screen UX (TASK-462)

`OfflineReadStatus` and `useOfflineReadUx` expose the persisted metadata without copying query data
or server state. The rollout is limited to the schedules list, marketplace supplier list and
production recipe list. Ukrainian status text distinguishes offline cached data, soft-stale data,
online refresh, failed refresh with viewable cache, and offline absence/hard expiry. Every cached
state includes the last successful server timestamp; a fresh successful response clears the status.

Retry is offered only when the device is online. The component is an accessible live alert with a
44-point retry target. Searches, details, `my-shifts`, permissions/modules and all mutation inputs
remain outside the persisted allowlist. Cached data never enables an offline submit.
